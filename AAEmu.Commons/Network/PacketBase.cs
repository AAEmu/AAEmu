namespace AAEmu.Commons.Network;

public abstract class PacketBase<T> : PacketMarshaler
{
    public ushort TypeId { get; }

    public T Connection { protected get; set; }
    public virtual PacketLogLevel LogLevel => PacketLogLevel.Debug;

    protected PacketBase(ushort typeId)
    {
        TypeId = typeId;
    }

    public abstract PacketStream Encode();
    public abstract PacketBase<T> Decode(PacketStream ps);
}
