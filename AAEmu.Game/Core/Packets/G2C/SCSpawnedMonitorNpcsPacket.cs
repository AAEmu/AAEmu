using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSpawnedMonitorNpcsPacket : GamePacket
{
    public SCSpawnedMonitorNpcsPacket() : base(SCOffsets.SCSpawnedMonitorNpcsPacket, 5)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        // hard coded
        stream.Write((short)2);
        stream.Write(16173);
        stream.Write(16174);
        return stream;
    }
}
