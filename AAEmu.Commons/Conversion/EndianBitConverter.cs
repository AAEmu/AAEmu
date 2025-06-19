using System;

namespace AAEmu.Commons.Conversion;

/// <summary>
/// 等效于 System.BitConverter，但可以处理任一字节序。
/// </summary>
public abstract class EndianBitConverter
{
    #region Endianness of this converter

    /// <summary>
    /// 指示此类转换数据时使用的字节顺序（“字节序”）。
    /// </summary>
    /// <remarks>
    /// 不同的计算机体系结构使用不同的字节顺序存储数据。“大端”
    /// 表示最高有效字节位于字的最左端。“小端”表示
    /// 最高有效字节位于字的最右端。
    /// </remarks>
    /// <returns>如果此转换器是小端字节序，则为 true，否则为 false。</returns>
    public abstract bool IsLittleEndian();

    /// <summary>
    /// 指示此类转换数据时使用的字节顺序（“字节序”）。
    /// </summary>
    public abstract Endianness Endianness { get; }

    #endregion

    #region Factory properties

    private static readonly LittleEndianBitConverter s_little = new();

    /// <summary>
    /// 返回一个小端字节序转换器实例。始终返回相同的实例。
    /// </summary>
    public static LittleEndianBitConverter Little
    {
        get { return s_little; }
    }

    private static readonly BigEndianBitConverter s_big = new();

    /// <summary>
    /// 返回一个大端字节序转换器实例。始终返回相同的实例。
    /// </summary>
    public static BigEndianBitConverter Big
    {
        get { return s_big; }
    }

    #endregion

    #region Double/primitive conversions

    /// <summary>
    /// 将指定的双精度浮点数转换为 64 位有符号整数。
    /// 注意：此转换器的字节序不影响返回的值。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>其值等效于 value 的 64 位有符号整数。</returns>
    public static long DoubleToInt64Bits(double value)
    {
        return BitConverter.DoubleToInt64Bits(value);
    }

    /// <summary>
    /// 将指定的 64 位有符号整数转换为双精度浮点数。
    /// 注意：此转换器的字节序不影响返回的值。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>其值等效于 value 的双精度浮点数。</returns>
    public static double Int64BitsToDouble(long value)
    {
        return BitConverter.Int64BitsToDouble(value);
    }

    /// <summary>
    /// 将指定的单精度浮点数转换为 32 位有符号整数。
    /// 注意：此转换器的字节序不影响返回的值。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>其值等效于 value 的 32 位有符号整数。</returns>
    public unsafe int SingleToInt32Bits(float value)
    {
        return *((int*)&value);
    }

    /// <summary>
    /// 将指定的 32 位有符号整数转换为单精度浮点数。
    /// 注意：此转换器的字节序不影响返回的值。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>其值等效于 value 的单精度浮点数。</returns>
    public unsafe float Int32BitsToSingle(int value)
    {
        // TODO 返回 BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
        return *((float*)&value);
    }

    #endregion

    #region To(PrimitiveType) conversions

    /// <summary>
    /// 从字节数组中指定位置的一个字节转换并返回一个布尔值。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>如果 value 中 startIndex 位置的字节非零，则为 true；否则为 false。</returns>
    public static bool ToBoolean(byte[] value, int startIndex)
    {
        CheckByteArgument(value, startIndex, 1);
        return BitConverter.ToBoolean(value, startIndex);
    }

    /// <summary>
    /// 从字节数组中指定位置的两个字节转换并返回一个 Unicode 字符。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的两个字节组成的字符。</returns>
    public char ToChar(byte[] value, int startIndex)
    {
        return unchecked((char)(CheckedFromBytes(value, startIndex, 2)));
    }

    /// <summary>
    /// 从字节数组中指定位置的八个字节转换并返回一个双精度浮点数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的八个字节组成的双精度浮点数。</returns>
    public double ToDouble(byte[] value, int startIndex)
    {
        return Int64BitsToDouble(ToInt64(value, startIndex));
    }

    /// <summary>
    /// 从字节数组中指定位置的四个字节转换并返回一个单精度浮点数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的四个字节组成的单精度浮点数。</returns>
    public float ToSingle(byte[] value, int startIndex)
    {
        return Int32BitsToSingle(ToInt32(value, startIndex));
    }

