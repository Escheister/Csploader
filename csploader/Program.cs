using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.IO;
using System;

using BootloaderProtocol;
using StaticSettings;
using ProtocolEnums;
using Extensions;
using System.Reflection;

namespace csploader
{
    class UploaderException : ArgumentException
    {
        public override string Message { get; }
        public double ExitCode { get; }

        public UploaderException(string arg, string val, double code)
        {
            ExitCode = code;
            Message = $"\r\nКод ошибки: <{code}>\r\n{exitCodes[code]}\r\n";
            if (code > 10.0)
            {
                Message += $"Некорректнное значение для аргумента <{arg}>: {val}\r\n";
                if (code == 10.1)
                    Message += $"Доступные устройства:\r\n{string.Join("\r\n", SerialPort.GetPortNames())}\r\n";
            }
            else Message += exitCodes[code];
        }
        public UploaderException(string arg, double code)
        {
            ExitCode = code;
            Message = $"\r\nКод ошибки: <{code}>\r\n{exitCodes[code]} <{arg}>\r\n";
        }
        public UploaderException(double code)
        {
            ExitCode = code;
            Message = $"\r\nКод ошибки: <{code}>\r\n{exitCodes[code]}";
        }
        public UploaderException(double[] code)
        {
            ExitCode = code[0];
            foreach(double num in code)
                Message += $"\r\nКод ошибки: <{num}>\r\n{exitCodes[num]}";
        }

