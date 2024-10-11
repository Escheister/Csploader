using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

using CommandsHandler;
using ProtocolEnums;
using Extensions;
using CRC16;

namespace BootloaderProtocol
{
    internal class Bootloader : CommandsOutput
    {
        public delegate byte[] BuildCmdDelegate(CmdOutput cmdOutput);
        public delegate byte[] BuildDataCmdDelegate(byte[] data);

        public Bootloader(object sender, byte[] targetSign) : base(sender)
        {
            _addrHex = new byte[2];
            _addrElar = new byte[2];
            _targetSign = targetSign;
            buildCmdDelegate += BuildCmd;
            buildDataCmdDelegate += BuildDataCmd;
        }
        public Bootloader(object sender, byte[] targetSign, byte[] throughSign) : base(sender)
        {
            _addrHex = new byte[2];
            _addrElar = new byte[2];
            _targetSign = targetSign;
            _throughSign = throughSign;
            buildCmdDelegate += BuildCmdThrough;
            buildDataCmdDelegate += BuildDataCmdThrough;
        }

        private byte[] _addrHex;
        private byte[] _addrElar;
        private readonly byte[] _throughSign;
        private readonly byte[] _targetSign;

        private Queue<byte[]> hexQueue;
        public Queue<byte[]> HexQueue { get { return hexQueue; } }

        private byte pageSize;
        public byte PageSize
        {
            set { pageSize = value; }
            get { return pageSize; }
        }

        public BuildCmdDelegate buildCmdDelegate;
        public BuildDataCmdDelegate buildDataCmdDelegate;

        private byte[] BuildCmd(CmdOutput cmdOutput) => FormatCmdOut(_targetSign, cmdOutput, 0xff);
        private byte[] BuildCmdThrough(CmdOutput cmdOutput) => CmdThroughRm(BuildCmd(cmdOutput), _throughSign, CmdOutput.ROUTING_PROG);

        private byte[] BuildDataCmd(byte[] data) => FormatUploadData(data);
        private byte[] BuildDataCmdThrough(byte[] data) => CmdThroughRm(BuildDataCmd(data), _throughSign, CmdOutput.ROUTING_PROG);

        private byte[] FormatUploadData(byte[] data)
        {
            byte[] loadField = new byte[4 + data.Length];
            _targetSign.CopyTo(loadField, 0);
            ((ushort)CmdOutput.LOAD_DATA_PAGE).GetReverseBytes().CopyTo(loadField, 2);
            data.CopyTo(loadField, 4);
            return new CRC16_CCITT_FALSE().CrcCalc(loadField);
        }
        private void GetStringsFromFile(string path, out string[] fileStrings)
        {
            using StreamReader file = new StreamReader(path);
            Task<string> data = file.ReadToEndAsync();
            fileStrings = data.Result.Trim().Split('\n');
        }
        private byte[] GetBytesFromString(string line) => StringToByteArray(line.Replace(":", string.Empty).Replace("\r", string.Empty));
        private byte[] StringToByteArray(string hex) => Enumerable.Range(0, hex.Length)
                                             .Where(x => x % 2 == 0)
                                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                                             .ToArray();
        private bool IntelHexCheckSum(byte[] array)
        {
            byte hex = 0;
            for (int i = 0; i < array.Length; i++) hex += array[i];
            return hex == 0;
        }
        public void SetQueueFromHex(string filepath)
        {
            hexQueue = new Queue<byte[]>();
            GetStringsFromFile(filepath, out string[] fileStrings);
            for (int i = 0; i < fileStrings.Length; i++)
            {
                byte[] byteString = GetBytesFromString(fileStrings[i]);
                if (!IntelHexCheckSum(byteString)) throw new Exception($"{FileCheck.CrcError}: {i + 1}");
                switch ((RecordType)byteString[3])
                {
                    case RecordType.ESAR: goto default;
                    case RecordType.SSAR: continue;
                    case RecordType.Rec: break;
                    case RecordType.EndRec: continue;
                    case RecordType.ELAR: break;
                    case RecordType.SLAR: continue;
                    default: throw new Exception($"{FileCheck.CmdError}: {i + 1}");
                }
                hexQueue.Enqueue(byteString);
            }
        }
        public void GetDataForUpload(out byte[] dataOutput)
        {
            bool GetAccessToDequeuing(byte[] dequeued)
                => dequeued[0] == 0x10 && hexQueue.Count != 0 && (RecordType)hexQueue.Peek()[3] == RecordType.Rec;

            List<byte> data = new List<byte>();
            List<byte> cmd = new List<byte>();
            while (pageSize > data.Count)
            {
                byte[] dequeueArray = hexQueue.Dequeue();
                switch ((RecordType)dequeueArray[3])
                {
                    case RecordType.Rec: // 0x00
                        if (data.Count == 0) _addrHex = new byte[] { dequeueArray[2], dequeueArray[1] };
                        data.AddRange(dequeueArray.Skip(4).Take(dequeueArray[0]));
                        break;
                    case RecordType.ELAR: // 0x04
                        _addrElar = new byte[] { dequeueArray[5], dequeueArray[4] };
                        continue;
                }
                if (!GetAccessToDequeuing(dequeueArray)) break;
            }
            cmd.AddRange(new byte[] { (byte)data.Count, 0x00 });
            cmd.AddRange(_addrHex);
            cmd.AddRange(_addrElar);
            cmd.AddRange(data);
            dataOutput = cmd.ToArray();
        }
    }
}
