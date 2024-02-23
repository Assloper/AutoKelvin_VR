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
using System.Drawing.Text;
using System.Data.SqlTypes;
using Oculus.Interaction.PoseDetection.Editor;

public class Serial_DataStream : MonoBehaviour
{
    static SerialPort serialPort = new SerialPort();
    GameObject Led;

    static List<int> listPPG = new List<int>();

    bool Sync_After = false;
    // ����Ʈ ����
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
                serialPort.PortName = "COM3";
                serialPort.BaudRate = 115200;
                serialPort.DataBits = 8;
                serialPort.StopBits = StopBits.One;
                serialPort.Parity = Parity.None;
                serialPort.Open();
            }

        }
        catch (Exception)
        {
            Debug.LogError("�ø��� ��Ʈ�� ������� �ʾҽ��ϴ�.");
        }

    }

    int streamdata;
    List<double> ppi = new List<double>();
    List<double> fftppg = new List<double>();
    List<int> thresholdppg = new List<int>();
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
                    streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG ������
                    listPPG.Add(streamdata);
                    fftppg.Add(streamdata);
                    thresholdppg.Add(streamdata);
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
        Led = GameObject.Find("PointLight");
        SerialOpen();
        startTime = DateTime.Now;
        TextWriter tw = new StreamWriter(filename_RAWPPG, false);
        //tw.WriteLine("SDNN, RMSSD, LF/HF Ratio, LF_Power, HF_Power");
        tw.WriteLine("Time, Value, Peak Data, SDNN, RMSSD, LF_Power, HF_Power, LF/HF Ratio, PPI");
        tw.Close();
    }

    void Awake()
    {
        DebugGUI.SetGraphProperties("PPG", "PPG", 1000, 3500, 0, new Color(1, 0.5f, 1), false);
        DebugGUI.SetGraphProperties("PPI", "PPI", 0, 1000, 1, new Color(0, 1f, 0), false);
        DebugGUI.SetGraphProperties("FFT", "FFT", 0, 100, 2, new Color(1, 0.5f, 0), false);
    }

    void Update()
    {
        PPGdata();
    }

    static double samplingRate = 255;

    float Fuzzify_LFHF_Ratio()
    {
        if (Led != null)
        {

        }
        else
        {
            Debug.Assert(false, "The Point Light object is missing from the scene.");
        }
    }

    static int CalculateColorTemperature(double LFHF_Ratio)
    {
        double veryLow = Math.Max(0, 0.6 - LFHF_Ratio) / 0.6;
        double low = Math.Max(0, Math.Min(LFHF_Ratio - 0.4, 0.9 - LFHF_Ratio)) / 0.5;
        double moderate = Math.Max(0, Math.Min(LFHF_Ratio - 0.7, 1.3 - LFHF_Ratio)) / 0.6;
        double high = Math.Max(0, Math.Min(LFHF_Ratio - 1.1, 1.6 - LFHF_Ratio)) / 0.5;
        double veryHigh = Math.Max(0, Math.Min(LFHF_Ratio - 1.4, 2 - LFHF_Ratio)) / 0.6;
    }

    public void SomeMethod()
    {

        if (Led != null)
        {
            if (SDNN[SDNN.Count - 1] > 10)
            {
                Led.GetComponent<LightTemperature>().temperature = 3000f;
            }
            else
            {
                Led.GetComponent<LightTemperature>().temperature = 6000f;
            }
        }
        else
        {
            Debug.LogError("The Point Light object is missing from the scene.");
        }
    }

    bool flag = false;
    List<int> peaklist = new List<int>(); //Peak �� �����ϴ� ����Ʈ
    double peak_time;
    List<double> peaktime = new List<double>(); //Peak �ð� �����ϴ� ����Ʈ
    List<double> prv = new List<double>();
    List<double> SDNN = new List<double>();
    List<double> RMSSD = new List<double>();
    List<double> LFHF_Ratio = new List<double>();
    List<double> LF_Power = new List<double>();
    List<double> HF_Power = new List<double>();
    int Threshold;

    public void PeakDetection(int[] ppgArray)
    {

        int prevalue;
        int valueIndex = ppgArray.Length - 1;
        int value = ppgArray[valueIndex];
        DateTime currentTime = DateTime.Now;
        TimeSpan elapsed = currentTime - startTime;
        int sec = (int)elapsed.TotalSeconds;
        Threshold = 0;
        DebugGUI.LogPersistent("sec", "��� �ð�: " + sec.ToString("F2"));

        if (ppgArray.Length < 765)
        {
            prevalue = 0;
        }
        else
        {
            int prevalueIndex = ppgArray.Length - 2;
            prevalue = ppgArray[prevalueIndex];
            Threshold = CalculateMovingAverage(ppgArray);
            DebugGUI.LogPersistent("Threshold", "Threshold: " + Threshold.ToString("F2"));
        }
        if (value >= Threshold)
        {
            if (prevalue > value && flag == false)
            {
                flag = true;
                DateTime currentpeakTime = DateTime.Now;
                TimeSpan ppitime = currentpeakTime - startTime;
                peaklist.Add(prevalue);
                peak_time = ppitime.TotalMilliseconds;
                Debug.Log("peak time: " + peak_time);
                peaktime.Add(peak_time);

                DebugGUI.LogPersistent("Peak", "Peak: " + peaklist[peaklist.Count - 1].ToString("F2"));
                if (peaklist.Count > 1)
                {
                    double peakInterval = (peaktime[peaktime.Count - 1] - peaktime[peaktime.Count - 2]);
                    if(peakInterval > 1000)
                    {
                        peakInterval = 1000;
                    }
                    if(peakInterval < 300)
                    {
                        peakInterval = 300;
                    }
                    ppi.Add(peakInterval);
                    prv.Add(peakInterval);
                    DebugGUI.LogPersistent("PPI", "PPI: " + peakInterval.ToString("F2") + " ms");
                    DebugGUI.Graph("PPI", (float)peakInterval);
                    double HeartRate = 60000 / peakInterval;
                    DebugGUI.LogPersistent("�ɹڼ�", "�ɹڼ�: " + HeartRate.ToString("F2") + " bpm");
                }
            }
        }
        else
        {
            flag = false;
        }
        if (ppgArray.Length == 15300 + plus)
        {
            double[] prvArray = prv.ToArray();
            SDNN.Add(CalculatorSDNN(prvArray));
            DebugGUI.LogPersistent("SDNN", "SDNN: " + SDNN[SDNN.Count - 1].ToString("F2") + " ms");
            RMSSD.Add(CalculatorRMSSD(prvArray));
            DebugGUI.LogPersistent("RMSSD", "RMSSD: " + RMSSD[RMSSD.Count -1].ToString("F2") + " ms");
            CalculatorFFT();
            SomeMethod();
            plus += 15300;
            prv.Clear();
        }
    }

    static int CalculateMovingAverage(int[] ppgArray)
    {
        int arrayLength = ppgArray.Length; //�迭�� ����
        int startIndex = Math.Max(0, arrayLength - 765);
        List<int> recentData = new List<int>();

        for (int i = startIndex; i <arrayLength; i++)
        {
            recentData.Add(ppgArray[i]);
        }

        double movingAverage = recentData.Average() + 300;

        return (int)Math.Floor(movingAverage);
    }

    public void CalculatorFFT()
    {
        double[] fftArray = fftppg.ToArray();

        int originalLength = fftArray.Length;

        int newLength = (int)Math.Pow(2, Math.Ceiling(Math.Log(originalLength, 2)));
        Array.Resize(ref fftArray, newLength);
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
        LF_Power.Add(lfPower);
        double hfPower = GetPowerInHFRange(fftdata, sampleRate, hfStart, hfEnd);
        HF_Power.Add(hfPower);
        double lfhfRatio = lfPower / hfPower;
        LFHF_Ratio.Add(lfhfRatio);
        DebugGUI.LogPersistent("LF/HF", "LF/HF Ratio: " + lfhfRatio.ToString("F2"));
        fftppg.Clear();
        fftArray.Clear();
    }

    static double GetPowerInHFRange(Complex[] specturm, double sampleRate, double startFrequency, double endFrequency)
    {
        int startIndex = (int)((startFrequency * specturm.Length) / sampleRate);
        int endIndex = (int)((endFrequency * specturm.Length) / sampleRate);

        double HFpowerSum = 0;
        for (int i = startIndex; i <= endIndex; i++)
        {
            HFpowerSum += specturm[i].Magnitude;
        }
        DebugGUI.LogPersistent("HF", "HF: " + HFpowerSum.ToString("F2") + " ms��");
        return HFpowerSum;
    }
    static double GetPowerInLFRange(Complex[] specturm, double sampleRate, double startFrequency, double endFrequency)
    {

        int startIndex = (int)((startFrequency * specturm.Length) / sampleRate);
        int endIndex = (int)((endFrequency * specturm.Length) / sampleRate);
        double LFpowerSum = 0;
        for (int i = startIndex; i <= endIndex; i ++)
        {
            LFpowerSum += specturm[i].Magnitude;
        }
        DebugGUI.LogPersistent("LF", "LF: " + LFpowerSum.ToString("F2") + " ms��");
        return LFpowerSum;
    }

    private static int plus = 0;
    static double CalculatorSDNN(double[] RRInterval)
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

    static double CalculatorRMSSD(double[] RRInterval)
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

    int j = 1; //peaklist
    int k = 1; //SDNN
    int l = 1; //RMSSD
    int m = 1; //LFHF_Ratio
    int n = 1; //LF_Power
    int o = 1; //HF_Power
    int p = 1; //PPI
    public void WriteCSVRAW()
    {
        //PPG Array�� ������ �����ϴ� ����
        int[] ppgArray = listPPG.ToArray();
        //Excel �ۼ� ����
        tw = new StreamWriter(filename_RAWPPG, true); // true�� ����Ͽ� ���Ͽ� �߰� ���� ����

        //�ð� ���� ���ϱ� ���Ͽ� ���� �ð��� ����
        DateTime currentTime = DateTime.Now;
        //�ռ� ���������� ����� Starttime�� ����ð��� CurrentTime�� Ȱ���ؼ� �ð��� ����
        TimeSpan elapsed = currentTime - startTime;
        //�ð� �� ����
        diff_time = string.Format("{0}:{1}:{2}:{3}", elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);
        //Debug.Log("�ð�: " + diff_time);
        //���� peaklist�� Count�� ���� ��� peaklist�� ������ ���� �ð��� ����ϰ� �� �ܿ��� 0�� ���
        if (peaklist.Count == j && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}", diff_time, ppgArray[ppgArray.Length - 1], peaklist[peaklist.Count - 1], null, null, null, null, null, null);
            j++;
        }
        else if (SDNN.Count == k && RMSSD.Count == l && LF_Power.Count == n && HF_Power.Count == o && LFHF_Ratio.Count == m&& check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}", diff_time, ppgArray[ppgArray.Length - 1], null, SDNN[SDNN.Count - 1], RMSSD[RMSSD.Count -1], LF_Power[LF_Power.Count -1], HF_Power[HF_Power.Count - 1], LFHF_Ratio[LFHF_Ratio.Count -1], null);
            k++;
            l++;
            m++;
            n++;
            o++;
        }
        else if (ppi.Count == p && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}", diff_time, ppgArray[ppgArray.Length - 1], null, null, null, null, null, null, ppi[ppi.Count - 1]);
            p++;
        }
        else
        {
            check = false;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}", diff_time, ppgArray[ppgArray.Length - 1], null, null, null, null, null, null, null);
        }
        tw.Flush();
        tw.Close();
    }
}