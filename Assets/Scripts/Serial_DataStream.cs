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
using Accord.Neuro;
using Accord.Neuro.Learning;
using AI.Fuzzy.Library;
using Accord.Math.Random;

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

    int preserveLength = 765;
    int deletecount = 1;

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
                        //WriteCSVRAW();
                        PeakDetection(ppgArray);
                        DebugGUI.LogPersistent("PPG", "PPG: " + listPPG[listPPG.Count - 1].ToString());
                        DebugGUI.Graph("PPG", ppgArray[ppgArray.Length - 1]);
                        //DebugGUI.LogPersistent("listPPG", "ListPPG: " + listPPG.Count.ToString());
                        //DebugGUI.LogPersistent("ppgArray.Length", "ppgArray.Length: " + ppgArray.Length.ToString());
                    }
                    if (SDNN.Count == k && RMSSD.Count == l && LF_Power.Count == n && HF_Power.Count == o && LFHF_Ratio.Count == m)
                    {
                        WriteCSVRAW();
                    }
                    /*if (SDNN.Count == deletecount && RMSSD.Count == deletecount && LF_Power.Count == deletecount && HF_Power.Count == deletecount && LFHF_Ratio.Count == deletecount)
                    {
                        List<int> preservedList = listPPG.GetRange(listPPG.Count - preserveLength, preserveLength);
                        listPPG.Clear();
                        listPPG.AddRange(preservedList);
                        deletecount++;
                    }*/
                }
            }
        }
    }

    int z = 1;

    TextWriter tw;
    void Start()
    {
        Led = GameObject.Find("PointLight");
        SerialOpen();
        startTime = DateTime.Now;
        TextWriter tw = new StreamWriter(filename_RAWPPG, false);
        tw.WriteLine("Time, Value, Peak Data, SDNN, RMSSD, LF_Power, HF_Power, LF/HF Ratio, PPI");
        //tw.WriteLine("SDNN, RMSSD, LF_Power, HF_Power, LF/HF Ratio, Stress Index, Color Temperature");
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

    float LFHF_Ratio_Test = 0.1f;
    float SDNN_Test = 0;
    float RMSSD_Test = 0;
    float Stress_Index_Test = 20;

    List<float> Stress_value = new List<float>();
    List<int> Color_Temperature = new List<int>();

    public void Fuzzy()
    {
        //입력 변수 정의, SDNN
        LinguisticVariable SDNN_Fuzzy = new LinguisticVariable("SDNN", 0, 183);
        FuzzySet Low_SDNN = new FuzzySet("Low", new TrapezoidalFunction(0, 0, 46, 126));
        FuzzySet Medium_SDNN = new FuzzySet("Medium", new TrapezoidalFunction(14, 61, 173));
        FuzzySet High_SDNN = new FuzzySet("High", new TrapezoidalFunction(25, 85, 183, 183));
        SDNN_Fuzzy.AddLabel(Low_SDNN);
        SDNN_Fuzzy.AddLabel(Medium_SDNN);
        SDNN_Fuzzy.AddLabel(High_SDNN);

        //입력 변수 정의, RMSSD
        LinguisticVariable RMSSD_Fuzzy = new LinguisticVariable("RMSSD", 0, 314);
        FuzzySet Low_RMSSD = new FuzzySet("Low", new TrapezoidalFunction(0, 0, 53, 182));
        FuzzySet Medium_RMSSD = new FuzzySet("Medium", new TrapezoidalFunction(2, 78, 314));
        FuzzySet High_RMSSD = new FuzzySet("High", new TrapezoidalFunction(14, 104, 314, 314));
        RMSSD_Fuzzy.AddLabel(Low_RMSSD);
        RMSSD_Fuzzy.AddLabel(Medium_RMSSD);
        RMSSD_Fuzzy.AddLabel(High_RMSSD);

        //입력 변수 정의, LF/HF Ratio
        LinguisticVariable LFHF_Ratio_Fuzzy = new LinguisticVariable("LFHF_Ratio", 0, 20);
        FuzzySet Low_LFHF = new FuzzySet("Low", new TrapezoidalFunction(0, 0, 1.1f, 4f));
        FuzzySet Medium_LFHF = new FuzzySet("Medium", new TrapezoidalFunction(0f, 1.5f, 18f));
        FuzzySet High_LFHF = new FuzzySet("High", new TrapezoidalFunction(0f, 1.6f, 20f, 20f));
        LFHF_Ratio_Fuzzy.AddLabel(Low_LFHF);
        LFHF_Ratio_Fuzzy.AddLabel(Medium_LFHF);
        LFHF_Ratio_Fuzzy.AddLabel(High_LFHF);

        //입력 변수 정의, Stress
        LinguisticVariable Stress_Fuzzy = new LinguisticVariable("Stress_value", 0, 100);
        FuzzySet Low_Stress = new FuzzySet("Low", new TrapezoidalFunction(0, 0, 25, 50));
        FuzzySet Medium_Stress = new FuzzySet("Medium", new TrapezoidalFunction(30, 50, 70));
        FuzzySet High_Stress = new FuzzySet("High", new TrapezoidalFunction(50, 75, 100, 100));
        Stress_Fuzzy.AddLabel(Low_Stress);
        Stress_Fuzzy.AddLabel(Medium_Stress);
        Stress_Fuzzy.AddLabel(High_Stress);

        //출력 변수 정의, ColorTemperature
        LinguisticVariable ColorTemperature_Fuzzy = new LinguisticVariable("ColorTemperature", 1000, 12000);
        FuzzySet warm = new FuzzySet("Warm", new TrapezoidalFunction(1000, 1000, 2500, 4000));
        FuzzySet normal = new FuzzySet("Normal", new TrapezoidalFunction(3000, 5500, 8000));
        FuzzySet cool = new FuzzySet("Cool", new TrapezoidalFunction(6000, 9000, 12000, 12000));
        ColorTemperature_Fuzzy.AddLabel(warm);
        ColorTemperature_Fuzzy.AddLabel(normal);
        ColorTemperature_Fuzzy.AddLabel(cool);

        //데이터 베이스 정의
        Database fuzzyDB = new Database(); //데이터 베이스 초기화
        fuzzyDB.AddVariable(LFHF_Ratio_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(SDNN_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(RMSSD_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(Stress_Fuzzy); //데이터 베이스에 입력변수 추가
        fuzzyDB.AddVariable(ColorTemperature_Fuzzy); //데이터 베이스에 출력변수 추가

        //추론 시스템 정의
        InferenceSystem IS = new InferenceSystem(fuzzyDB, new CentroidDefuzzifier(1000));

        //규칙 정의
        IS.NewRule("Rule 1", "IF SDNN IS Low AND RMSSD IS Low AND LFHF_Ratio IS High THEN Stress_value IS High");
        IS.NewRule("Rule 2", "IF SDNN IS Medium AND RMSSD IS Medium AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 3", "IF SDNN IS High AND RMSSD IS High AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 4", "IF SDNN IS Low AND RMSSD IS Low AND LFHF_Ratio IS Low THEN Stress_value IS High");
        IS.NewRule("Rule 5", "IF SDNN IS Medium AND RMSSD IS Medium AND LFHF_Ratio IS Low THEN Stress_value IS Medium");
        IS.NewRule("Rule 6", "IF SDNN IS High AND RMSSD IS High AND LFHF_Ratio IS High THEN Stress_value IS Low");
        IS.NewRule("Rule 7", "IF SDNN IS Low AND RMSSD IS Medium AND LFHF_Ratio IS High THEN Stress_value IS Medium");
        IS.NewRule("Rule 8", "IF SDNN IS Medium AND RMSSD IS High AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 9", "IF SDNN IS High AND RMSSD IS Low AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 10", "IF SDNN IS Low AND RMSSD IS High AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 11", "IF SDNN IS Medium AND RMSSD IS Low AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 12", "IF SDNN IS High AND RMSSD IS Medium AND LFHF_Ratio IS High THEN Stress_value IS Low");
        IS.NewRule("Rule 13", "IF SDNN IS Low AND RMSSD IS High AND LFHF_Ratio IS High THEN Stress_value IS High");
        IS.NewRule("Rule 14", "IF SDNN IS Medium AND RMSSD IS Low AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 15", "IF SDNN IS High AND RMSSD IS Medium AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 16", "IF SDNN IS Low AND RMSSD IS Low AND LFHF_Ratio IS Medium THEN Stress_value IS High");
        IS.NewRule("Rule 17", "IF SDNN IS Medium AND RMSSD IS Medium AND LFHF_Ratio IS High THEN Stress_value IS Medium");
        IS.NewRule("Rule 18", "IF SDNN IS High AND RMSSD IS High AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 19", "IF SDNN IS Low AND RMSSD IS Medium AND LFHF_Ratio IS Low THEN Stress_value IS Low");
        IS.NewRule("Rule 20", "IF SDNN IS Medium AND RMSSD IS High AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 21", "IF SDNN IS High AND RMSSD IS Low AND LFHF_Ratio IS High THEN Stress_value IS High");
        IS.NewRule("Rule 22", "IF SDNN IS Low AND RMSSD IS High AND LFHF_Ratio IS Medium THEN Stress_value IS Medium");
        IS.NewRule("Rule 23", "IF SDNN IS Medium AND RMSSD IS Low AND LFHF_Ratio IS High THEN Stress_value IS Medium");
        IS.NewRule("Rule 24", "IF SDNN IS High AND RMSSD IS Medium AND LFHF_Ratio IS Low THEN Stress_value IS Medium");
        IS.NewRule("Rule 25", "IF SDNN IS Low AND RMSSD IS Low AND LFHF_Ratio IS Low THEN Stress_value IS High");
        IS.NewRule("Rule 26", "IF SDNN IS Medium AND RMSSD IS Medium AND LFHF_Ratio IS Low THEN Stress_value IS Medium");
        IS.NewRule("Rule 27", "IF SDNN IS High AND RMSSD IS High AND LFHF_Ratio IS High THEN Stress_value IS Low");

        double SDNN_Fuzzy_Input = SDNN[SDNN.Count - 1];
        IS.SetInput("SDNN", (float)SDNN_Fuzzy_Input);
        double RMSSD_Fuzzy_Input = RMSSD[RMSSD.Count - 1];
        IS.SetInput("RMSSD", (float)RMSSD_Fuzzy_Input);
        double LFHFRATIO_Fuzzy_Input = LFHF_Ratio[LFHF_Ratio.Count - 1];
        IS.SetInput("LFHF_Ratio", (float)LFHFRATIO_Fuzzy_Input);

        float Stress_result = IS.Evaluate("Stress_value");
        DebugGUI.LogPersistent("Stress_value", "Stress value: " + Stress_result.ToString("F2"));
        double Stress_Fuzzy_Input = Math.Round(Stress_result, 2);
        IS.SetInput("Stress_value", (float)Stress_Fuzzy_Input);
        Stress_value.Add((float)Stress_result);
        /*IS.SetInput("Stress_Index", Stress_Index_Test);
        Stress_Index_Test += 20f;*/

        //규칙 정의
        IS.NewRule("Rule 28", "IF Stress_value IS Low THEN ColorTemperature IS Cool");
        IS.NewRule("Rule 29", "IF Stress_value IS Medium THEN ColorTemperature IS Normal");
        IS.NewRule("Rule 30", "IF Stress_value IS High THEN ColorTemperature IS Warm");

        //Test
        /*IS.SetInput("SDNN", SDNN_Test);
        IS.SetInput("RMSSD", RMSSD_Test);
        IS.SetInput("LFHF_Ratio", LFHF_Ratio_Test);*/

        float ColorTemperature_result = IS.Evaluate("ColorTemperature");
        Color_Temperature.Add((int)ColorTemperature_result);
        DebugGUI.LogPersistent("ColorTemperature", "ColorTemperature: " + ColorTemperature_result.ToString("F0"));
        Led.GetComponent<LightTemperature>().temperature = ColorTemperature_result;

        /*LFHF_Ratio_Test += 0.1f;
        DebugGUI.LogPersistent("LFHF_Ratio_Test", "LFHR_Ratio_Test: " + LFHF_Ratio_Test.ToString("F2"));
        SDNN_Test += 10f;
        DebugGUI.LogPersistent("SDNN_Test", "SDNN_Test: " + SDNN_Test.ToString("F2"));
        RMSSD_Test += 10f;  
        DebugGUI.LogPersistent("RMSSD_Test", "RMSSD_Test: " + RMSSD_Test.ToString("F2"));*/
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
            Threshold = CalculateAdaptiveThreshold(ppgArray);
            DebugGUI.LogPersistent("Threshold", "Threshold: " + Threshold.ToString("F2"));
        }
        if (value >= Threshold)
        {
            if (prevalue > value && flag == false)
            {
                flag = true;
                peaklist.Add(prevalue);
                //Debug.Log("피크값: " + peaklist[peaklist.Count - 1]);
                DateTime currentpeakTime = DateTime.Now;
                TimeSpan ppitime = currentpeakTime - startTime;
                peak_time = ppitime.TotalMilliseconds;
                //Debug.Log("peak time: " + peak_time);
                peaktime.Add(peak_time);

                DebugGUI.LogPersistent("Peak", "Peak: " + peaklist[peaklist.Count - 1].ToString("F2"));
                if (peaktime.Count > 1)
                {
                    double peakInterval = peaktime[peaktime.Count - 1] - peaktime[peaktime.Count - 2];
                    if (peakInterval > 1000)
                    {
                        peakInterval = 1000;
                    }
                    if (peakInterval < 300)
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
        if (ppgArray.Length == 15300+plus)//15300 + plus)// //
        {
            CalculatorFFT();
            double[] prvArray = prv.ToArray();
            SDNN.Add(CalculatorSDNN(prvArray));
            DebugGUI.LogPersistent("SDNN", "SDNN: " + SDNN[SDNN.Count - 1].ToString("F2") + " ms");
            RMSSD.Add(CalculatorRMSSD(prvArray));
            DebugGUI.LogPersistent("RMSSD", "RMSSD: " + RMSSD[RMSSD.Count -1].ToString("F2") + " ms");
            Fuzzy();
            //plus += 2550;
            plus += 15300;
            prv.Clear();
        }
    }

    static int ThresholdTimer = 765;
    static int CalculateAdaptiveThreshold(int[] ppgArray)
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
        double Threshold = recentData.Average() + (recentData.Average() * 10/100);
        // 이동 평균의 소수점 이하를 버림하여 정수값으로 반환
        return (int)Math.Floor(Threshold);
    }
    public void CalculatorFFT()
    {
        double[] fftArray = prv.ToArray();

        int originalLength = fftArray.Length;

        int newLength = (int)Math.Pow(2, Math.Ceiling(Math.Log(originalLength, 2)));
        Array.Resize(ref fftArray, newLength);
        Complex[] fftdata = fftArray.ToComplex();
        FourierTransform.FFT(fftdata, FourierTransform.Direction.Forward);
        for (int i = 0; i < fftdata.Length; i++)
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
        for (int i = startIndex; i <= endIndex; i++)
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
    static string filename_Date = DateTime.Now.ToString("yyyyMMdd_HHmmss");


    string filename_RAWPPG = filename + "/PRV_" + filename_Date + ".csv";
    static string filename = "C:/Users/Miran-Laptop/Documents/GitHub/AutoKelvin_VR/Assets/ppgDATA";


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
    int q = 1; //Stress_Index
    int r = 1; //Color_Temperature

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
        if (SDNN.Count == k && RMSSD.Count == l && LF_Power.Count == n && HF_Power.Count == o && LFHF_Ratio.Count == m && check == false)
        {
            check = true;
            tw.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}", SDNN[SDNN.Count - 1], RMSSD[RMSSD.Count -1], LF_Power[LF_Power.Count -1], HF_Power[HF_Power.Count - 1], LFHF_Ratio[LFHF_Ratio.Count -1], Stress_value[Stress_value.Count - 1], Color_Temperature[Color_Temperature.Count - 1]);
            k++;
            l++;
            m++;
            n++;
            o++;
            q++;
            r++;
        }
        else
        {
            check = false;
        }
        tw.Close();
    }

    /*public void WriteCSVRAW()
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
/            k++;
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
    }*/
}