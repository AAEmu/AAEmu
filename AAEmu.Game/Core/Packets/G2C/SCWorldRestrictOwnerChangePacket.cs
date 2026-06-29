using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_WORLD_RESTRICT_OWNER_CHANGE (674). Body:
// single bool `worldRestrictOwnerChange`.
public class SCWorldRestrictOwnerChangePacket(bool worldRestrictOwnerChange) : GamePacket(SCOffsets.SCWorldRestrictOwnerChangePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldRestrictOwnerChange);
        return stream;
    }
}
