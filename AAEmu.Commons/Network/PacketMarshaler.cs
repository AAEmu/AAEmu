using NLog;

namespace AAEmu.Commons.Network;

public abstract class PacketMarshaler
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public virtual void Read(PacketStream stream)
    {
        Logger.Warn("{0} doesn't inherit Read()", GetType().FullName);
    }

    public virtual PacketStream Write(PacketStream stream)
    {
        Logger.Warn("{0} doesn't inherit Write()", GetType().FullName);
        return stream;
    }

    public virtual string Verbose()
    {
        return string.Empty;
    }
}
