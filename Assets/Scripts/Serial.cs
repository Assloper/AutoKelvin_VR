using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine.Audio;
using System.Runtime.ExceptionServices;
using System.Linq;
using Unity.VisualScripting;
using System.IO.Enumeration;

public class Serial : MonoBehaviour
{
    //private string output_data = "";

    public Serial()
    {
    }

    static SerialPort serialPort = new SerialPort();

    List<float> listPPG = new List<float>();
    List<float> tempPPG = new List<float>(); // made by shin

    bool Sync_After = false;
    // 바이트 선언
    byte Packet_TX_Index = 0;
    byte Data_Prev = 0;
    byte PUD0 = 0;
    byte CRD_PUD2_PCDT = 0;
    byte PUD1 = 0;
    byte PacketCount = 0;
    byte PacketCyclicData = 0;
    byte psd_idx = 0;
    static int Ch_Num = 6;
    static int Sample_Num = 1;
    public string output_data;
    byte[] PacketStreamData = new byte[Ch_Num * 2 * Sample_Num];
    private float[] ppgdata;



    public AudioMixer audioMixer;
    public AudioMixerGroup audioMixerGroup;

    private float filterCutoffLow = 0.5f;
    private float filterCutoffHigh = 4.0f;


    int previousPeakIndex = -1;
    //float peakThreshold = 2500f;
    const int windowsSize = 64; // SSF 계산을 위한 창 크기, 1 / 256 * windowssize가 한번에 입력받는 시간(초)
    private float samplingRate = 255f;
    const float InitialThresholdRatio = 0.7f;
    int IntervalSeconds = 3;
    const int BufferSize = 5; // SSF 피크 저장하는 버퍼 크기


    int Parsing_LXDFT2(byte data_crnt)
    {

        int retv = 0;
        if (Data_Prev == 0xFF && data_crnt == 0xFE)
        {
            Sync_After = true;
            Packet_TX_Index = 0;
        }

        Data_Prev = data_crnt;

        if (Sync_After == true)
        {
            Packet_TX_Index++;
            if (Packet_TX_Index > 1)
            {
                if (Packet_TX_Index == 2)
                {
                    PUD0 = data_crnt;
                }
                else if (Packet_TX_Index == 3)
                {
                    CRD_PUD2_PCDT = data_crnt;
                }
                else if (Packet_TX_Index == 4)
                {
                    PacketCount = data_crnt;
                }
                else if (Packet_TX_Index == 5)
                {
                    PUD1 = data_crnt;
                }
                else if (Packet_TX_Index == 6)
                {
                    PacketCyclicData = data_crnt;
                }
                else if (Packet_TX_Index > 6)
                {
                    psd_idx = (byte)(Packet_TX_Index - 7);
                    PacketStreamData[psd_idx] = data_crnt;
                    if (Packet_TX_Index == (Ch_Num * 2 * Sample_Num + 6))
                    {
                        Sync_After = false;
                        retv = 1;
                    }
                }
            }
        }
        return retv;
    }
    // Start is called before the first frame update
    void SerialOpen()
    {
        try
        {
            if (!serialPort.IsOpen)
            {
                serialPort.PortName = "COM4";
                serialPort.BaudRate = 115200;
                serialPort.DataBits = 8;
                serialPort.StopBits = StopBits.One;
                serialPort.Parity = Parity.None;
                serialPort.Open();
            }

        }
        catch (Exception )
        {
            Debug.LogError("시리얼 포트가 연결되지 않았습니다.");
        }

    }

    IEnumerator ReceivePPG()
    {

        while (true)
        {

            int receivedNumber = serialPort.BytesToRead;

            if (receivedNumber > 0)
            {
                byte[] buffer = new byte[receivedNumber];
                 serialPort.Read(buffer, 0, receivedNumber);

                foreach (byte receivedData in buffer)
                {
                    if (Parsing_LXDFT2(receivedData) == 1)
                    {


                        int i = 0;
                        int streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG 데이터

                        listPPG.Add((float)streamdata);

                        float[] ppgArray = listPPG.ToArray();
                        
                        if ((ppgArray.Length - windowsSize + 1) >= 0)
                        {
                            CalculateSSF(ppgArray); // 64개
                        }


                    }
                }
            }

            yield return new WaitForSeconds(1f);
        }

    }

    string filename_SSFPPG = "";
    string filename_RAWPPG = "";
    string filename_PeakPPG = "";
    void Start()
    {
        SerialOpen();
        StartCoroutine(ReceivePPG());
        filename_SSFPPG = Application.dataPath + "/SSFPPG.csv";
        filename_RAWPPG = Application.dataPath + "/RawPPG.csv";

    }

