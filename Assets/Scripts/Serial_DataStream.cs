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

public class Serial_DataStream : MonoBehaviour
{
    //private string output_data = "";

    static SerialPort serialPort = new SerialPort();

    public Serial_DataStream()
    {
    }

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



    public AudioMixer audioMixer;
    public AudioMixerGroup audioMixerGroup;

    private float filterCutoffLow = 0.5f;
    private float filterCutoffHigh = 4.0f;


    int previousPeakIndex = -1;
    float peakThreshold = 2500f;
    private float samplingRate = 255f;
    int IntervalSeconds = 3;


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
            if(!serialPort.IsOpen)
            {
                serialPort.PortName = "COM3";
                serialPort.BaudRate = 115200;
                serialPort.DataBits = 8;
                serialPort.StopBits = StopBits.One;
                serialPort.Parity = Parity.None;
                serialPort.Open();
            }

        }
        catch (Exception ex)
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

                        PeakDetect(ppgArray);


                        if ((ppgArray.Length - windowSize + 1) >= 0)
                        {
                            List<float> ssfValues = ApplySSF(ppgArray, windowSize);

                            for (int k = 0; k < ssfValues.Count; k++)
                            {
                                //Debug.Log("SSF PPG: " + ssfValues[k]);
                            }
                        }


                    }
                }
            }

            yield return new WaitForSeconds(0);
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


    int windowSize = 64; //윈도우 사이즈 창 크기

    //SSF 적용 함수
    static List<float> ApplySSF(float[] ppgArray, int windowSize)
    {
        List<float> ssfValues = new List<float>();

        for (int i = 0; i < ppgArray.Length; i++)
        {
            float ssfValue = ConvertToSSF(ppgArray, i, windowSize);
            ssfValues.Add(ssfValue);
        }

        return ssfValues;
    }

    //PPG신호를 SSF신호로 변환하는 함수
    static float ConvertToSSF(float[] ppgArray, int currentIndex, int windowSize)
    {
        float ssfValue = 0;

        for (int k = 0; k < windowSize; k++)
        {
            int forwardIndex = currentIndex + k;
            int backwardIndex = currentIndex - k;

            if (forwardIndex < ppgArray.Length && backwardIndex >= 0 && backwardIndex >= 0)
            {
                float delta = (forwardIndex == currentIndex) ? 0 : 1;
                ssfValue += delta * (ppgArray[forwardIndex] - ppgArray[backwardIndex]);
            }
        }

        return ssfValue;
    }

    

    //피크 검출 알고리즘 v1
    static List<int> PeakDetect(float[] ppgArray)
    {
        int peakIndex = -1; //피크의 위치값
        List<int> peakIndices = new List<int>(); //peakIndex 저장하는 리스트
        float peakValue = ppgArray.Min(); //피크의 가장 작은 신호값
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
                if (peakValue == ppgArray.Min() || value > peakValue)
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
                peakValue = ppgArray.Min();
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

    public void WriteCSVRAW(float[] excel2)
    {
        float[] ppgArray = excel2.ToArray();

        if (ppgArray.Length > 0)
        {
            TextWriter tw = new StreamWriter(filename_RAWPPG, false);
            tw.WriteLine("PeakValue, Peak number");
            tw.Close();

            tw = new StreamWriter(filename_RAWPPG, true);

            //float currentTime = 0;

            for (int i = 0; i < ppgArray.Length; i++)
            {
                //double seconds = currentTime / 1000.0;

                //string formattedTime = $":{timeSpan.Seconds:D2}:{timeSpan.Milliseconds:D2}";

                //tw.WriteLine($"{formattedTime}, {ppgArray[i]}");

            }
            tw.Close();
        }
    }

    const int BufferSize = 5; // SSF 피크 저장하는 버퍼 크기
    const int InitialPeakDetectionTime = 3; // 초기 피크 검출 시간
    float[] peakBuffer = new float[BufferSize]; // 피크를 저장하는 버퍼
    float threshold; // 임계값
    const float InitialThresholdRatio = 0.7f; // 초기 임계값 비율
    int peakBufferIndex = 0; // 버퍼 인덱스
    int currentPeakDetectionTime = 0; // 현재 피크 검출 시간

    //실시간 PPG 피크 검출 알고리즘
    public void RealtimePeakDetection(float[] ppgArray)
    {
        float currentPeakValue = FindMax(ppgArray);

        if (currentPeakDetectionTime < InitialPeakDetectionTime)
        {
            //초기 피크 검출 시간이 지나지 않았을 때
            if (currentPeakValue > threshold)
            {
                //피크 감지시 초기화하고 버퍼에 추가
                ResetPeakBuffer();
                AddToPeakBuffer(currentPeakValue);
            }
            
            currentPeakDetectionTime++;
        }
        else
        {
            //3초 이후
            if (currentPeakValue > threshold)
            {
                ResetPeakBuffer();
                AddToPeakBuffer(currentPeakValue);

                if (currentPeakValue > threshold)
                {
                    //임계값 업데이트
                    UpdateThreshold();
                }
            }
            else
            {
                //임계값을 넘지 못했을 때
                threshold = InitialThresholdRatio * peakBuffer[peakBufferIndex];
            }
        }
    }
    


    //가장 큰 값 찾아주는 함수
    private float FindMax(float[] array)
    {
        float max = float.MinValue;
        foreach(float value in array)
        {
            if (value > max)
            { 
              max = value;
            }
        }
        return max;
    }

    //피크버퍼를 업데이트하는 함수
    private void UpdateThreshold()
    {
        float maxInBuffer = FindMax(peakBuffer);
        threshold = InitialThresholdRatio * maxInBuffer;
    }

    //피크버퍼를 추가하는 함수
    private void AddToPeakBuffer(float value)
    {
        peakBuffer[peakBufferIndex] = value;
        peakBufferIndex = (peakBufferIndex * 1) % BufferSize;
    }

    //피크버퍼를 리셋하는 함수
    private void ResetPeakBuffer()
    {
        Array.Clear(peakBuffer, 0, peakBuffer.Length);
        peakBufferIndex = 0;
    }


    //실시간 피크 검출 알고리즘
    static void PeakDetection(float[] peakdetec)
    {

        float[] ppgArray = peakdetec.ToArray();
        List<float> ssfPeaksBuffer = new List<float>();

        for (int i = 0; i < ppgArray.Length; i++)
        {
            float ssfPeak = ppgArray[i];
            ssfPeaksBuffer.Add(ssfPeak);

            // 버퍼의 크기가 제한 내에 유지되도록
            if (ssfPeaksBuffer.Count > 5)
            {
                int oldsetIndex = FindOldsetPeakIndex(ssfPeaksBuffer);
            }

            //임계값을 업데이트
            float threshold = GetAdaptiveThreshold(ssfPeaksBuffer);

            if(ssfPeak > threshold)
            {
                Debug.Log("피크 임계값: " + ssfPeak + "피크의 개수: " + ssfPeaksBuffer.Count);
            }
        }
    }

    // 초기 임계값 계산 함수
    static float CalculateInitialThreshold(float[] ssfSignal)
    {
        int thresSecondSamples = 3 * 255;
        float maxPeak = ssfSignal.Take(thresSecondSamples).Max();
        float initialThreshold = 0.7f * maxPeak;
        return initialThreshold;

    }

    //가장 오래된 피크의 인덱스를 찾는 함수
    static int FindOldsetPeakIndex(List<float> ssfPeaksBuffer)
    {         
        float oldestPeak = ssfPeaksBuffer.Min();
        return ssfPeaksBuffer.FindIndex(peak => peak == oldestPeak);
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


    /*public void WriteCSVSSF(float[] excel)
    {
        float[] ppgArray = excel.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);


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
    }*/

    
}
