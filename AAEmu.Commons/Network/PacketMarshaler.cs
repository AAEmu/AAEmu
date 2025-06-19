using NLog;

namespace AAEmu.Commons.Network;

/// <summary>
/// 定义了可序列化到/反序列化自 <see cref="PacketStream"/> 的对象的抽象基类。
/// 派生类应重写 Read 和 Write 方法以实现特定对象的编组逻辑。
/// </summary>
public abstract class PacketMarshaler
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 从提供的 <see cref="PacketStream"/> 中读取数据以填充对象的属性。
    /// 默认实现会记录一个警告，提示此方法应在派生类中被重写。
    /// </summary>
    /// <param name="stream">包含要读取的数据的 <see cref="PacketStream"/>。</param>
    public virtual void Read(PacketStream stream)
    {
        Logger.Warn("{0} doesn't inherit Read()", GetType().FullName);
    }

    /// <summary>
    /// 将对象的属性写入提供的 <see cref="PacketStream"/>。
    /// 默认实现会记录一个警告，提示此方法应在派生类中被重写。
    /// </summary>
    /// <param name="stream">要将数据写入的 <see cref="PacketStream"/>。</param>
    /// <returns>写入数据后的 <see cref="PacketStream"/>。</returns>
    public virtual PacketStream Write(PacketStream stream)
    {
        Logger.Warn("{0} doesn't inherit Write()", GetType().FullName);
        return stream;
    }

    /// <summary>
    /// 返回此对象内容的详细字符串表示形式，通常用于日志记录。
    /// 默认实现返回一个空字符串。
    /// </summary>
    /// <returns>表示对象详细信息的字符串。</returns>
    public virtual string Verbose()
    {
        return string.Empty;
    }
}