    void Update()
    {
    }
    // SSF (SuperSlopeFilter) 계산 함수
    static float[] CalculateSSF(float[] ssfchange)
    {
        float[] ssf = new float[ssfchange.Length];

        for (int i = 0; i < ssfchange.Length; i++)
        {
            float ssf_i = 0;
            for (int k = 0; k < 64; k++)
            {
                int delta = Math.Abs(k - 64 / 2);
                ssf_i += (ssfchange[(i + k) % ssfchange.Length] - ssfchange[(i - k + ssfchange.Length) % ssfchange.Length]) / (delta + 1);
            }

            ssf[i] = ssf_i;
            Debug.Log("ssf 값: " + ssf[i]);
        }

        return ssf;
    }

    // 맥박 피크를 감지하는 함수
    static List<int> DetectPulsePeaks(double[] ppgSignal, double[] ssfSignal)
    {
        // SSF 시작점과 끝점 찾기
        int ssfOnset = Array.IndexOf(ssfSignal, ssfSignal.Max());
        int ssfOffset = ssfOnset + Array.IndexOf(ssfSignal.Skip(ssfOnset).ToArray(), ssfSignal.Skip(ssfOnset).Min());

        // 해당 범위 내의 맥박 피크 식별
        List<int> pulsePeaks = Enumerable.Range(ssfOnset, ssfOffset - ssfOnset)
            .Where(i => ppgSignal[i] == ppgSignal.Skip(ssfOnset).Take(ssfOffset - ssfOnset).Max())
            .ToList();

        return pulsePeaks;
    }




// PPG신호를 SSF신호로 변환하는 함수
static float[] ConvertToSSF(float[] ppgSignal) //ppg시그널의 Length는 윈도우 사이즈랑 같음 
{
    // SSF 신호를 저장할 배열
    //Debug.Log(ppgSignal.Length);
    float[] ssfSignal = new float[ppgSignal.Length - windowsSize + 1];

    // SSF 계산
    for (int i = 0; i < ssfSignal.Length; i++) //중단점 체크 SSF의 시그널의 길이는 Length랑 같아야한다.
    {
        float sum = 0;
        // 윈도우 크기만큼의 샘플을 합산
        for (int j = 0; j < windowsSize; j++)
        {
            sum += ppgSignal[i + j];
        }

        // 평균 계산하여 SSF에 저장
        ssfSignal[i] = sum/windowsSize;

        //PeakDetection(ssfSignal);
    }
    return ssfSignal;


}



//피크 검출 알고리즘 v1
static List<int> PeakDetect(float[] ppgArray)
    {
        int peakIndex = -1; //피크의 위치값
        List<int> peakIndices = new List<int>(); //peakIndex 저장하는 리스트
        float peakValue = float.MinValue; //피크의 가장 작은 신호값
        float Baseline = CaloulateAverage(ppgArray);


        for (int i = 1; i < ppgArray.Length; i++)
        {
            // 현재 SSFsignal 값
            float value = ppgArray[i];
            // 이전의 SSFsignal 값
            float derivative = value - ppgArray[i - 1];
            //만약에 현재 value가 Baseline 값을 넘었을 때
            if (value > Baseline)
            {
                //만약 현재 peakValue가 Minvalue와 동일하거나 ssf값이 peakvalue 값보다 높을 때
                if (peakValue == float.MinValue || value > peakValue)
                {
                    //현재 값이 이전 피크보다 클 경우 현재 값을 피크로 저장
                    peakIndex = i;
                    peakValue = value;
                }
            }
            // ssfSignal 값이 기준선을 넘지 못하고 peakIndex가 -1이 아닐때
            else if (value < Baseline && peakIndex != -1)
            {
                //이전 피크의 인덱스를 배열에 추가
                peakIndices.Add(peakIndex);
                //피크인덱스 -1로 초기화
                peakIndex = -1;
                peakValue = float.MinValue;

            }

        }
        //만약 PeakIndex가 다를때 
        if (peakIndex != -1)
        {
            //마지막으로 설정된 피크가 있을 경우
            peakIndices.Add(peakIndex);
        }


        return peakIndices;
    }

    //배열의 평균값 계산 함수
    static float CaloulateAverage(float[] values)
    {
        //합계를 저장하는 변수 초기화
        float sum = 0;

        //배열의 각 요소에 반복
        foreach (float value in values)
        {
            sum += value;
        }

        //배열의 모든 요소를 더한 후 평균값 계산
        return sum / values.Length;

    }

