using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class PongPacket(long tm, long when, uint local) : GamePacket(PPOffsets.PongPacket, 2)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private readonly uint _world = (uint)(Environment.TickCount & int.MaxValue);

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
