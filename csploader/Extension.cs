using System.Linq;
using System;

namespace Extensions
{
    public static class ExtensionClass
    {
        public static byte[] GetBytes(this int intNum) => BitConverter.GetBytes(Convert.ToUInt16(intNum));
        public static byte[] GetReverseBytes(this ushort ushortNum) => BitConverter.GetBytes(ushortNum).Reverse().ToArray();
    }
}