    /// <summary>
    /// 从字节数组中指定位置的两个字节转换并返回一个 16 位有符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的两个字节组成的 16 位有符号整数。</returns>
    public short ToInt16(byte[] value, int startIndex)
    {
        return unchecked((short)(CheckedFromBytes(value, startIndex, 2)));
    }

    /// <summary>
    /// 从字节数组中指定位置的四个字节转换并返回一个 32 位有符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的四个字节组成的 32 位有符号整数。</returns>
    public int ToInt32(byte[] value, int startIndex)
    {
        return unchecked((int)(CheckedFromBytes(value, startIndex, 4)));
    }

    /// <summary>
    /// 从字节数组中指定位置的八个字节转换并返回一个 64 位有符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的八个字节组成的 64 位有符号整数。</returns>
    public long ToInt64(byte[] value, int startIndex)
    {
        return CheckedFromBytes(value, startIndex, 8);
    }

    /// <summary>
    /// 从字节数组中指定位置的两个字节转换并返回一个 16 位无符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的两个字节组成的 16 位无符号整数。</returns>
    public ushort ToUInt16(byte[] value, int startIndex)
    {
        return unchecked((ushort)(CheckedFromBytes(value, startIndex, 2)));
    }

    /// <summary>
    /// 从字节数组中指定位置的四个字节转换并返回一个 32 位无符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的四个字节组成的 32 位无符号整数。</returns>
    public uint ToUInt32(byte[] value, int startIndex)
    {
        return unchecked((uint)(CheckedFromBytes(value, startIndex, 4)));
    }

    /// <summary>
    /// 从字节数组中指定位置的八个字节转换并返回一个 64 位无符号整数。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的八个字节组成的 64 位无符号整数。</returns>
    public ulong ToUInt64(byte[] value, int startIndex)
    {
        return unchecked((ulong)(CheckedFromBytes(value, startIndex, 8)));
    }

