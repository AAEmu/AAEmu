namespace AAEmu.Commons.Network;

public abstract class PacketBase<T> : PacketMarshaler
{
    public ushort TypeId { get; }

    public T Connection { protected get; set; }
    public virtual PacketLogLevel LogLevel => PacketLogLevel.Debug;

    // TEMP DIAGNOSTIC: set env AAEMU_LOG_ALL_PACKETS=1 to force EVERY packet (both directions, both channels)
    // to log — including normally-silent (Off) pings/pongs/movements — so we can see the exact last packet
    // before the client disconnects. Off packets are promoted to Warning so they are easy to grep.
    public static readonly bool ForceLogAllPackets =
        System.Environment.GetEnvironmentVariable("AAEMU_LOG_ALL_PACKETS") == "1";

    protected PacketLogLevel EffectiveLogLevel =>
        ForceLogAllPackets && LogLevel == PacketLogLevel.Off ? PacketLogLevel.Warning : LogLevel;

    protected PacketBase(ushort typeId)
    {
        TypeId = typeId;
    }

    public abstract PacketStream Encode();
    public abstract PacketBase<T> Decode(PacketStream ps);
}
