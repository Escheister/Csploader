using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO.Ports;
using System.Linq;
using System;

using StaticSettings;
using ProtocolEnums;
using Extensions;
using CRC16;

namespace CommandsHandler
{
    internal class CommandsOutput
    {
        protected delegate void SendDataDelegate(byte[] cmdOut);
        protected delegate Task<byte[]> ReceiveDataDelegate(int length, int ms = 250);
        public CommandsOutput(object sender) { GetTypeDevice(sender); }


        private ReceiveDataDelegate receiveData;
        private SendDataDelegate sendData;
        private SerialPort Port;
        private Socket Sock;

        private void GetTypeDevice(object sender)
        {
            if (sender is SerialPort ser)
            {
                Port = ser;
                sendData += (byte[] data) => Port.Write(data, 0, data.Length);
                receiveData = SerialReceiveData;
            }
            else if (sender is Socket sock)
            {
                Sock = sock;
                sendData += (byte[] data) => Sock.Send(data);
                receiveData = SocketReceiveData;
            }
        }
        private ProtocolReply GetReply(byte[] bufferIn, byte[] rmSign, CmdInput cmdMain)
        {
            if (bufferIn.Length == 0) return ProtocolReply.Null;
            if (Options.checkCrc && !CRC16_CCITT_FALSE.CrcCheck(bufferIn)) return ProtocolReply.WCrc;
            if (!SignatureEqual(bufferIn, rmSign)) return ProtocolReply.WSign;
            if (!CmdInputEqual(bufferIn, cmdMain)) return ProtocolReply.WCmd;
            return ProtocolReply.Ok;
        }
        private ProtocolReply GetReply(byte[] bufferIn, byte[] rmThrough, CmdInput cmdThrough, byte[] rmSign, CmdInput cmdMain)
        {
            if (bufferIn.Length == 0) return ProtocolReply.Null;
            if (Options.checkCrc && !CRC16_CCITT_FALSE.CrcCheck(bufferIn)) return ProtocolReply.WCrc;
            if (!SignatureEqual(bufferIn, rmThrough, rmSign)) return ProtocolReply.WSign;
            if (!CmdInputEqual(bufferIn, cmdThrough, cmdMain)) return ProtocolReply.WCmd;
            return ProtocolReply.Ok;
        }
        private ProtocolReply GetDataReply(byte[] bufferIn, byte[] bufferOut)
        {
            if (!DataEqual(bufferIn, bufferOut)) return ProtocolReply.WData;
            return ProtocolReply.Ok;
        }
        private bool SignatureEqual(byte[] bufferIn, byte[] rmSign)
        {
            try
            {
                byte[] targetSign = new byte[2] { bufferIn[0], bufferIn[1] };
                return Enumerable.SequenceEqual(rmSign, targetSign);
            }
            catch { return false; }
        }
        private bool SignatureEqual(byte[] bufferIn, byte[] rmThrough, byte[] rmSign)
        {
            try
            {
                byte[] throughSign = new byte[2] { bufferIn[0], bufferIn[1] };
                byte[] targetSign = new byte[2] { bufferIn[4], bufferIn[5] };
                return Enumerable.SequenceEqual(rmSign, targetSign)
                    && Enumerable.SequenceEqual(rmThrough, throughSign);
            }
            catch { return false; }
        }
        private bool CmdInputEqual(byte[] bufferIn, CmdInput cmdMain)
        {
            try { return cmdMain == (CmdInput)((bufferIn[2] << 8) | bufferIn[3]); }
            catch { return false; }
        }
        private bool CmdInputEqual(byte[] bufferIn, CmdInput cmdThrough, CmdInput cmdMain)
        {
            try
            {
                return cmdMain == (CmdInput)((bufferIn[6] << 8) | bufferIn[7])
                    && cmdThrough == (CmdInput)((bufferIn[2] << 8) | bufferIn[3]);
            }
            catch { return false; }
        }
        private bool DataEqual(byte[] bufferIn, byte[] bufferOut)
        {
            try
            {
                int start = 4;
                int crap = 6;
                if (Options.through)
                {
                    start += 8;
                    crap += 12;
                }
                byte[] data_out = new byte[bufferOut.Length - crap];
                byte[] data_in = new byte[bufferOut.Length - crap];
                Array.Copy(bufferOut, start, data_out, 0, data_out.Length);
                Array.Copy(bufferIn, start, data_in, 0, data_in.Length);
                return Enumerable.SequenceEqual(data_out, data_in);
            }
            catch { return false; }
        }
        protected byte[] FormatCmdOut(byte[] rmSign, CmdOutput cmd, byte ix = 0x00, bool crc = true)
        {
            List<byte> data = new List<byte>();
            data.AddRange(rmSign);
            data.AddRange(BitConverter.GetBytes((ushort)cmd).Reverse());
            if (ix != 0xff) data.Add(ix);
            if (crc) return new CRC16_CCITT_FALSE().CrcCalc(data.ToArray());
            return data.ToArray();
        }
        public byte[] CmdThroughRm(byte[] cmdIn, byte[] rmThrough, CmdOutput cmd)
        {
            byte[] cmdOut = new byte[cmdIn.Length + 4];
            rmThrough.CopyTo(cmdOut, 0);
            ((ushort)cmd).GetReverseBytes().CopyTo(cmdOut, 2);
            cmdIn.CopyTo(cmdOut, 4);
            return new CRC16_CCITT_FALSE().CrcCalc(cmdOut);
        }
        async private Task<byte[]> SocketReceiveData(int length, int ms = 250)
        {
            DateTime t0 = DateTime.Now;
            TimeSpan tstop;
            int bytes;
            do
            {
                bytes = Sock.Available;
                tstop = DateTime.Now - t0;
            }
            while (bytes < length && tstop.Milliseconds <= ms);
            ArraySegment<Byte> buffer = new ArraySegment<byte>(new byte[bytes]);
            if (bytes > 0) await Sock.ReceiveAsync(buffer, SocketFlags.None);
            return buffer.ToArray();
        }
        async private Task<byte[]> SerialReceiveData(int length, int ms = 250)
        {
            DateTime t0 = DateTime.Now;
            TimeSpan tstop;
            int bytes;
            do
            {
                bytes = Port.BytesToRead;
                tstop = DateTime.Now - t0;
            }
            while (bytes < length && tstop.Milliseconds <= ms);
            byte[] buffer = new byte[bytes];
            if (buffer.Length > 0) await Port.BaseStream.ReadAsync(buffer, 0, bytes);
            return buffer;
        }
        async public Task<Tuple<byte[], ProtocolReply>> GetData(byte[] cmdOut, int size, int ms = 50)
        {
            void PraseCmd(byte[] cmdOut, out CmdInput cmdMain, out CmdInput cmdThrough)
            {
                CmdOutput cmdOne = (CmdOutput)((cmdOut[2] << 8) | cmdOut[3]);
                switch (cmdOne)
                {
                    case CmdOutput.ROUTING_THROUGH:
                    case CmdOutput.ROUTING_PROG:
                        CmdOutput cmdTwo = (CmdOutput)((cmdOut[6] << 8) | cmdOut[7]);
                        Enum.TryParse(Enum.GetName(typeof(CmdOutput), cmdOne), out cmdThrough);
                        Enum.TryParse(Enum.GetName(typeof(CmdOutput), cmdTwo), out cmdMain);
                        break;
                    default:
                        Enum.TryParse(Enum.GetName(typeof(CmdOutput), cmdOne), out cmdMain);
                        cmdThrough = CmdInput.ROUTING_THROUGH;
                        break;
                }
            }

            sendData(cmdOut);

            Task<byte[]> receiveTask = receiveData(size, Options.through ? ms * 2 : ms);

            PraseCmd(cmdOut, out CmdInput cmdMain, out CmdInput cmdThrough);

            await Task.WhenAll(receiveTask);

            byte[] cmdIn = receiveTask.Result;

            ProtocolReply reply = Options.through 
                ? GetReply(cmdIn, new byte[2] { cmdOut[0], cmdOut[1] }, cmdThrough,
                                          new byte[2] { cmdOut[4], cmdOut[5] }, cmdMain)
                : GetReply(cmdIn, new byte[2] { cmdOut[0], cmdOut[1] }, cmdMain);

            reply = 
                (reply == ProtocolReply.Ok && cmdMain == CmdInput.LOAD_DATA_PAGE)
                ? GetDataReply(cmdIn, cmdOut) 
                : reply;

            if (reply != ProtocolReply.Ok) throw new Exception(reply.ToString());

            return new Tuple<byte[], ProtocolReply>(cmdIn, reply);
        }
    }
}