    /// <summary>
    /// 检查给定参数的有效性。
    /// </summary>
    /// <param name="value">传入的字节数组</param>
    /// <param name="startIndex">传入的起始索引</param>
    /// <param name="bytesRequired">所需的字节数</param>
    /// <exception cref="ArgumentNullException">value 为空引用</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// startIndex 小于零或大于 value 的长度减去 bytesRequired。
    /// </exception>
    private static void CheckByteArgument(byte[] value, int startIndex, int bytesRequired)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(value));
        if (startIndex < 0 || startIndex > value.Length - bytesRequired)
            throw new ArgumentOutOfRangeException(nameof(startIndex));
    }

    /// <summary>
    /// 在调用 FromBytes 之前检查参数的有效性
    /// （因此 FromBytes 可以假定参数有效）。
    /// </summary>
    /// <param name="value">检查后要转换的字节</param>
    /// <param name="startIndex">要转换的第一个字节的索引</param>
    /// <param name="bytesToConvert">要转换的字节数</param>
    /// <returns></returns>
    private long CheckedFromBytes(byte[] value, int startIndex, int bytesToConvert)
    {
        CheckByteArgument(value, startIndex, bytesToConvert);
        return FromBytes(value, startIndex, bytesToConvert);
    }

    /// <summary>
    /// 从给定数组的给定起始位置转换给定数量的字节为 long 类型，
    /// 将这些字节用作 long 类型的最低有效部分。
    /// 调用此方法时，已检查参数的有效性。
    /// </summary>
    /// <param name="value">要转换的字节</param>
    /// <param name="startIndex">要转换的第一个字节的索引</param>
    /// <param name="bytesToConvert">转换中要使用的字节数</param>
    /// <returns>转换后的数字</returns>
    protected abstract long FromBytes(byte[] value, int startIndex, int bytesToConvert);

    #endregion

    #region ToString conversions

    /// <summary>
    /// 从字节数组的元素转换并返回一个字符串。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <remarks>value 的所有元素都将被转换。</remarks>
    /// <returns>
    /// 一个由连字符分隔的十六进制对字符串，其中每对
    /// 表示 value 中的相应元素；例如，“7F-2C-4A”。
    /// </returns>
    public static string ToString(byte[] value)
    {
        return BitConverter.ToString(value);
    }

    /// <summary>
    /// 从字节数组中从指定数组位置开始的元素转换并返回一个字符串。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <remarks>从数组位置 startIndex 到数组末尾的元素将被转换。</remarks>
    /// <returns>
    /// 一个由连字符分隔的十六进制对字符串，其中每对
    /// 表示 value 中的相应元素；例如，“7F-2C-4A”。
    /// </returns>
    public static string ToString(byte[] value, int startIndex)
    {
        return BitConverter.ToString(value, startIndex);
    }

    /// <summary>
    /// 从字节数组中指定位置的指定数量字节转换并返回一个字符串。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <param name="length">要转换的字节数。</param>
    /// <remarks>从数组位置 startIndex 开始的 length 个元素将被转换。</remarks>
    /// <returns>
    /// 一个由连字符分隔的十六进制对字符串，其中每对
    /// 表示 value 中的相应元素；例如，“7F-2C-4A”。
    /// </returns>
    public static string ToString(byte[] value, int startIndex, int length)
    {
        return BitConverter.ToString(value, startIndex, length);
    }

    #endregion

    #region	Decimal conversions

    /// <summary>
    /// 从字节数组中指定位置的十六个字节转换并返回一个 decimal 值。
    /// </summary>
    /// <param name="value">字节数组。</param>
    /// <param name="startIndex">value 中的起始位置。</param>
    /// <returns>由 startIndex 开始的十六个字节组成的 decimal 值。</returns>
    public decimal ToDecimal(byte[] value, int startIndex)
    {
        // HACK（注）：这里总是假设有四个部分，每个部分都有自己的字节序，
        // 从字节数组开头的第一个部分开始。
        // 另一方面，没有指定真正的格式…
        var parts = new int[4];
        for (var i = 0; i < 4; i++)
            parts[i] = ToInt32(value, startIndex + i * 4);
        return new(parts);
    }

    /// <summary>
    /// 将指定的 decimal 值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 16 的字节数组。</returns>
    public byte[] GetBytes(decimal value)
    {
        var bytes = new byte[16];
        var parts = decimal.GetBits(value);
        for (var i = 0; i < 4; i++)
            CopyBytesImpl(parts[i], 4, bytes, i * 4);
        return bytes;
    }

    /// <summary>
    /// 将指定的 decimal 值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的字符。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(decimal value, byte[] buffer, int index)
    {
        var parts = decimal.GetBits(value);
        for (var i = 0; i < 4; i++)
            CopyBytesImpl(parts[i], 4, buffer, i * 4 + index);
    }

    #endregion

    #region GetBytes conversions

    /// <summary>
    /// 返回一个包含给定数量字节的数组，这些字节由指定值的
    /// 最低有效字节构成。
    /// 此方法用于实现其他 GetBytes 方法。
    /// </summary>
    /// <param name="value">要获取字节的值</param>
    /// <param name="bytes">要返回的有效字节数</param>
    public byte[] GetBytes(long value, int bytes)
    {
        var buffer = new byte[bytes];
        CopyBytes(value, bytes, buffer, 0);
        return buffer;
    }

    /// <summary>
    /// 将指定的布尔值作为字节数组返回。
    /// </summary>
    /// <param name="value">布尔值。</param>
    /// <returns>长度为 1 的字节数组。</returns>
    public static byte[] GetBytes(bool value)
    {
        return BitConverter.GetBytes(value);
    }

    /// <summary>
    /// 将指定的 Unicode 字符值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的字符。</param>
    /// <returns>长度为 2 的字节数组。</returns>
    public byte[] GetBytes(char value)
    {
        return GetBytes(value, 2);
    }

    /// <summary>
    /// 将指定的双精度浮点值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 8 的字节数组。</returns>
    public byte[] GetBytes(double value)
    {
        return GetBytes(DoubleToInt64Bits(value), 8);
    }

    /// <summary>
    /// 将指定的 16 位有符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 2 的字节数组。</returns>
    public byte[] GetBytes(short value)
    {
        return GetBytes(value, 2);
    }

    /// <summary>
    /// 将指定的 32 位有符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 4 的字节数组。</returns>
    public byte[] GetBytes(int value)
    {
        return GetBytes(value, 4);
    }

    /// <summary>
    /// 将指定的 64 位有符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 8 的字节数组。</returns>
    public byte[] GetBytes(long value)
    {
        return GetBytes(value, 8);
    }

    /// <summary>
    /// 将指定的单精度浮点值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 4 的字节数组。</returns>
    public byte[] GetBytes(float value)
    {
        return GetBytes(SingleToInt32Bits(value), 4);
    }

    /// <summary>
    /// 将指定的 16 位无符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 2 的字节数组。</returns>
    public byte[] GetBytes(ushort value)
    {
        return GetBytes(value, 2);
    }

    /// <summary>
    /// 将指定的 32 位无符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 4 的字节数组。</returns>
    public byte[] GetBytes(uint value)
    {
        return GetBytes(value, 4);
    }

    /// <summary>
    /// 将指定的 64 位无符号整数值作为字节数组返回。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <returns>长度为 8 的字节数组。</returns>
    public byte[] GetBytes(ulong value)
    {
        return GetBytes(unchecked((long)value), 8);
    }

    #endregion

    #region CopyBytes conversions

    /// <summary>
    /// 从指定值的最低有效端复制给定数量的字节到指定的字节数组中，
    /// 从指定的索引开始。
    /// 此方法用于实现其他 CopyBytes 方法。
    /// </summary>
    /// <param name="value">要为其复制字节的值</param>
    /// <param name="bytes">要复制的有效字节数</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    private void CopyBytes(long value, int bytes, byte[] buffer, int index)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer), "Byte array must not be null");
        if (buffer.Length < index + bytes)
            throw new ArgumentOutOfRangeException(nameof(buffer), "Buffer not big enough for value");

        CopyBytesImpl(value, bytes, buffer, index);
    }

    /// <summary>
    /// 从指定值的最低有效端复制给定数量的字节到指定的字节数组中，
    /// 从指定的索引开始。
    /// 这必须在具体的派生类中实现，但实现
    /// 可以假定值将适合缓冲区。
    /// </summary>
    /// <param name="value">要为其复制字节的值</param>
    /// <param name="bytes">要复制的有效字节数</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    protected abstract void CopyBytesImpl(long value, int bytes, byte[] buffer, int index);

    /// <summary>
    /// 将指定的布尔值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">布尔值。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(bool value, byte[] buffer, int index)
    {
        CopyBytes(value ? 1 : 0, 1, buffer, index);
    }

    /// <summary>
    /// 将指定的 Unicode 字符值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的字符。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(char value, byte[] buffer, int index)
    {
        CopyBytes(value, 2, buffer, index);
    }

    /// <summary>
    /// 将指定的双精度浮点值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(double value, byte[] buffer, int index)
    {
        CopyBytes(DoubleToInt64Bits(value), 8, buffer, index);
    }

    /// <summary>
    /// 将指定的 16 位有符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(short value, byte[] buffer, int index)
    {
        CopyBytes(value, 2, buffer, index);
    }

    /// <summary>
    /// 将指定的 32 位有符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(int value, byte[] buffer, int index)
    {
        CopyBytes(value, 4, buffer, index);
    }

    /// <summary>
    /// 将指定的 64 位有符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(long value, byte[] buffer, int index)
    {
        CopyBytes(value, 8, buffer, index);
    }

    /// <summary>
    /// 将指定的单精度浮点值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(float value, byte[] buffer, int index)
    {
        CopyBytes(SingleToInt32Bits(value), 4, buffer, index);
    }

    /// <summary>
    /// 将指定的 16 位无符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(ushort value, byte[] buffer, int index)
    {
        CopyBytes(value, 2, buffer, index);
    }

    /// <summary>
    /// 将指定的 32 位无符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(uint value, byte[] buffer, int index)
    {
        CopyBytes(value, 4, buffer, index);
    }

    /// <summary>
    /// 将指定的 64 位无符号整数值复制到指定的字节数组中，
    /// 从指定的索引开始。
    /// </summary>
    /// <param name="value">要转换的数字。</param>
    /// <param name="buffer">要将字节复制到的字节数组</param>
    /// <param name="index">复制字节到数组的起始索引</param>
    public void CopyBytes(ulong value, byte[] buffer, int index)
    {
        CopyBytes(unchecked((long)value), 8, buffer, index);
    }

    #endregion
}
