
namespace AAEmu.Commons.Conversion;

/// <summary>
/// 转换器的字节序
/// </summary>
public enum Endianness
{
    /// <summary>
    /// 小端字节序 - 最低有效字节在前
    /// </summary>
    LittleEndian,
    /// <summary>
    /// 大端字节序 - 最高有效字节在前
    /// </summary>
    BigEndian
}
