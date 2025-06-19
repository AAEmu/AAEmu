using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace AAEmu.Commons.Utils;

public static class Helpers
{
    private static Assembly _assembly;
    private static string _exePath;
    private static string _baseDirectory;

    /// <summary>
    /// 获取当前正在执行的入口程序集。
    /// </summary>
    public static Assembly Assembly => _assembly ?? (_assembly = Assembly.GetEntryAssembly());
    /// <summary>
    /// 获取当前执行程序集的文件路径。
    /// </summary>
    public static string ExePath => _exePath ?? (_exePath = Assembly.Location);
    /// <summary>
    /// 获取一个值，该值指示当前操作系统是否为64位操作系统。
    /// </summary>
    public static readonly bool Is64Bit = Environment.Is64BitOperatingSystem;

    /// <summary>
    /// 获取应用程序的基目录路径。
    /// </summary>
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

    /// <summary>
    /// 从指定的程序集中获取位于特定命名空间下的所有类型。
    /// </summary>
    /// <param name="sourceAssembly">要搜索的程序集。</param>
    /// <param name="nameSpace">要匹配的命名空间。</param>
    /// <returns>位于指定命名空间中的类型枚举。</returns>
    public static IEnumerable<Type> GetTypesInNamespace(Assembly sourceAssembly, string nameSpace)
    {
        return sourceAssembly.GetTypes().Where(t => string.Equals(t.Namespace, nameSpace, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>
    /// 将 <see cref="DateTime"/> 对象转换为 Unix 时间戳（自1970年1月1日以来的秒数）。
    /// </summary>
    /// <param name="time">要转换的 <see cref="DateTime"/> 对象。</param>
    /// <returns>表示给定时间的 Unix 时间戳。</returns>
    public static long UnixTime(DateTime time)
    {
        if (time <= DateTime.MinValue)
            return 0;
        if (time < DateTime.UnixEpoch) // 处理 Unix 纪元之前的时间
            return 0;
        var timeSpan = (time - DateTime.UnixEpoch);
        return (long)timeSpan.TotalSeconds;
    }

    /// <summary>
    /// 将 Unix 时间戳（自1970年1月1日以来的秒数）转换为 <see cref="DateTime"/> 对象。
    /// </summary>
    /// <param name="time">要转换的 Unix 时间戳。</param>
    /// <returns>表示给定 Unix 时间戳的 <see cref="DateTime"/> 对象。</returns>
    public static DateTime UnixTime(long time)
    {
        if (time > DateTime.MaxValue.Second) // 防止溢出
            return DateTime.MaxValue;

        if (time < DateTime.MinValue.Second) // 处理非常早的时间戳
            return DateTime.MinValue;

        return DateTime.UnixEpoch.AddSeconds(time);
    }

    /// <summary>
    /// 获取当前的 Unix 时间戳（自1970年1月1日以来的秒数）。
    /// </summary>
    /// <returns>当前的 Unix 时间戳。</returns>
    public static long UnixTimeNow()
    {
        var timeSpan = (DateTime.UtcNow - DateTime.UnixEpoch);
        return (long)timeSpan.TotalSeconds;
    }

    /// <summary>
    /// 获取当前的 Unix 时间戳（自1970年1月1日以来的毫秒数）。
    /// </summary>
    /// <returns>当前的 Unix 时间戳（毫秒）。</returns>
    public static long UnixTimeNowInMilli()
    {
        var timeSpan = (DateTime.UtcNow - DateTime.UnixEpoch);
        return (long)timeSpan.TotalMilliseconds;
    }

    /// <summary>
    /// [已过时] 将字节数组转换为 X 坐标。请改用 <see cref="ConvertPosition(byte[])"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
    public static float ConvertX(byte[] coords)
    {
        return (float)Math.Round(coords[0] * 0.002f + coords[1] * 0.5f + coords[2] * 128, 4, MidpointRounding.ToEven);
    }

    /// <summary>
    /// [已过时] 将 X 坐标转换为字节数组。请改用 <see cref="ConvertPosition(float, float, float)"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
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

    /// <summary>
    /// [已过时] 将字节数组转换为 Y 坐标。请改用 <see cref="ConvertPosition(byte[])"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
    public static float ConvertY(byte[] coords)
    {
        return (float)Math.Round(coords[0] * 0.002f + coords[1] * 0.5f + coords[2] * 128, 4, MidpointRounding.ToEven);
    }

    /// <summary>
    /// [已过时] 将 Y 坐标转换为字节数组。请改用 <see cref="ConvertPosition(float, float, float)"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
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

    /// <summary>
    /// [已过时] 将字节数组转换为 Z 坐标。请改用 <see cref="ConvertPosition(byte[])"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
    public static float ConvertZ(byte[] coords)
    {
        return (float)Math.Round(coords[0] * 0.001f + coords[1] * 0.2561f + coords[2] * 65.5625f - 100, 4,
            MidpointRounding.ToEven);
    }

    /// <summary>
    /// [已过时] 将 Z 坐标转换为字节数组。请改用 <see cref="ConvertPosition(float, float, float)"/>。
    /// </summary>
    [Obsolete("This method is deprecated, it's better to use ConvertPosition", false)]
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

    /// <summary>
    /// 将包含压缩坐标数据的9字节数组转换为 (x, y, z) 浮点坐标元组。
    /// 转换涉及到位操作和特定于游戏格式的缩放因子。
    /// </summary>
    /// <param name="values">包含压缩坐标数据的9字节数组。</param>
    /// <returns>包含 x, y, z 坐标的元组。</returns>
    public static (float x, float y, float z) ConvertPosition(byte[] values)
    {
        // 从字节数组中提取和组合 X, Y, Z 的原始整数值
        var tempX = 8 * (values[0] + ((values[1] + (values[2] << 8)) << 8));
        var flagX = (int)(((-(values[8] & 0x80) >> 30) & 0xFFFFFFFE) + 1); // 根据最高位确定符号
        var resX = ((long)tempX << 32) * flagX;

        var tempY = 8 * (values[3] + ((values[4] + (values[5] << 8)) << 8));
        var flagY = (((-(values[8] & 0x40) >> 30) & 0xFFFFFFFE) + 1); // 根据次高位确定符号
        var resY = ((long)tempY << 32) * flagY;

        var tempZ = (ulong)(values[6] + ((values[7] + ((values[8] & 0x3f) << 8)) << 8)); // Z 值使用剩余的位

        // 将原始整数值转换为浮点坐标
        var resultX = ConvertLongX(resX);
        var resultY = ConvertLongY(resY);
        var resultZ = (float)Math.Round(tempZ * 0.00000023841858 * 4196 - 100, 4, MidpointRounding.ToEven);

        return (resultX, resultY, resultZ);
    }

    /// <summary>
    /// 将浮点坐标 (x, y, z) 转换为用于网络传输的9字节压缩格式。
    /// 此转换是 <see cref="ConvertPosition(byte[])"/> 的逆操作。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    /// <param name="z">Z 坐标。</param>
    /// <returns>表示压缩坐标的9字节数组。</returns>
    public static byte[] ConvertPosition(float x, float y, float z)
    {
        var longX = ConvertLongX(x); // 将浮点 X 转换为长整型中间值
        var longY = ConvertLongY(y); // 将浮点 Y 转换为长整型中间值

        // 处理符号位和数值转换的特定逻辑
        var preX = longX >> 31;
        var preY = longY >> 31;

        var resultX = (preX ^ (longX + preX + (0 > preX ? 1 : 0))) >> 3;
        var resultY = (preY ^ (longY + preY + (0 > preY ? 1 : 0))) >> 3;
        var resultZ = (long)Math.Floor((z + 100f) / 4196f * 4194304f + 0.5); // Z 坐标的特定转换

        var position = new byte[9];
        // 将转换后的值打包到字节数组中
        position[0] = (byte)(resultX >> 32);
        position[1] = (byte)(resultX >> 40);
        position[2] = (byte)(resultX >> 48);

        position[3] = (byte)(resultY >> 32);
        position[4] = (byte)(resultY >> 40);
        position[5] = (byte)(resultY >> 48);

        position[6] = (byte)resultZ;
        position[7] = (byte)(resultZ >> 8);
        // 第8个字节的最后两位用于存储 X 和 Y 的符号信息
        position[8] = (byte)(((resultZ >> 16) & 0x3F) + (((y < 0 ? 1 : 0) + 2 * (x < 0 ? 1 : 0)) << 6));
        return position;
    }

    /// <summary>
    /// 将用于位置转换的长整型中间值转换为浮点 X 坐标。
    /// </summary>
    /// <param name="x">长整型 X 中间值。</param>
    /// <returns>浮点 X 坐标。</returns>
    public static float ConvertLongX(long x)
    {
        return (x >> 32) / 4096f;
    }

    /// <summary>
    /// 将浮点 X 坐标转换为用于位置转换的长整型中间值。
    /// </summary>
    /// <param name="x">浮点 X 坐标。</param>
    /// <returns>长整型 X 中间值。</returns>
    public static long ConvertLongX(float x)
    {
        return (long)(x * 4096) << 32;
    }

    /// <summary>
    /// 将用于位置转换的长整型中间值转换为浮点 Y 坐标。
    /// </summary>
    /// <param name="y">长整型 Y 中间值。</param>
    /// <returns>浮点 Y 坐标。</returns>
    public static float ConvertLongY(long y)
    {
        return (y >> 32) / 4096f;
    }

    /// <summary>
    /// 将浮点 Y 坐标转换为用于位置转换的长整型中间值。
    /// </summary>
    /// <param name="y">浮点 Y 坐标。</param>
    /// <returns>长整型 Y 中间值。</returns>
    public static long ConvertLongY(float y)
    {
        return (long)(y * 4096) << 32;
    }

    /// <summary>
    /// 将有符号字节表示的旋转值转换为短整型表示。
    /// 这可能涉及到特定于游戏单位或范围的缩放。
    /// </summary>
    /// <param name="rotation">有符号字节表示的旋转值。</param>
    /// <returns>短整型表示的旋转值。</returns>
    public static short ConvertRotation(sbyte rotation)
    {
        return (short)(rotation * 0.0078740157f / 0.000030518509f);
    }

    /// <summary>
    /// 将短整型表示的旋转值转换为有符号字节表示。
    /// 这可能涉及到特定于游戏单位或范围的缩放。
    /// </summary>
    /// <param name="rotation">短整型表示的旋转值。</param>
    /// <returns>有符号字节表示的旋转值。</returns>
    public static sbyte ConvertRotation(short rotation)
    {
        return (sbyte)(rotation * 0.000030518509f / 0.0078740157f);
    }

    /// <summary>
    /// 将十六进制字符串转换为字节数组。
    /// </summary>
    /// <param name="hex">要转换的十六进制字符串。</param>
    /// <returns>转换后的字节数组。</returns>
    public static byte[] StringToByteArray(string hex)
    {
        return Enumerable.Range(0, hex.Length)
            .Where(x => x % 2 == 0)
            .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
            .ToArray();
    }

    /// <summary>
    /// 将 IP 地址字符串转换为网络字节顺序（大端）的字节数组。
    /// </summary>
    /// <param name="ip">要转换的 IP 地址字符串。</param>
    /// <returns>表示 IP 地址的字节数组（网络字节顺序）。</returns>
    public static byte[] ConvertIp(string ip)
    {
        var result = IPAddress.Parse(ip);
        return result.GetAddressBytes().Reverse().ToArray(); // .Reverse() 用于确保大端字节序
    }

    /// <summary>
    /// 计算给定字节数组的 CRC8 校验和。
    /// </summary>
    /// <param name="data">要计算校验和的字节数组。</param>
    /// <param name="size">要处理的数组中的字节数。</param>
    /// <returns>计算出的 CRC8 校验和。</returns>
    public static byte Crc8(byte[] data, int size)
    {
        var len = size;
        uint checksum = 0;
        for (var i = 0; i <= len - 1; i++)
        {
            checksum *= 0x13; // CRC8 多项式因子
            checksum += data[i];
        }

        return (byte)(checksum);
    }

    /// <summary>
    /// 计算给定字节数组的 CRC8 校验和。处理整个数组。
    /// </summary>
    /// <param name="data">要计算校验和的字节数组。</param>
    /// <returns>计算出的 CRC8 校验和。</returns>
    public static byte Crc8(byte[] data)
    {
        var size = data.Length;
        return Crc8(data, size);
    }

    /// <summary>
    /// 将弧度值转换为有符号字节表示的方向值。
    /// 转换涉及将弧度映射到 [-127, 127] 的范围。
    /// </summary>
    /// <param name="radian">以弧度为单位的角度值。</param>
    /// <returns>表示方向的有符号字节。</returns>
    public static sbyte ConvertRadianToSbyteDirection(float radian)
    {
        var z = radian * 0.15915494309189533576888376337251; // 弧度到 [-1, 1] 范围的转换 (radian / (2 * PI))
        var dir = Convert.ToSByte(z * 127f); // 缩放到 sbyte 范围

        return dir;
    }

    // 使用 MemberwiseClone 克隆对象的方法
    public static T Clone<T>(T obj)
    {
        var inst = obj.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return (T)inst?.Invoke(obj, null);
    }
}