        private readonly Dictionary<double, string> exitCodes = new Dictionary<double, string>()
        {
            [1] =       "Нет интерфейса для дальнейшей работы",
            [2] =       "Невозможно определить тип интерфейса",
            [7] =       "Нет ответа от устройства",
            [10.1] =    "Serial Device: Устройство не найдено или используется другой программой.\r\nУбедитесь в формате ввода: [ser=<serial_dev>]",
            [10.2] =    "Baudrate: Допустимые значения: 9600, 19200, 38400 ... 1000000",
            [10.3] =    "Databits: Заданное число должно быть 7 или 8",
            [10.4] =    "Parity: Допускаются варианты: None | N, Even | E",
            [10.5] =    "Stopbits: Заданное число должно быть 1 или 2",
            [11] =      "Ethernet: Значение должно содержать IP и Port. Убедитесь в формате ввода: [eth=<ip:port>]",
            [11.1] =    "IP: Адрес должен соответствовать формату IPv4",
            [11.2] =    "IP: Адрес не отвечает",
            [11.3] =    "Port: Заданное число должно быть от 0 до 65535",
            [20.1] =    "Sig: Заданное число должно быть от 0 до 65535",
            [20.2] =    "Through: Заданное число должно быть от 0 до 65535",
            [30.1] =    "Filepath: Расширение файла должно оканчиваться на .hex",
            [30.2] =    "Filepath: Файл не найден, возможно его не существует",
            [40] =      "Pagesize: Количество байт должно быть кратно 16",
            [50] =      "Seconds: Заданное число должно быть от 2 до 999",
            [100] =     "Неизвестный аргумент"
        };
    }
    internal class Program
    {
        private static void GetHelp()
        {
            Console.Write(
" Использование:                                             \r\n" +
" software.exe /arg1 [option1] /arg2 [option2] ...           \r\n\r\n" +
" Аргумент              Описание и использование             \r\n" +
" ---------------------------------------------------------- \r\n" +
" /i                    : Интерфейс подключения              \r\n" +
"    [ser=<serial_dev>] > {COM1,/dev/ttyS0,USB1...}          \r\n" +
"    [eth=<ip:port>]    > {IPv4:0-65535}                     \r\n" +
"  Дополнительные настройки Serial:                          \r\n" +
"    [br=<baudrate>]    > {115200,9600...}    default <38400>\r\n" +
"    [db=<databits>]    > {7,8}               default <8>    \r\n" +
"    [p=<parity>]       > {N,E}               default <N>    \r\n" +
"    [sb=<stopbits>]    > {1,2}               default <1>    \r\n\r\n" +
" /s                    : Сигнатура устройства               \r\n" +
"    [Sig]              > {0-65535}           default <0>    \r\n" +
"    [Sig:Through]      > {0-65535:0-65535}                  \r\n\r\n" +
" /f                    : Путь к файлу с расширением .hex    \r\n" +
"    [Filepath]                                              \r\n\r\n" +
" /p                    : Размер пакета            \r\n" +
"    [Pagesize]         > {16,32,48...}       defaut <64>    \r\n\r\n" +
" /t                    : Время ожидания ответа       \r\n" +
"    [Seconds]          > {2-999}             default <30>   \r\n\r\n" +
" /nocrc                : Пропускает проверку CRC            \r\n" +
" /norepeat             : Пропускает вопрос о повторе");
            Console.ReadKey(true);
        }
        private static void TrySetSettings(string[] args)
        {
            if (!args.Contains("/i")) throw new UploaderException(1);
            args = string.Join(" ", args).Split('/').Where(x => x != "").ToArray();
            foreach (string s in args)
            {
                List<string> argsList = new List<string>(s.Split());
                string argument = argsList[0];
                argsList.RemoveAt(0);
                string value = string.Join(" ", argsList).Trim();
                switch (argument.ToLower())
                {
                    case "i":
                        if (!value.Contains("eth") && !value.Contains("ser"))
                            throw new UploaderException(new double[] { 1, 10.1, 11 });
                        if (value.Contains('='))
                            ParseInterface(argument, value);
                        break;
                    case "s":
                        ushort targetSign;
                        if (value.Contains(':'))
                        {
                            string[] values = value.Split(':');

                            if (ushort.TryParse(values[0], out targetSign)) Options.targetSign = targetSign;
                            else throw new UploaderException(argument, values[0], 20.1);

                            if (ushort.TryParse(values[1], out ushort throughSign)) Options.throughSign = throughSign;
                            else throw new UploaderException(argument, values[1], 20.2);

                            Options.through = true;
                        }
                        else
                        {
                            if (ushort.TryParse(value, out targetSign)) Options.targetSign = targetSign;
                            else throw new UploaderException(argument, value, 20.1);
                        }
                        break;
                    case "f":
                        if (File.Exists(value))
                        {
                            if (new FileInfo(value).Extension == ".hex") Options.hexPath = value;
                            else throw new UploaderException(argument, value, 30.1);
                        }
                        else throw new UploaderException(argument, value, 30.2);
                        break;
                    case "p":
                        if (byte.TryParse(value, out byte size) && size > 0 && size % 16 == 0 && size <= 128)
                            Options.hexPageSize = size;
                        else throw new UploaderException(argument, value, 40);
                        break;
                    case "t":
                        if (int.TryParse(value, out int seconds) && seconds >= 2 && seconds <= 999)
                            Options.hexTimeout = seconds;
                        else throw new UploaderException(argument, value, 50);
                        break;
                    case "nocrc":
                        Options.checkCrc = false;
                        break;
                    case "norepeat":
                        Options.repeat = false;
                        break;
                    default: throw new UploaderException(argument, 100);
                }
            }
        }
        private static void ParseInterface(string arg, string value)
        {
            foreach (string option in value.Split())
            {
                string key = option.Split('=')[0].ToLower();
                string val = option.Split('=')[1].ToLower();
                switch (key)
                {
                    case "ser":
                        Options.portName = val;
                        try {
                            using (SerialPort sp = new SerialPort(val))
                            {
                                sp.Open();
                                sp.Close();
                            }
                        } catch { throw new UploaderException(arg, value, 10.1); }
                        break;
                    case "eth":
                        if (!val.Contains(":")) throw new UploaderException(arg, value, 11);
                        string valueIP = val.Split(':')[0];
                        if (valueIP.Split('.').Length < 4) throw new UploaderException(arg, value, 11.1);
                        string valuePort = val.Split(':')[1];

                        if (IPAddress.TryParse(valueIP, out IPAddress ipAddr)
                            && ipAddr.AddressFamily == AddressFamily.InterNetwork) 
                            Options.ipAddr = ipAddr.ToString();
                        else throw new UploaderException(arg, value, 11.1);

                        if (ushort.TryParse(valuePort, out ushort portAddr)) Options.portAddr = portAddr;
                        else throw new UploaderException(arg, value, 11.2);

                        break;
                    case "br":
                        if (Int32.TryParse(val, out int baudrate))
                            Options.baudRate = baudrate;
                        else throw new UploaderException(arg, value, 10.2);
                        break;
                    case "db":
                        if (byte.TryParse(val, out byte databits)
                            && (databits == 7 || databits == 8))
                            Options.dataBits = databits;
                        else throw new UploaderException(arg, value, 10.3);
                        break;
                    case "p":
                        switch (val)
                        {
                            case "n": case "none":
                                Options.parity = Parity.None;
                                break;
                            case "e": case "even":
                                Options.parity = Parity.Even;
                                break;
                            default: throw new UploaderException(arg, value, 10.4);
                        }
                        break;
                    case "sb":
                        if (byte.TryParse(val, out byte stopbits)
                            && Enum.TryParse(Enum.GetName(typeof(StopBits), stopbits), out StopBits bits))
                            Options.stopBits = bits;
                        else throw new UploaderException(arg, value, 10.3);
                        break;
                }
            }
        }
        public static void Main(string[] args)
        {
            Console.WriteLine($"{Assembly.GetEntryAssembly().GetName().Name} {Assembly.GetEntryAssembly().GetName().Version}");
            if (args.Length == 0)
            {
                GetHelp();
                Environment.Exit(0);
            }
            else
            {
                try
                {
                    TrySetSettings(args);
                    Task<Tuple<bool, double>> task = TryStartHexUpload();
                    Task.WaitAll(task);
                    if (!task.Result.Item1) throw new UploaderException(task.Result.Item2);
                    Environment.Exit(0);
                }
                catch (UploaderException uex)
                {
                    Console.WriteLine(uex.Message);
                    Environment.Exit((int)uex.ExitCode);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Environment.Exit(9999);
                }
                finally { }
            }
        }
        async private static Task<Tuple<bool, double>> TryStartHexUpload()
        {
            async Task<bool> check_ip(IPAddress ipAddr)
            {
                using Ping ping = new Ping();
                byte[] buffer = new byte[32];
                PingOptions pingOptions = new PingOptions(buffer.Length, true);
                PingReply reply = await ping.SendPingAsync(ipAddr, 255, buffer, pingOptions);
                return reply.Status == IPStatus.Success;
            }

            if (Options.portName != string.Empty)
            {
                List<string> serDevs = new List<string>();
                foreach(string serDev in SerialPort.GetPortNames()) serDevs.Add(serDev.ToLower());
                if (!serDevs.Contains(Options.portName)) return new Tuple<bool, double>(false, 10.1);

                using (SerialPort serialPort =
                    new SerialPort(
                        Options.portName,
                        Options.baudRate,
                        Options.parity,
                        Options.dataBits,
                        Options.stopBits))
                {
                    serialPort.Open();
                    Console.WriteLine(
                        $"{serialPort.PortName}:" +
                        $"{serialPort.BaudRate}," +
                        $"{serialPort.DataBits}," +
                        $"{serialPort.Parity}," +
                        $"{serialPort.StopBits}");
                    Options.mainInterface = serialPort;
                    await HexUploadAsync();
                    serialPort.Close();
                };
            }
            else if (IPAddress.TryParse(Options.ipAddr, out IPAddress ipAddr))
            {
                if (!await check_ip(ipAddr)) return new Tuple<bool, double>(false, 11.2);
                using (Socket socket = new Socket( SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(ipAddr, Options.portAddr);
                    Console.WriteLine(
                        $"{((IPEndPoint)socket.RemoteEndPoint).Address.MapToIPv4()}:" +
                        $"{((IPEndPoint)socket.RemoteEndPoint).Port}");
                    Options.mainInterface = socket;
                    await HexUploadAsync();
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
            else return new Tuple<bool, double>(false, 2);
            return new Tuple<bool, double>(true, 0);
        }
        async private static Task HexUploadAsync()
        {
            Bootloader boot = Options.through
                ? new Bootloader(Options.mainInterface, Options.targetSign.GetBytes(), Options.throughSign.GetBytes())
                : new Bootloader(Options.mainInterface, Options.targetSign.GetBytes());

            Console.WriteLine(Options.through
                            ? $"Target sign:{Options.targetSign} through: {Options.throughSign}"
                            : $"Target sign:{Options.targetSign}");
            Console.WriteLine($"Page size:  {Options.hexPageSize}");
            Console.WriteLine($"Filename:   {Path.GetFileName(Options.hexPath)}");
            try { boot.SetQueueFromHex(Options.hexPath); }
            catch (Exception ex) { Console.WriteLine(ex.Message); return; }

            int allLines = boot.HexQueue.Count;

            async Task<bool> GetReplyFromDevice(byte[] cmdOut, int receiveDelay = 50, bool taskDelay = false, int delayMs = 25)
            {
                DateTime t0 = DateTime.Now;
                TimeSpan tstop = DateTime.Now - t0;
                string procPercent = $"[{(boot.HexQueue.Count() > 0 ? Math.Round(100.000 - (100.000 * boot.HexQueue.Count() / allLines), 2) : 100) : 000.00}%]";
                while (tstop.Seconds < Options.hexTimeout)
                {
                    try
                    {
                        Tuple<byte[], ProtocolReply> replyes = await boot.GetData(cmdOut, cmdOut.Length, receiveDelay);
                        Console.Write($"{$"{procPercent}", -10} {$"{replyes.Item2}", 4}");
                        Console.SetCursorPosition(15, Console.CursorTop);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.Write($"{$"{procPercent} {ex.Message}", 15}");
                        Console.SetCursorPosition(15, Console.CursorTop);
                        if ((DateTime.Now - t0).Seconds >= Options.hexTimeout)
                        {
                            if (!Options.repeat) return false;
                            Console.Write($"{"Retry?(Y | Any)", 15}");
                            ConsoleKeyInfo consoleKey = Console.ReadKey(true);
                            switch (consoleKey.Key)
                            {
                                case ConsoleKey.Y:
                                    t0 = DateTime.Now;
                                    Console.SetCursorPosition(15, Console.CursorTop);
                                    break;
                                default: return false;
                            }
                        }
                        if (taskDelay) await Task.Delay(delayMs);
                    }
                }
                return false;
            }

            byte[] cmdBootStart = boot.buildCmdDelegate(CmdOutput.START_BOOTLOADER);
            byte[] cmdBootStop = boot.buildCmdDelegate(CmdOutput.STOP_BOOTLOADER);
            byte[] cmdConfirmData = boot.buildCmdDelegate(CmdOutput.UPDATE_DATA_PAGE);

            Console.WriteLine();
            Console.Write($"{"Bootloader...", -15}");
            if (!await GetReplyFromDevice(cmdBootStart, taskDelay: true)) throw new UploaderException(7);
            boot.PageSize = Options.hexPageSize;
            int startFrom = boot.HexQueue.Count();
            Console.WriteLine();
            Console.Write($"{"Firmware...", -15}");

            Stopwatch stopwatchQueue = Stopwatch.StartNew();

            while (boot.HexQueue.Count() > 0)
            {
                boot.GetDataForUpload(out byte[] dataOutput);
                if (!await GetReplyFromDevice(boot.buildDataCmdDelegate(dataOutput), receiveDelay: 150)) throw new UploaderException(7);
                if (!await GetReplyFromDevice(cmdConfirmData, taskDelay: true, delayMs: 10)) throw new UploaderException(7);
            }
            await GetReplyFromDevice(cmdBootStop, taskDelay: true);
            stopwatchQueue.Stop();
            string timeUplod = $"{stopwatchQueue.Elapsed.Minutes:00}:{stopwatchQueue.Elapsed.Seconds:00}:{stopwatchQueue.Elapsed.Milliseconds:000}";

            Console.WriteLine();
            Console.WriteLine($"Firmware uploaded for {timeUplod}\n");
        }
    }
}
