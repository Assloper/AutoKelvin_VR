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
    List<float> tempPPG = new List<float>(); // made by shin

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
    public string output_data;
    byte[] PacketStreamData = new byte[Ch_Num * 2 * Sample_Num];
    private float[] ppgdata;


    public AudioMixer audioMixer;
    public AudioMixerGroup audioMixerGroup;

    private float filterCutoffLow = 0.5f;
    private float filterCutoffHigh = 4.0f;


    int previousPeakIndex = -1;
    float peakThreshold = 2500f;
    const int windowsSize = 64; // SSF ����� ���� â ũ��
    private float samplingRate = 255f;
    const float InitialThresholdRatio = 0.7f;
    int IntervalSeconds = 3;
    const int BufferSize = 5; // SSF ��ũ �����ϴ� ���� ũ��


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
            Debug.LogError("�ø��� ��Ʈ�� ������� �ʾҽ��ϴ�.");
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
                        int streamdata = ((PacketStreamData[i * 2] & 0x0F) << 8) + PacketStreamData[i * 2 + 1]; //PPG ������

                        listPPG.Add((float)streamdata);

                        float[] ppgArray = listPPG.ToArray();

                        if ((ppgArray.Length - windowsSize + 1) >= 0)
                        {
                            //PeakDetect(ConvertToSSF(ppgArray));
                            WriteCSVRAW(ppgArray);

                            //List<int> detectedPeaks = PeakDetect(ConvertToSSF(ppgArray));
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




    // PPG��ȣ�� SSF��ȣ�� ��ȯ�ϴ� �Լ�
    static float[] ConvertToSSF(float[] ppgSignal)
    {

        // SSF ��ȣ�� ������ �迭
        //Debug.Log(ppgSignal.Length);
        float[] ssfSignal = new float[ppgSignal.Length - windowsSize + 1];

        // SSF ���
        for (int i = 0; i < ssfSignal.Length; i++)
        {
            float sum = 0;
            // ������ ũ�⸸ŭ�� ������ �ջ�
            for (int j = 0; j < windowsSize; j++)
            {
                sum += ppgSignal[i + j];
            }

            // ��� ����Ͽ� SSF�� ����
            ssfSignal[i] = sum/windowsSize;
            //Debug.Log("ssfSignal �� " + ssfSignal[i]);

            //PeakDetect(ssfSignal);
        }
        return ssfSignal;


    }

    

    //��ũ ���� �˰��� v1
    public List<int> PeakDetect(float[] ssfSignal)
    {
        int peakIndex = -1;
        List<int> peakIndices = new List<int>(); //peakIndex �����ϴ� ����Ʈ
        float peakValue = float.MinValue; //��ũ�� ���� ���� ��ȣ��
        float Baseline = CaloulateAverage(ssfSignal);



        for (int i = 1; i < ssfSignal.Length; i++)
        {
            // ���� SSFsignal ��
            float value = ssfSignal[i];
            // ������ SSFsignal ��
            float derivative = value - ssfSignal[i - 1];
            //���࿡ ���� value�� Baseline ���� �Ѿ��� ��
            if (value > Baseline)
            {
                //���� ���� peakValue�� Minvalue�� �����ϰų� ssf���� peakvalue ������ ���� ��
                if (peakValue == float.MinValue || value > peakValue)
                {
                    //���� ���� ���� ��ũ���� Ŭ ��� ���� ���� ��ũ�� ����
                    peakIndex = i;
                    peakValue = value;
                }
            }
            // ssfSignal ���� ���ؼ��� ���� ���ϰ� peakIndex�� -1�� �ƴҶ�
            else if (value < Baseline && peakIndex != -1)
            {
                //���� ��ũ�� �ε����� �迭�� �߰�
                peakIndices.Add(peakIndex);
                //��ũ�ε��� -1�� �ʱ�ȭ
                peakIndex = -1;
                peakValue = float.MinValue;

            }

        }
        //���� PeakIndex�� �ٸ���
        if (peakIndex != -1)
        {
            //���������� ������ ��ũ�� ���� ���
            peakIndices.Add(peakIndex);
            Debug.Log("���� ��ũ ��: " + peakValue + "��ũ �ѹ�: " + peakIndices.Count + "");
        }


        return peakIndices;
    }
   
    //�迭�� ��հ� ��� �Լ�
    float CaloulateAverage(float[] values)
    {
        //�հ踦 �����ϴ� ���� �ʱ�ȭ
        float sum = 0;

        //�迭�� �� ��ҿ� �ݺ�
        foreach (float value in values)
        {
            sum += value;
        }

        //�迭�� ��� ��Ҹ� ���� �� ��հ� ���
        return sum / values.Length;
            
    }
    
    //�ǽð� ��ũ ���� �˰���
    /*static void PeakDetection(float[] ppgData)
    {
        float[] ppgArray = ppgData.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);
        List<float> ssfPeaksBuffer = new List<float>();

        for (int i = 0; i < ssfSignal.Length; i++)
        {
            float ssfPeak = ssfSignal[i];
            ssfPeaksBuffer.Add(ssfPeak);

            // ������ ũ�Ⱑ ���� ���� �����ǵ���
            if (ssfPeaksBuffer.Count > BufferSize)
                ssfPeaksBuffer.RemoveAt(0);

            //�Ӱ谪�� ������Ʈ
            float threshold = GetAdaptiveThreshold(ssfPeaksBuffer);

            if(ssfPeak > threshold)
            {

            }
        }
    }

    // �������� �Ӱ谪�� ��� �Լ�
    static float GetAdaptiveThreshold(List<float> ssfPeaksBuffer)
    {
        //�ʱ� �Ӱ谪�� �ִ� ��ũ�� ������� ���
        float initialThreshold = InitialThresholdRatio * ssfPeaksBuffer.Max();

        //���� ������ Ȱ���Ͽ� �Ӱ谪 ����
        float adjustedThreshold = initialThreshold;

        return adjustedThreshold;
    }*/

    //PPG�� ��� �ǽð����� ��ũ�� ������ �� ������.....

    /*void DetectPeaks(float[] data)
    {
        // Peak ���� �˰���
        for (int i = 1; i < data.Length; i++)
        {
            float derivative = data[i] - data[i - 1];
            float squaredSignal = derivative * derivative;
            if (data[i] > peakThreshold)
            {
                //peakIndices�� 1�� �߰�
                peakIndices.Add(i);

                //��ũ�� 2�� �̻��϶�
                if (peakIndices.Count >= 2)
                {
                    int totalPeaks = peakIndices.Count;
                    int currentPeakIndex = peakIndices[totalPeaks - 1];

                    if (previousPeakIndex != -1)
                    {
                        float currentPeakTime = currentPeakIndex / 255f;
                        float previousPeakTime = previousPeakIndex / 255f;
                        float ppi = (currentPeakTime - previousPeakTime) * 1000f;

                        //Debug.Log("PPI: " + ppi + "ms " + "Peak ����: " + totalPeaks + ", �� " + data[i]);
                    }

                    //���� ��ũ�� ���� ��ũ�� ����
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
    public void WriteCSVRAW(float[] excel2)
    {
        float[] ppgArray = excel2.ToArray();
        float[] ssfSignal = ConvertToSSF(ppgArray);


        if (ppgArray.Length > 0)
        {
            TextWriter tw = new StreamWriter(filename_RAWPPG, false);
            tw.WriteLine("Time, RAW PPG");
            tw.Close();

            tw = new StreamWriter(filename_RAWPPG, true);

            float currentTime = 0;

            for (int i = 0; i < ppgArray.Length; i++)
            {
                double seconds = currentTime / 1000.0;

                TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);

                string formattedTime = $":{timeSpan.Seconds:D2}:{timeSpan.Milliseconds:D2}";

                tw.WriteLine($"{formattedTime}, {ppgArray[i]}");

                currentTime += Time.deltaTime * 1000;
            }
            tw.Close();
        }
    }
}
