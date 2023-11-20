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


    int windowSize = 64; //������ ������ â ũ��

    //SSF ���� �Լ�
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

    //PPG��ȣ�� SSF��ȣ�� ��ȯ�ϴ� �Լ�
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

    

    //��ũ ���� �˰��� v1
    static List<int> PeakDetect(float[] ppgArray)
    {
        int peakIndex = -1; //��ũ�� ��ġ��
        List<int> peakIndices = new List<int>(); //peakIndex �����ϴ� ����Ʈ
        float peakValue = ppgArray.Min(); //��ũ�� ���� ���� ��ȣ��
        float Baseline = CaloulateAverage(ppgArray);


        for (int i = 1; i < ppgArray.Length; i++)
        {
            // ���� SSFsignal ��
            float value = ppgArray[i];
            // ������ SSFsignal ��
            float derivative = value - ppgArray[i - 1];
            //���࿡ ���� value�� Baseline ���� �Ѿ��� ��
            if (value > Baseline)
            {
                //���� ���� peakValue�� Minvalue�� �����ϰų� ssf���� peakvalue ������ ���� ��
                if (peakValue == ppgArray.Min() || value > peakValue)
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
                peakValue = ppgArray.Min();
            }

        }
        //���� PeakIndex�� �ٸ��� 
        if (peakIndex != -1)
        {
            //���������� ������ ��ũ�� ���� ���
            peakIndices.Add(peakIndex);
        }
        
        return peakIndices;
        
    }
    
   
    //�迭�� ��հ� ��� �Լ�
    static float CaloulateAverage(float[] values)
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

    const int BufferSize = 5; // SSF ��ũ �����ϴ� ���� ũ��
    const int InitialPeakDetectionTime = 3; // �ʱ� ��ũ ���� �ð�
    float[] peakBuffer = new float[BufferSize]; // ��ũ�� �����ϴ� ����
    float threshold; // �Ӱ谪
    const float InitialThresholdRatio = 0.7f; // �ʱ� �Ӱ谪 ����
    int peakBufferIndex = 0; // ���� �ε���
    int currentPeakDetectionTime = 0; // ���� ��ũ ���� �ð�

    //�ǽð� PPG ��ũ ���� �˰���
    public void RealtimePeakDetection(float[] ppgArray)
    {
        float currentPeakValue = FindMax(ppgArray);

        if (currentPeakDetectionTime < InitialPeakDetectionTime)
        {
            //�ʱ� ��ũ ���� �ð��� ������ �ʾ��� ��
            if (currentPeakValue > threshold)
            {
                //��ũ ������ �ʱ�ȭ�ϰ� ���ۿ� �߰�
                ResetPeakBuffer();
                AddToPeakBuffer(currentPeakValue);
            }
            
            currentPeakDetectionTime++;
        }
        else
        {
            //3�� ����
            if (currentPeakValue > threshold)
            {
                ResetPeakBuffer();
                AddToPeakBuffer(currentPeakValue);

                if (currentPeakValue > threshold)
                {
                    //�Ӱ谪 ������Ʈ
                    UpdateThreshold();
                }
            }
            else
            {
                //�Ӱ谪�� ���� ������ ��
                threshold = InitialThresholdRatio * peakBuffer[peakBufferIndex];
            }
        }
    }
    


    //���� ū �� ã���ִ� �Լ�
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

    //��ũ���۸� ������Ʈ�ϴ� �Լ�
    private void UpdateThreshold()
    {
        float maxInBuffer = FindMax(peakBuffer);
        threshold = InitialThresholdRatio * maxInBuffer;
    }

    //��ũ���۸� �߰��ϴ� �Լ�
    private void AddToPeakBuffer(float value)
    {
        peakBuffer[peakBufferIndex] = value;
        peakBufferIndex = (peakBufferIndex * 1) % BufferSize;
    }

    //��ũ���۸� �����ϴ� �Լ�
    private void ResetPeakBuffer()
    {
        Array.Clear(peakBuffer, 0, peakBuffer.Length);
        peakBufferIndex = 0;
    }


    //�ǽð� ��ũ ���� �˰���
    static void PeakDetection(float[] peakdetec)
    {

        float[] ppgArray = peakdetec.ToArray();
        List<float> ssfPeaksBuffer = new List<float>();

        for (int i = 0; i < ppgArray.Length; i++)
        {
            float ssfPeak = ppgArray[i];
            ssfPeaksBuffer.Add(ssfPeak);

            // ������ ũ�Ⱑ ���� ���� �����ǵ���
            if (ssfPeaksBuffer.Count > 5)
            {
                int oldsetIndex = FindOldsetPeakIndex(ssfPeaksBuffer);
            }

            //�Ӱ谪�� ������Ʈ
            float threshold = GetAdaptiveThreshold(ssfPeaksBuffer);

            if(ssfPeak > threshold)
            {
                Debug.Log("��ũ �Ӱ谪: " + ssfPeak + "��ũ�� ����: " + ssfPeaksBuffer.Count);
            }
        }
    }

    // �ʱ� �Ӱ谪 ��� �Լ�
    static float CalculateInitialThreshold(float[] ssfSignal)
    {
        int thresSecondSamples = 3 * 255;
        float maxPeak = ssfSignal.Take(thresSecondSamples).Max();
        float initialThreshold = 0.7f * maxPeak;
        return initialThreshold;

    }

    //���� ������ ��ũ�� �ε����� ã�� �Լ�
    static int FindOldsetPeakIndex(List<float> ssfPeaksBuffer)
    {         
        float oldestPeak = ssfPeaksBuffer.Min();
        return ssfPeaksBuffer.FindIndex(peak => peak == oldestPeak);
    }

    // �������� �Ӱ谪�� ��� �Լ�
    static float GetAdaptiveThreshold(List<float> ssfPeaksBuffer)
    {
        //�ʱ� �Ӱ谪�� �ִ� ��ũ�� ������� ���
        float initialThreshold = InitialThresholdRatio * ssfPeaksBuffer.Max();

        //���� ������ Ȱ���Ͽ� �Ӱ谪 ����
        float adjustedThreshold = initialThreshold;

        return adjustedThreshold;
    }

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

    
}
