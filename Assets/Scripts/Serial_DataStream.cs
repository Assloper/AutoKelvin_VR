using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using Parsing;
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

    List<int> peakIndices = new List<int>();

    int previousPeakIndex = -1;
    float peakThreshold = 2500f;
    const int windowsSize = 128; // SSF 계산을 위한 창 크기
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


                int dataIndex = 0;

                foreach (byte receivedData in buffer)
                {
                    if (Parsing_LXDFT2(receivedData) == 1)
                    {


                        int i = 0;
                        int streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG 데이터

                        listPPG.Add((float)streamdata);

                        float[] ppgArray = listPPG.ToArray();

                        // ppg 개수가 windowsize 만큼 뺄수 있게 되고부터 실행
                        //Debug.Log(ppgArray.Length);

                        if ((ppgArray.Length - windowsSize + 1) >= 0)
                        {

                            PeakDetection(ppgArray);
                            //WriteCSVSSF(ppgArray);
                            //WriteCSVRAW(ppgArray);

                        }


                    }
                }
            }

            yield return new WaitForSeconds(1f);
        }
        
    }

    string filename_SSFPPG = "";
    string filename_RAWPPG = "";
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





    // PPG신호를 SSF신호로 변환하는 함수
    static float[] ConvertToSSF(float[] ppgSignal)
    {

        // SSF 신호를 저장할 배열
        //Debug.Log(ppgSignal.Length);
        float[] ssfSignal = new float[ppgSignal.Length - windowsSize + 1];

        // SSF 계산
        for (int i = 0; i < ssfSignal.Length; i++)
        {
            float sum = 0;
            // 윈도우 크기만큼의 샘플을 합산
            for (int j = 0; j < windowsSize; j++)
            {
                sum += ppgSignal[i + j];
            }

            // 평균 계산하여 SSF에 저장
            ssfSignal[i] = sum/windowsSize;
        }

        return ssfSignal;


    }

    //실시간 피크 검출 알고리즘
    static void PeakDetection(float[] ppgData)
    {
        float[] ppgArray = ppgData.ToArray();
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

            if(ssfPeak > threshold)
            {

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
    }
    public void WriteCSVRAW(float[] excel2)
    {
        float[] ppgArray = excel2.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);


        if (ppgArray.Length > 0)
        {
            TextWriter tw = new StreamWriter(filename_RAWPPG, false);
            tw.WriteLine("RAW PPG");
            tw.Close();

            tw = new StreamWriter(filename_RAWPPG, true);

            for (int i = 0; i < ppgArray.Length; i++)
            {
                tw.WriteLine(ppgArray[i]);
            }
            tw.Close();
        }
    }*/

}
