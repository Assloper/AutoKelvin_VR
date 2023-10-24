using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace Parsing
{
    public class Serial_Packet_Parsing : MonoBehaviour //LXSDFT2 Parsing
    {

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
    }
}

