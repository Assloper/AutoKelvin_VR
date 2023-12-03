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
using System.Runtime.InteropServices;

public class Serial_DataStream : MonoBehaviour
{

    static SerialPort serialPort = new SerialPort();

    public Serial_DataStream()
    {
    }

    List<int> listPPG = new List<int>();
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

                        listPPG.Add(streamdata);

                        int[] ppgArray = listPPG.ToArray();

                        PeakDetection(ppgArray);
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
    }

    void Update()
    {
    }

    bool flag = false;
    List<int> peaklist = new List<int>(); //Peak 값 저장하는 리스트
    public void PeakDetection(int[] ppgArray)
    {
        float Baseline = CaloulateAverage(ppgArray); //Peak의 경계선
        
        int prevalue;

        if (ppgArray.Length <= 1)
        {
            prevalue = 0;
        }
        else
        {
            int prevalueIndex = ppgArray.Length - 2;
            prevalue = ppgArray[prevalueIndex];
        }
        int valueIndex = ppgArray.Length - 1;
        int value = ppgArray[valueIndex];

        if (value >= Baseline) //피크가 Baseline보다 크면
        {
            //현재 피크값이 ppgArray.Min()이면서 현재 value값이 peakValue보다 크면 peakIndex를
            //저장하며 PeakValue = value로 설정
            if (prevalue > value && flag == false)
            {
                flag = true;
                peaklist.Add(prevalue);
                Debug.Log("Peak : " + peaklist[peaklist.Count -1] + "피크의 개수: " + peaklist.Count );
            }

        }
        else
        {
            flag = false;
        }
    }

    //평균이동선 구하는 함수
    static int CaloulateAverage(int[] values)
    {
        float sum = 0;

        foreach (float value in values)
        {
            sum += value;
        }

        return (int)(sum / values.Length);
    }
}
