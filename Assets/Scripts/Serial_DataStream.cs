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
using System.Diagnostics.Eventing.Reader;

public class Serial_DataStream : MonoBehaviour
{

    static SerialPort serialPort = new SerialPort();

    public Serial_DataStream()
    {
    }

    List<int> listPPG = new List<int>();

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

    DateTime startTime;


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
            if (!serialPort.IsOpen)
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
            //Debug.LogError("시리얼 포트가 연결되지 않았습니다.");
        }

    }

    int streamdata;

    void PPGdata()
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
                    streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG 데이터
                    listPPG.Add(streamdata);

                    int[] ppgArray = listPPG.ToArray();

                    if (ppgArray.Length >= 0)
                    {
                        WriteCSVRAW();

                        //SSF_Filtering(ppgArray);
                        PeakDetection(ppgArray);
                        DebugGUI.LogPersistent("PPG", "PPG: " + ppgArray[ppgArray.Length - 1].ToString("F3"));
                        DebugGUI.Graph("PPG", ppgArray[ppgArray.Length - 1]);
                    }
                }
            }
        }
    }
            


    TextWriter tw;
    void Start()
    {
        SerialOpen();
        startTime = DateTime.Now;
        TextWriter tw = new StreamWriter(filename_RAWPPG, false);
        tw.WriteLine("Time, Value, Peak Time, Peak Data");
        tw.Close();
    }

    void Awake()
    {
        DebugGUI.SetGraphProperties("PPG", "PPG", 1000, 3500, 0, new Color(1, 0.5f, 1), false);
        DebugGUI.SetGraphProperties("Peak", "Peak", 1000, 3500, 0, new Color(1, 0.3f, 1), false);
    }

    void Update()
    {
        PPGdata();
    }

    /*int wplus = 0;
    List<int> ssfArray = new List<int>();
    public void SSF_Filtering(int[] ppgArray)
    {
        if (ppgArray.Length%32 == 0)
        {
            int SSF = 0;
            int[] u = new int[32];
            int[] y = new int[32];
            for (int i = 0 + wplus; i < ppgArray.Length; i++)
            {
                y[i] = ppgArray[i+1] - ppgArray[i];  //차분
                if (y[i] <= 0)
                {
                    u[i] = 0;
                }
                else
                {
                    u[i] = y[i];
                }
                SSF += u[i];
                ssfArray.Add(SSF);
            }
            wplus += 32;

            Debug.Log("SSF 값: " + ssfArray[ssfArray.Count - 1]);
        }
    }*/

    public class BandpassFilter
    {
        private double[] lpf;
        private double[] hpf;
    }

    bool flag = false;
    List<int> peaklist = new List<int>(); //Peak 값 저장하는 리스트
    public void PeakDetection(int[] ppgArray)
    {
        int Baseline = 2500; //Peak의 경계선

        int prevalue;
        int valueIndex = ppgArray.Length - 1;
        int value = ppgArray[valueIndex];

        if (ppgArray.Length <= 1)
        {
            prevalue = 0;
        }
        else
        {
            int prevalueIndex = ppgArray.Length - 2;
            prevalue = ppgArray[prevalueIndex];
        }

        if (value >= Baseline) //피크가 Baseline보다 크면
        {
            if (prevalue > value && flag == false)
            {
                flag = true;
                peaklist.Add(prevalue);
                DebugGUI.Graph("Peak", peaklist[peaklist.Count -1]);
                DebugGUI.LogPersistent("Peak", "Peak: " + peaklist[peaklist.Count - 1].ToString("F2"));
                if (peaklist.Count > 1)
                {
                }
            }

        }
        else
        {
            flag = false;
        }
    }

    int beforPeak;
    int currentPeak;
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

    private void OnDestroy()
    {
        serialPort.Close();
        DebugGUI.RemoveGraph("PPG");
    }
    string filename_RAWPPG = Application.dataPath + "/Test.csv";
    string filename = "";

    string diff_time;
    bool check = false;
    DateTime StartDate;
    DateTime EndDate;
    TimeSpan elapsed;

    int diffHours;
    int diffMinutes;
    int diffSeconds;
    int diffMs;

    public void WriteCSVRAW()
    {
        int[] ppgArray = listPPG.ToArray();

        tw = new StreamWriter(filename_RAWPPG, true); // true를 사용하여 파일에 추가 모드로 열기

        
        DateTime currentTime = DateTime.Now;
        TimeSpan elapsed = currentTime - startTime;
        diff_time = string.Format("{0}:{1}:{2}:{3}", elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);
        Debug.Log("시간: " + diff_time);
        if (peaklist.Count > 1 && (peaklist.Count - 1 != peaklist.Count -2))
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}", diff_time, ppgArray[ppgArray.Length - 1], diff_time, peaklist[peaklist.Count - 1]);
        }
        else
        {
            check = false;
            tw.WriteLine("{0}, {1}, {2}, {3}", diff_time, ppgArray[ppgArray.Length - 1], diff_time, 0);
        }   


        tw.Flush();
        tw.Close();
    }
}


