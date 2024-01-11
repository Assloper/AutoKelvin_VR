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
using Accord.Audio.Filters;
using Accord.Math;
using Accord.Math.Transforms;
using UnityEngine.Experimental.Audio;
using System.Numerics;
using UnityEditor;

public class Serial_DataStream : MonoBehaviour
{
    static SerialPort serialPort = new SerialPort();

    public Serial_DataStream()
    {
    }

    static List<int> listPPG = new List<int>();

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
    byte[] PacketStreamData = new byte[Ch_Num * 2 * Sample_Num];

    static DateTime startTime;


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
        catch (Exception)
        {
            Debug.LogError("시리얼 포트가 연결되지 않았습니다.");
        }

    }

    int streamdata;
    List<double> ppi = new List<double>();
    List<double> fftppg = new List<double>();
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
                    fftppg.Add(streamdata);
                    int[] ppgArray = listPPG.ToArray();
                    

                    if (ppgArray.Length >= 0)
                    {
                        WriteCSVRAW();
                        PeakDetection(ppgArray);
                        DebugGUI.LogPersistent("PPG", "PPG: " + listPPG[listPPG.Count - 1].ToString("F3"));
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
        tw.WriteLine("Time, Value, Peak Time, Peak Data, SDNN Time, SDNN Data, RMSSD Time, RMSSD Data");
        tw.Close();
    }

    void Awake()
    {
        DebugGUI.SetGraphProperties("PPG", "PPG", 1000, 3500, 0, new Color(1, 0.5f, 1), false);
        DebugGUI.SetGraphProperties("PPI", "PPI", 0, 1000, 1, new Color(0, 1f, 0), false);
        DebugGUI.SetGraphProperties("FFT", "FFT", 0, 100, 2, new Color(1, 0.5f, 0), true);
    }

    void Update()
    {
        PPGdata();
    }

    static double samplingRate = 255;
    static double lowcut = 0.5;
    static double highcut = 4.0;

    /*static double[] bandpassfilter(double[] ppgArray)
    {
        var bandpass = new BandpassFilter(lowcut, highcut, samplingRate);
        var filtered = bandpass.Apply(ppgArray);
        return filtered;
    }*/

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

    /*static double[] ApplyBandpassFilter(double[] ppgArray, double samplingRate, double low, double high)
    {
        HighPassFilter bandpassFilter = new IirFilter
    }*/

    bool flag = false;
    List<int> peaklist = new List<int>(); //Peak 값 저장하는 리스트
    double peak_time;
    List<double> peaktime = new List<double>(); //Peak 시간 저장하는 리스트
    List<double> prv = new List<double>();
    List<double> SDNN = new List<double>();
    List<double> RMSSD = new List<double>();

    public void PeakDetection(int[] ppgArray)
    {

        int prevalue;
        int valueIndex = ppgArray.Length - 1;
        int value = ppgArray[valueIndex];
        DateTime currentTime = DateTime.Now;
        TimeSpan elapsed = currentTime - startTime;
        int sec = (int)elapsed.TotalSeconds;

        int Baseline = 0;
        //Debug.Log("sec: " + sec);

        if (ppgArray.Length <= 765)
        {
            prevalue = 0;
        }
        else
        {
            int prevalueIndex = ppgArray.Length - 2;
            prevalue = ppgArray[prevalueIndex];
            Baseline = average(ppgArray);
        }

        if (value >= Baseline) //피크가 Baseline보다 크면
        {
            if (prevalue > value && flag == false)
            {
                flag = true;
                peaklist.Add(prevalue);
                //Threadshold = UpdaterThreshold(peaklist[peaklist.Count - 1]);
                DateTime currentpeakTime = DateTime.Now;
                TimeSpan ppitime = currentpeakTime - startTime;
                peak_time = ppitime.TotalMilliseconds;
                peaktime.Add(peak_time);

                DebugGUI.LogPersistent("Peak", "Peak: " + peaklist[peaklist.Count - 1].ToString("F3"));
                if (peaklist.Count > 1)
                {
                    double peakInterval = (peaktime[peaktime.Count - 1] - peaktime[peaktime.Count - 2]);
                    ppi.Add(peakInterval);
                    prv.Add(peakInterval);
                    DebugGUI.LogPersistent("PPI", "PPI: " + peakInterval.ToString("F2") + " Ms");
                    DebugGUI.Graph("PPI", (float)peakInterval);
                    double HeartRate = 60000 / peakInterval;
                    DebugGUI.LogPersistent("심박수", "심박수: " + HeartRate.ToString("F2") + " BPM");
                }
            }
        }
        else
        {
            flag = false;
        }
        if (sec == 60 + plus)
        {
            double[] prvArray = prv.ToArray();
            SDNN.Add(CaloulatSDNN(prvArray));
            DebugGUI.LogPersistent("SDNN", "SDNN: " + SDNN[SDNN.Count - 1].ToString("F2") + " ms");
            RMSSD.Add(CaloulatRMSSD(prvArray));
            DebugGUI.LogPersistent("RMSSD", "RMSSD: " + RMSSD[RMSSD.Count -1].ToString("F2") + " ms");
            CaloulatiorFFT();
            plus += 60;
            prv.Clear();
        }
    }

    List<double> lfPeak = new List<double>();
    List<double> hfPeak = new List<double>();
    List<double> lfhfRatio = new List<double>();
    static int InitialThresholdRatio = 70;
    //임계값 업데이트
    static int average(int[] ppgdata)
    {
        int sum = 0;
        for (int i = 0; i < ppgdata.Length; i++)
        {
            sum += ppgdata[i];
        }
        int average = sum / ppgdata.Length;
        return average;
    }

    public void CaloulatiorFFT()
    {
        double[] fftArray = fftppg.ToArray();

        int originalLength = fftArray.Length;

        int newLangth = (int)Math.Pow(2, Math.Ceiling(Math.Log(originalLength, 2)));
        Array.Resize(ref fftArray, newLangth);
        Complex[] fftdata = fftArray.ToComplex();
        FourierTransform.FFT(fftdata, FourierTransform.Direction.Forward);
        for(int i = 0; i < fftdata.Length; i++)
        {
            DebugGUI.Graph("FFT", (float)fftdata[i].Magnitude);
        }

        double sampleRate = 255;
        double lfStart = 0.04;
        double lfEnd = 0.15;
        double hfStart = 0.15;
        double hfEnd = 0.4;

        double lfPower = GetPowerInLFRange(fftdata, sampleRate, lfStart, lfEnd);
        
        double hfPower = GetPowerInHFRange(fftdata, sampleRate, hfStart, hfEnd);
        double lfhfRatio = lfPower / hfPower;
        DebugGUI.LogPersistent("LF/HF", "LF/HF: " + lfhfRatio.ToString("F2") + " ms");
        fftppg.Clear();
        fftArray.Clear();
    }


    static double GetPowerInHFRange(Complex[] specturm, double sampleRate, double startFrequency, double endFrequency)
    {
        int startIndex = (int)(startFrequency / (sampleRate / specturm.Length));
        int endIndex = (int)(endFrequency / (sampleRate / specturm.Length));

        double HFpowerSum = 0;
        for (int i = startIndex; i <= endIndex; i++)
        {
            HFpowerSum += specturm[i].Magnitude;
        }
        DebugGUI.LogPersistent("HF", "HF: " + HFpowerSum.ToString("F2") + " ms");
        return HFpowerSum;
    }
    static double GetPowerInLFRange(Complex[] specturm, double sampleRate, double startFrequency, double endFrequency)
    {
        int startIndex = (int)(startFrequency * (sampleRate / specturm.Length));
        int endIndex = (int)(endFrequency * (sampleRate / specturm.Length));

        double LFpowerSum = 0;
        for (int i = startIndex; i <= endIndex; i++)
        {
            LFpowerSum += specturm[i].Magnitude;
        }
        DebugGUI.LogPersistent("LF", "LF: " + LFpowerSum.ToString("F2") + " ms");
        return LFpowerSum;
    }


    private static int plus = 0;
    static double CaloulatSDNN(double[] RRInterval)
    {
        double sum = 0;
        double average = 0;
        double SDNN = 0;
        for (int i = 0; i < RRInterval.Length; i++)
        {
            sum += RRInterval[i];
        }
        average = sum / RRInterval.Length;
        sum = 0;
        for (int i = 0; i < RRInterval.Length; i++)
        {
            sum += Math.Pow(RRInterval[i] - average, 2);
        }
        SDNN = Math.Sqrt(sum / (RRInterval.Length - 1));
        return SDNN;
    }

    static double CaloulatRMSSD(double[] RRInterval)
    {
        double sum = 0;
        double RMSSD = 0;
        for (int i = 0; i < RRInterval.Length - 1; i++)
        {
            sum += Math.Pow(RRInterval[i + 1] - RRInterval[i], 2);
        }
        RMSSD = Math.Sqrt(sum / (RRInterval.Length - 1));
        return RMSSD;
    }

    private void OnDestroy()
    {
        serialPort.Close();
        DebugGUI.RemoveGraph("PPG");
        DebugGUI.RemoveGraph("PPI");
        DebugGUI.RemoveGraph("FFT");
    }
    string filename_RAWPPG = Application.dataPath + "/Test.csv";
    string filename = "";

    string diff_time;
    bool check = false;
    static DateTime StartDate;
    TimeSpan elapsed;


    int j = 1;
    int k = 1;
    int l = 1;
    public void WriteCSVRAW()
    {
        int[] ppgArray = listPPG.ToArray();

        tw = new StreamWriter(filename_RAWPPG, true); // true를 사용하여 파일에 추가 모드로 열기

        
        DateTime currentTime = DateTime.Now;
        TimeSpan elapsed = currentTime - startTime;
        diff_time = string.Format("{0}:{1}:{2}:{3}", elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);
        //Debug.Log("시간: " + diff_time);
        if (peaklist.Count == j && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", diff_time, ppgArray[ppgArray.Length - 1], diff_time, peaklist[peaklist.Count - 1], diff_time, 0, diff_time, 0);
            j++;
        }
        else if (SDNN.Count == k && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", diff_time, ppgArray[ppgArray.Length - 1], diff_time, 0, diff_time, SDNN[SDNN.Count - 1], diff_time, 0);
            k++;
        }
        else if (RMSSD.Count == l && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", diff_time, ppgArray[ppgArray.Length - 1], diff_time, 0, diff_time, 0, diff_time, RMSSD[RMSSD.Count - 1]);
            l++;
        }
        else
        {
            check = false;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", diff_time, ppgArray[ppgArray.Length -1], diff_time, 0,diff_time, 0,diff_time, 0);
        }   
        tw.Flush();
        tw.Close();
    }
}


