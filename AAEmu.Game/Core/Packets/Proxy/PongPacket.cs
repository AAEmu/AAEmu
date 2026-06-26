using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

/// <summary>
/// Pong. Two roles: (1) S->C reply to a client Ping (PingPacket.Read), built with real tm/when/local;
/// (2) C->S — the client's reply to our server-initiated keepalive ping arrives as opcode 0x013 and is
/// instantiated via the parameterless ctor; we don't need its RTT, so Read is a no-op. Level 2 (proxy
/// channel) bypasses the client's encryption gate (dispatcher case 2), so a plain pong is always accepted.
/// </summary>
public class PongPacket(long tm, long when, uint local) : GamePacket(PPOffsets.PongPacket, 2)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off; // keepalive — keep out of logs

    private readonly uint _world = (uint)(Environment.TickCount & int.MaxValue);

    // Parameterless ctor used by the C2S receive path (Activator.CreateInstance) for the client's
    // reply to our server-initiated ping.
    public PongPacket() : this(0, 0, 0)
    {
    }

    // C->S: the client's pong reply — nothing to process server-side.
    public override void Read(PacketStream stream)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tm);
        stream.Write(when);
        stream.Write((long)0); // elapsed
        stream.Write((long)_world * 1000); // world * 1000; remote
        stream.Write(local);
        stream.Write(_world); // TODO packet sleep 250ms...

        return stream;
    }
}
