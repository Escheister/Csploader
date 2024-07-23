using System.IO.Ports;

namespace StaticSettings
{
    static class Options
    {
        //interfaces
        public static object mainInterface;
        public static bool mainIsAvailable = false;

        //socket
        public static string ipAddr { get; set; } = "";
        public static ushort portAddr { get; set; } = 0;

        //serial
        public static string portName { get; set; } = "";
        public static int baudRate { get; set; } = 38400;
        public static byte dataBits { get; set; } = 8;
        public static Parity parity { get; set; } = Parity.None;
        public static StopBits stopBits { get; set; } = StopBits.One;

        //signature
        public static int targetSign { get; set; } = 0;
        public static bool through { get; set; } = false;
        public static int throughSign { get; set; } = 0;

        //Hex uploader
        public static byte hexPageSize { get; set; } = 64;
        public static string hexPath { get; set; } = "";
        public static int hexTimeout { get; set; } = 30;
        public static bool checkCrc { get; set; } = true;
        public static bool repeat {  get; set; } = true;
    }
}
