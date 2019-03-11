using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace AAEmu.Commons.Utils
{
    public static class Helpers
    {
        private static DateTime _unixDate = new DateTime(1970, 1, 1, 0, 0, 0);
        private static Assembly _assembly;
        private static string _exePath;
        private static string _baseDirectory;

        public static Assembly Assembly => _assembly ?? (_assembly = Assembly.GetEntryAssembly());
        public static string ExePath => _exePath ?? (_exePath = Assembly.Location);

        public static string BaseDirectory
        {
            get
            {
                if (_baseDirectory == null)
                {
                    try
                    {
                        _baseDirectory = ExePath;

                        if (_baseDirectory.Length > 0)
                            _baseDirectory = Path.GetDirectoryName(_baseDirectory);
                    }
                    catch
                    {
                        _baseDirectory = "";
                    }
                }

                return _baseDirectory;
            }
        }

        public static readonly bool Is64Bit = Environment.Is64BitOperatingSystem;

        public static IEnumerable<Type> GetTypesInNamespace(string nameSpace)
        {
            return Assembly.GetTypes().Where(t => string.Equals(t.Namespace, nameSpace, StringComparison.Ordinal)).ToArray();
        }

        public static long UnixTime(DateTime time)
        {
            if (time <= DateTime.MinValue)
                return 0;
            if (time < _unixDate)
                return 0;
            var timeSpan = (time - _unixDate);
            return (long)timeSpan.TotalSeconds;
        }

        public static DateTime UnixTime(long time)
        {
            return time == 0 ? DateTime.MinValue : _unixDate.AddSeconds(time);
        }

        public static long UnixTimeNow()
        {
            var timeSpan = (DateTime.Now - _unixDate);
            return (long)timeSpan.TotalSeconds;
        }

        public static long UnixTimeNowInMilli()
        {
            var timeSpan = (DateTime.Now - _unixDate);
            return (long)timeSpan.TotalMilliseconds;
        }

        public static float ConvertX(byte[] coords)
        {
            return (float)Math.Round(coords[0] * 0.002f + coords[1] * 0.5f + coords[2] * 128, 4, MidpointRounding.ToEven);
        }

        public static byte[] ConvertX(float x)
        {
            var coords = new byte[3];
            var temp = x;
            coords[2] = (byte)(temp / 128f);
            temp -= coords[2] * 128;
            coords[1] = (byte)(temp / 0.5f);
            temp -= coords[1] * 0.5f;
            coords[0] = (byte)(temp * 512);
            return coords;
        }

        public static float ConvertY(byte[] coords)
        {
            return (float)Math.Round(coords[0] * 0.002f + coords[1] * 0.5f + coords[2] * 128, 4, MidpointRounding.ToEven);
        }

        public static byte[] ConvertY(float y)
        {
            var coords = new byte[3];
            var temp = y;
            coords[2] = (byte)(temp / 128);
            temp -= coords[2] * 128;
            coords[1] = (byte)(temp / 0.5f);
            temp -= coords[1] * 0.5f;
            coords[0] = (byte)(temp * 512);
            return coords;
        }

        public static float ConvertZ(byte[] coords)
        {
            return (float)Math.Round(coords[0] * 0.001f + coords[1] * 0.2561f + coords[2] * 65.5625f - 100, 4,
                MidpointRounding.ToEven);
        }

        public static byte[] ConvertZ(float z)
        {
            var coords = new byte[3];
            var temp = z + 100;
            coords[2] = (byte)(temp / 65.5625f);
            temp -= coords[2] * 65.5625f;
            coords[1] = (byte)(temp / 0.2561);
            temp -= coords[1] * 0.2561f;
            coords[0] = (byte)(temp / 0.001);
            return coords;
        }

        public static float ConvertLongX(long x)
        {
            return (x >> 32) / 4096f;
        }

        public static long ConvertLongX(float x)
        {
            return (long)(x * 4096) << 32;
        }

        public static float ConvertLongY(long y)
        {
            return (y >> 32) / 4096f;
        }

        public static long ConvertLongY(float y)
        {
            return (long)(y * 4096) << 32;
        }

        public static short ConvertRotation(sbyte rotation)
        {
            return (short)(rotation * 0.0078740157f / 0.000030518509f);
        }

        public static sbyte ConvertRotation(short rotation)
        {
            return (sbyte)(rotation * 0.000030518509f / 0.0078740157f);
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static byte[] ConvertIp(string ip)
        {
            var result = IPAddress.Parse(ip);
            return result.GetAddressBytes().Reverse().ToArray();
        }

        public static byte Crc8(byte[] data, int size)
        {
            var len = size;
            uint checksum = 0;
            for (var i = 0; i <= len - 1; i++)
            {
                checksum *= 0x13;
                checksum += data[i];
            }

            return (byte)(checksum);
        }

        public static byte Crc8(byte[] data)
        {
            var size = data.Length;
            return Crc8(data, size);
        }
    }
}
