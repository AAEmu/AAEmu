namespace AAEmu.Commons.Network;

/// <summary>
/// 表示网络数据包的抽象基类。
/// </summary>
/// <typeparam name="T">与此数据包关联的连接或会话类型。</typeparam>
public abstract class PacketBase<T> : PacketMarshaler
{
    /// <summary>
    /// 获取此数据包类型的唯一标识符。
    /// </summary>
    public ushort TypeId { get; }

    /// <summary>
    /// 获取或（受保护地）设置与此数据包关联的连接或会话。
    /// </summary>
    public T Connection { protected get; set; }
    /// <summary>
    /// 获取此数据包的日志记录级别。默认为 Debug。
    /// 派生类可以重写此属性以指定不同的日志级别。
    /// </summary>
    public virtual PacketLogLevel LogLevel => PacketLogLevel.Debug;

    /// <summary>
    /// 初始化 <see cref="PacketBase{T}"/> 类的新实例。
    /// </summary>
    /// <param name="typeId">数据包类型的唯一标识符。</param>
    protected PacketBase(ushort typeId)
    {
        TypeId = typeId;
    }

    /// <summary>
    /// 将此数据包编码到 <see cref="PacketStream"/> 中以便进行网络传输。
    /// 此方法必须由派生类实现。
    /// </summary>
    /// <returns>包含已编码数据包数据的 <see cref="PacketStream"/>。</returns>
    public abstract PacketStream Encode();
    /// <summary>
    /// 从 <see cref="PacketStream"/> 解码数据以填充此数据包的属性。
    /// 此方法必须由派生类实现。
    /// </summary>
    /// <param name="ps">包含要解码的数据包数据的 <see cref="PacketStream"/>。</param>
    /// <returns>解码后的数据包实例（通常是 this）。</returns>
    public abstract PacketBase<T> Decode(PacketStream ps);
}
