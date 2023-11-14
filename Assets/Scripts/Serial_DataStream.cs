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

public class Serial_DataStream : MonoBehaviour
{
    //private string output_data = "";

    static SerialPort serialPort = new SerialPort();

    public Serial_DataStream()
    {
    }

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

    void Start()
    {
        SerialOpen();
    }

    void Update()
    {
        int receivedNumber = serialPort.BytesToRead;

        if (receivedNumber > 0)
        {
            byte[] buffer = new byte[receivedNumber];
            serialPort.Read(buffer, 0, receivedNumber);
            List<float> ppgdata = new List<float>();

            int dataIndex = 0;

            foreach (byte receivedData in buffer)
            {
                if (Parsing_LXDFT2(receivedData) == 1)
                {
                    int i = 0;
                    int streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG 데이터
                    float stdata = (float)streamdata;
                    ppgdata.Add(stdata);
                    float[] ppgArray = ppgdata.ToArray();
                    ConvertToSSF(ppgArray);

                }
            }
        }
    }
    List<int> peakIndices = new List<int>();

    int previousPeakIndex = -1;
    float peakThreshold = 2500f;
    const int windowsSize = 32;
    private float samplingRate = 255f;
    double ThresholdRatio = 0.7;
    int IntervalSeconds = 3;


    void PeakDetection()
    {
        float[] ppgSignal = GetPPGSignal();

        float[] sefSignal = ConvertToSSF(ppgSighnal);

        double threshold = 0;
        float[] thresholdBuffer = new float[5];

        for (int i = 0; i < ssfSignal.Length; i++)
        {
            if(i < IntervalSeconds * 255)
            {
                float maxPeak = ssfSignal.Skip(i).Take(255).Max();
                threshold = ThresholdRatio * maxPeak;
            }

            if (ssfSignal[i] > threshold)
            {

            }
        }
    }

    float[] ConvertToSSF(float[] ppgSignal)
    {
        float[] ssfSignal = new float[ppgSignal.Length - windowsSize + 1];

        for (int i = 0; i < ssfSignal.Length; i++)
        {
            float sum = 0;

            for (int j = 0; j < windowsSize; j++)
            {
                sum += ppgSignal[i + j];
            }

            ssfSignal[i] = sum/windowsSize;
        }

        return ssfSignal;
    }

    void DetectPeaks(float[] data)
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

                        Debug.Log("PPI: " + ppi + "ms " + "Peak 감지: " + totalPeaks + ", 값 " + data[i]);
                    }

                    //현재 피크를 이전 피크로 설정
                    previousPeakIndex = currentPeakIndex;
                }
            }
        }
        
    }
}
