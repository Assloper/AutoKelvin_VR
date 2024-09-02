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
        // 시리얼 포트 설정
        string portName = "COM3"; // 시리얼 포트 이름
        int baudRate = 9600; // 통신 속도

        // 시리얼 포트 초기화
        serialPort = new SerialPort(portName, baudRate);

        try
        {
            // 시리얼 포트 열기
            serialPort.Open();
            Console.WriteLine("시리얼 포트가 열렸습니다.");

            // 데이터 전송
            while (true)
            {
                Console.Write("전송할 데이터를 입력하세요: ");
                string data = Console.ReadLine();

                // 시리얼 포트를 통해 데이터 전송
                serialPort.WriteLine(data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("시리얼 포트를 열 수 없습니다: " + ex.Message);
        }
        finally
        {
            // 시리얼 포트 닫기
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }
}
