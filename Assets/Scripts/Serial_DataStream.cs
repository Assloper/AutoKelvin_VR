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
using UnityEngine.UI;
using Accord.Fuzzy;
using AI.Fuzzy.Library;

public class Serial_DataStream : MonoBehaviour
{
    static SerialPort serialPort = new SerialPort();
    GameObject Led;

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
            Debug.LogError("시리얼 포트가 연결되지 않았습니다.");
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
                    streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG 데이터
                    listPPG.Add(streamdata);
                    fftppg.Add(streamdata);
                    thresholdppg.Add(streamdata);
                    int[] ppgArray = listPPG.ToArray();
                    

                    if (ppgArray.Length >= 0)
                    {
                        WriteCSVRAW();
                        PeakDetection(ppgArray);
                        DebugGUI.LogPersistent("PPG", "PPG: " + listPPG[listPPG.Count - 1].ToString());
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

    public void Fuzzy()
    {
        //입력 변수 정의, LF/Hf Ratio
        LinguisticVariable LFHF_Ratio_Fuzzy = new LinguisticVariable("LFHF_Ratio", 0, 3);
        FuzzySet verylow = new FuzzySet("VeryLow", new TrapezoidalFunction(0, 0, 1.1f, 1.1f));
        FuzzySet low = new FuzzySet("Low", new TrapezoidalFunction(0.3f, 0.3f, 1.7f, 1.7f));
        FuzzySet moderate = new FuzzySet("Moderate", new TrapezoidalFunction(0.5f, 0.5f, 2.0f, 2.0f));
        FuzzySet high = new FuzzySet("High", new TrapezoidalFunction(1.0f, 1.0f, 2.5f, 2.5f));
        FuzzySet veryhigh = new FuzzySet("VeryHigh", new TrapezoidalFunction(1.2f, 1.2f, 3f, 3f));
        LFHF_Ratio_Fuzzy.AddLabel(verylow);
        LFHF_Ratio_Fuzzy.AddLabel(low);
        LFHF_Ratio_Fuzzy.AddLabel(moderate);
        LFHF_Ratio_Fuzzy.AddLabel(high);
        LFHF_Ratio_Fuzzy.AddLabel(veryhigh);

        //입력 변수 정의, SDNN
        LinguisticVariable SDNN_Fuzzy = new LinguisticVariable("SDNN", 0, 200);
        FuzzySet verylowSDNN = new FuzzySet("VeryLow", new TrapezoidalFunction(0, 0, 50, 50));
        FuzzySet lowSDNN = new FuzzySet("Low", new TrapezoidalFunction(20, 20, 70, 70));
        FuzzySet moderateSDNN = new FuzzySet("Moderate", new TrapezoidalFunction(40, 40, 90, 90));
        FuzzySet highSDNN = new FuzzySet("High", new TrapezoidalFunction(60, 60, 110, 110));
        FuzzySet veryhighSDNN = new FuzzySet("VeryHigh", new TrapezoidalFunction(80, 80, 120, 120));
        SDNN_Fuzzy.AddLabel(verylowSDNN);
        SDNN_Fuzzy.AddLabel(lowSDNN);
        SDNN_Fuzzy.AddLabel(moderateSDNN);
        SDNN_Fuzzy.AddLabel(highSDNN);
        SDNN_Fuzzy.AddLabel(veryhighSDNN);

        //입력 변수 정의, RMSSD
        LinguisticVariable RMSSD_Fuzzy = new LinguisticVariable("RMSSD", 0, 200);
        FuzzySet verylowRMSSD = new FuzzySet("VeryLow", new TrapezoidalFunction(0, 0, 50, 50));
        FuzzySet lowRMSSD = new FuzzySet("Low", new TrapezoidalFunction(20, 20, 70, 70));
        FuzzySet moderateRMSSD = new FuzzySet("Moderate", new TrapezoidalFunction(40, 40, 90, 90));
        FuzzySet highRMSSD = new FuzzySet("High", new TrapezoidalFunction(60, 60, 110, 110));
        FuzzySet veryhighRMSSD = new FuzzySet("VeryHigh", new TrapezoidalFunction(80, 80, 120, 120));
        RMSSD_Fuzzy.AddLabel(verylowRMSSD);
        RMSSD_Fuzzy.AddLabel(lowRMSSD);
        RMSSD_Fuzzy.AddLabel(moderateRMSSD);
        RMSSD_Fuzzy.AddLabel(highRMSSD);
        RMSSD_Fuzzy.AddLabel(veryhighRMSSD);
        
        //출력 변수 정의, ColorTemperature
        LinguisticVariable ColorTemperature_Fuzzy = new LinguisticVariable("ColorTemperature", 0, 12000);
        FuzzySet verywarm = new FuzzySet("VeryWarm", new TrapezoidalFunction(0, 0, 5000, 5000));
        FuzzySet warm = new FuzzySet("Warm", new TrapezoidalFunction(2000, 2000, 7000, 7000));
        FuzzySet normal = new FuzzySet("Normal", new TrapezoidalFunction(4000, 4000, 9000, 9000));
        FuzzySet cool = new FuzzySet("Cool", new TrapezoidalFunction(6000, 6000, 11000, 11000));
        FuzzySet verycool = new FuzzySet("VeryCool", new TrapezoidalFunction(8000, 8000, 12000, 12000));
        ColorTemperature_Fuzzy.AddLabel(verywarm);
        ColorTemperature_Fuzzy.AddLabel(warm);
        ColorTemperature_Fuzzy.AddLabel(normal);
        ColorTemperature_Fuzzy.AddLabel(cool);
        ColorTemperature_Fuzzy.AddLabel(verycool);

        //데이터 베이스 정의
        Database fuzzyDB = new Database(); //데이터 베이스 초기화
        fuzzyDB.AddVariable(LFHF_Ratio_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(SDNN_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(RMSSD_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(ColorTemperature_Fuzzy); //데이터 베이스에 출력변수 추가

        //추론 시스템 정의
        InferenceSystem IS = new InferenceSystem(fuzzyDB, new CentroidDefuzzifier(1000));

        //규칙 정의
        IS.NewRule("Rule 1", "IF LFHF_Ratio IS VeryLow THEN ColorTemperature IS VeryWarm");
        IS.NewRule("Rule 2", "IF LFHF_Ratio IS Low THEN ColorTemperature IS Warm");
        IS.NewRule("Rule 3", "IF LFHF_Ratio IS Moderate THEN ColorTemperature IS Normal");
        IS.NewRule("Rule 4", "IF LFHF_Ratio IS High THEN ColorTemperature IS Cool");
        IS.NewRule("Rule 5", "IF LFHF_Ratio IS VeryHigh THEN ColorTemperature IS VeryCool");


        double LFHFRATIO_Fuzzy = LFHF_Ratio[LFHF_Ratio.Count - 1];
        IS.SetInput("LFHF_Ratio", (float)LFHFRATIO_Fuzzy);
        //IS.SetInput("ColorTemperature", (float)LightTemperature);

        float ColorTemperature_result = IS.Evaluate("ColorTemperature");
        DebugGUI.LogPersistent("ColorTemperature", "ColorTemperature: " + ColorTemperature_result.ToString("F0"));
        Led.GetComponent<LightTemperature>().temperature = ColorTemperature_result;
    }

    bool flag = false;
    List<int> peaklist = new List<int>(); //Peak 값 저장하는 리스트
    double peak_time;
    List<double> peaktime = new List<double>(); //Peak 시간 저장하는 리스트
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
        DebugGUI.LogPersistent("sec", "경과 시간: " + sec.ToString("F2"));

        if (ppgArray.Length < 765)
        {
            prevalue = 0;
        }
        else
        {
            int prevalueIndex = ppgArray.Length - 2;
            prevalue = ppgArray[prevalueIndex];
            //Threshold = testAverage(ppgArray);
            Threshold = CalculateMovingAverage(ppgArray);
            DebugGUI.LogPersistent("Threshold", "Threshold: " + Threshold.ToString("F2"));
        }
        if (value >= Threshold)
        {
            if (prevalue > value && flag == false)
            {
                flag = true;
                peaklist.Add(prevalue);
                Debug.Log("피크값: " + peaklist[peaklist.Count - 1]);
                DateTime currentpeakTime = DateTime.Now;
                TimeSpan ppitime = currentpeakTime - startTime;
                peak_time = ppitime.TotalMilliseconds;
                Debug.Log("peak time: " + peak_time);
                peaktime.Add(peak_time);

                DebugGUI.LogPersistent("Peak", "Peak: " + peaklist[peaklist.Count - 1].ToString("F2"));
                if (peaktime.Count > 1)
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
                    DebugGUI.LogPersistent("심박수", "심박수: " + HeartRate.ToString("F2") + " bpm");
                }
            }
        }
        else
        {
            flag = false;
        }
        if (ppgArray.Length == 15300 + plus)
        {
            CalculatorFFT();
            double[] prvArray = prv.ToArray();
            SDNN.Add(CalculatorSDNN(prvArray));
            DebugGUI.LogPersistent("SDNN", "SDNN: " + SDNN[SDNN.Count - 1].ToString("F2") + " ms");
            RMSSD.Add(CalculatorRMSSD(prvArray));
            DebugGUI.LogPersistent("RMSSD", "RMSSD: " + RMSSD[RMSSD.Count -1].ToString("F2") + " ms");
            Fuzzy();
            plus += 15300;
            prv.Clear();
        }
    }

    static int ThresholdTimer = 765;
    static int CalculateMovingAverage(int[] ppgArray)
    {
        int arrayLength = ppgArray.Length; //배열의 길이
        // 최소값이 0이고, (배열의 길이 - 765) 중 큰 값을 선택하여 시작 인덱스를 결정
        int startIndex = Math.Max(0, arrayLength - ThresholdTimer); 
        List<int> recentData = new List<int>();

        for (int i = startIndex; i <arrayLength; i++)
        {
            recentData.Add(ppgArray[i]);
        }
        // 최근 데이터의 평균을 계산하고 이에 300을 더한 값을 이동 평균으로 설정
        double Threshold = recentData.Average() + 300;
        // 이동 평균의 소수점 이하를 버림하여 정수값으로 반환
        return (int)Math.Floor(Threshold);
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
        DebugGUI.LogPersistent("HF", "HF: " + HFpowerSum.ToString("F2") + " ms²");
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
        DebugGUI.LogPersistent("LF", "LF: " + LFpowerSum.ToString("F2") + " ms²");
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
        //PPG Array가 들어오기 시작하는 구간
        int[] ppgArray = listPPG.ToArray();
        //Excel 작성 시작
        tw = new StreamWriter(filename_RAWPPG, true); // true를 사용하여 파일에 추가 모드로 열기

        //시간 차를 구하기 위하여 현재 시간을 구함
        DateTime currentTime = DateTime.Now;
        //앞서 전역변수로 선언된 Starttime과 현재시간인 CurrentTime을 활용해서 시간차 구함
        TimeSpan elapsed = currentTime - startTime;
        //시간 차 포맷
        diff_time = string.Format("{0}:{1}:{2}:{3}", elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);
        //Debug.Log("시간: " + diff_time);
        //만약 peaklist의 Count가 오를 경우 peaklist의 마지막 값과 시간을 출력하고 그 외에는 0을 출력
        if (peaklist.Count == j && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}", diff_time, ppgArray[ppgArray.Length - 1], peaklist[peaklist.Count - 1], null, null, null, null, null, null);
            j++;
        }
        else if (SDNN.Count == k && RMSSD.Count == l && LF_Power.Count == n && HF_Power.Count == o && LFHF_Ratio.Count == m && check == false)
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