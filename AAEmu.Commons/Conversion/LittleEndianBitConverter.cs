namespace AAEmu.Commons.Conversion;

/// <summary>
/// EndianBitConverter 的实现，用于小端字节数组的转换。
/// </summary>
public sealed class LittleEndianBitConverter : EndianBitConverter
{
    /// <summary>
    /// 指示此类转换数据时使用的字节顺序（“字节序”）。
    /// </summary>
    /// <remarks>
    /// 不同的计算机体系结构使用不同的字节顺序存储数据。“大端”
    /// 表示最高有效字节位于字的最左端。“小端”表示
    /// 最高有效字节位于字的最右端。
    /// </remarks>
    /// <returns>如果此转换器是小端字节序，则为 true，否则为 false。</returns>
    public override bool IsLittleEndian() => true;

    /// <summary>
    /// 指示此类转换数据时使用的字节顺序（“字节序”）。
    /// </summary>
    public override Endianness Endianness
    {
        get { return Endianness.LittleEndian; }
    }

    /// <summary>
    /// 从 value 复制指定数量的字节到 buffer，从 index 开始。
    /// </summary>
    /// <param name="value">要复制的值</param>
    /// <param name="bytes">要复制的字节数</param>
    /// <param name="buffer">要将字节复制到的缓冲区</param>
    /// <param name="index">起始索引</param>
    protected override void CopyBytesImpl(long value, int bytes, byte[] buffer, int index)
    {
        for (var i = 0; i < bytes; i++)
        {
            buffer[i + index] = unchecked((byte)(value & 0xff));
            value = value >> 8;
        }
    }

    /// <summary>
    /// 从给定缓冲区的指定数量的字节（从 startIndex 开始）构建并返回值。
    /// </summary>
    /// <param name="buffer">字节数组格式的数据</param>
    /// <param name="startIndex">要使用的第一个索引</param>
    /// <param name="bytesToConvert">要使用的字节数</param>
    /// <returns>从给定字节构建的值</returns>
    protected override long FromBytes(byte[] buffer, int startIndex, int bytesToConvert)
    {
        var endOffset = startIndex + bytesToConvert - 1;
        long ret = 0;
        for (var i = 0; i < bytesToConvert; i++)
            ret = unchecked((ret << 8) | buffer[endOffset - i]);
        return ret;
    }
}