    static void RealTimePeakDetection(float[] RTPeak)
    {
        float[] ppgArray = RTPeak.ToArray();
        float[] Threetime = new float[256 *3];


    }

    //실시간 피크 검출 알고리즘
    static void PeakDetection(float[] peakdetec)
    {

        float[] ppgArray = peakdetec.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);
        List<float> ssfPeaksBuffer = new List<float>();

        for (int i = 0; i < ssfSignal.Length; i++)
        {
            float ssfPeak = ssfSignal[i];
            ssfPeaksBuffer.Add(ssfPeak);

            // 버퍼의 크기가 제한 내에 유지되도록
            if (ssfPeaksBuffer.Count > BufferSize)
                ssfPeaksBuffer.RemoveAt(0);

            //임계값을 업데이트
            float threshold = GetAdaptiveThreshold(ssfPeaksBuffer);

            if (ssfPeak > threshold)
            {
                //Debug.log("피크 임계값, 피크 개수")
            }
        }
    }

    // 적응적인 임계값을 얻는 함수
    static float GetAdaptiveThreshold(List<float> ssfPeaksBuffer)
    {
        //초기 임계값을 최대 피크의 백분율로 계산
        float initialThreshold = InitialThresholdRatio * ssfPeaksBuffer.Max();

        //버퍼 정보를 활용하여 임계값 조정
        float adjustedThreshold = initialThreshold;

        return adjustedThreshold;
    }

    //PPG를 어떻게 실시간으로 피크를 검출할 수 있을까.....

    /*void DetectPeaks(float[] data)
    {
        // Peak 검출 알고리즘
        for (int i = 1; i < data.Length; i++)
        {
            float derivative = data[i] - data[i - 1];
            float squaredSignal = derivative * derivative;
            if (data[i] > peakThreshold)
            {
                //peakIndices에 1씩 추가
                peakIndices.Add(i);

                //피크가 2개 이상일때
                if (peakIndices.Count >= 2)
                {
                    int totalPeaks = peakIndices.Count;
                    int currentPeakIndex = peakIndices[totalPeaks - 1];

                    if (previousPeakIndex != -1)
                    {
                        float currentPeakTime = currentPeakIndex / 255f;
                        float previousPeakTime = previousPeakIndex / 255f;
                        float ppi = (currentPeakTime - previousPeakTime) * 1000f;

                        //Debug.Log("PPI: " + ppi + "ms " + "Peak 감지: " + totalPeaks + ", 값 " + data[i]);
                    }

                    //현재 피크를 이전 피크로 설정
                    previousPeakIndex = currentPeakIndex;
                }
            }
        }
        
    }*/


    string RawPPG;


    public void WriteCSVSSF(float[] excel)
    {
        float[] ppgArray = excel.ToArray();
        float[] ssfSignal = CalculateSSF(ppgArray);


        if (ssfSignal.Length > 0)
        {
            TextWriter tw = new StreamWriter(filename_SSFPPG, false);
            tw.WriteLine("SSF PPG");
            tw.Close();

            tw = new StreamWriter(filename_SSFPPG, true);

            for(int i = 0; i < ssfSignal.Length; i++)
            {
                tw.WriteLine(ssfSignal[i]);
            }
            tw.Close();
        }
    }
    /*public void WriteCSVRAW(float[] excel2)
    {
        float[] ppgArray = excel2.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);

        if (ppgArray.Length > 0)
        {
            TextWriter tw = new StreamWriter(filename_RAWPPG, false);
            tw.WriteLine("Time, RAW PPG");
            tw.Close();

            tw = new StreamWriter(filename_RAWPPG, true);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            //float currentTime = 0;

            for (int i = 0; i < ppgArray.Length; i++)
            {
                long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                //double seconds = currentTime / 1000.0;

                TimeSpan timeSpan = TimeSpan.FromMilliseconds(elapsedMilliseconds);

                //string formattedTime = $":{timeSpan.Seconds:D2}:{timeSpan.Milliseconds:D2}";

                //tw.WriteLine($"{formattedTime}, {ppgArray[i]}");

                //currentTime += Time.deltaTime * 1000;
                string formattedTime = $"{timeSpan.Hours:D1}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";

                tw.WriteLine($"{formattedTime}, {ppgArray[i]}");
            }

            stopwatch.Stop();
            tw.Close();
        }
    }
    */
}

