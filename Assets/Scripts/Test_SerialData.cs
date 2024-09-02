using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;
using System;


public class Test_SerialData : MonoBehaviour
{
    static SerialPort serialPort;
    static void Main(string[] args)
    {
        // �ø��� ��Ʈ ����
        string portName = "COM3"; // �ø��� ��Ʈ �̸�
        int baudRate = 9600; // ��� �ӵ�

        // �ø��� ��Ʈ �ʱ�ȭ
        serialPort = new SerialPort(portName, baudRate);

        try
        {
            // �ø��� ��Ʈ ����
            serialPort.Open();
            Console.WriteLine("�ø��� ��Ʈ�� ���Ƚ��ϴ�.");

            // ������ ����
            while (true)
            {
                Console.Write("������ �����͸� �Է��ϼ���: ");
                string data = Console.ReadLine();

                // �ø��� ��Ʈ�� ���� ������ ����
                serialPort.WriteLine(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("�ø��� ��Ʈ�� �� �� �����ϴ�: " + ex.Message);
        }
        finally
        {
            // �ø��� ��Ʈ �ݱ�
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }
}
