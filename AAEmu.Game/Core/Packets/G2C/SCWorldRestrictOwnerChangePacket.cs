using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_WORLD_RESTRICT_OWNER_CHANGE (674). Body per x2game-dev_dedicate sub_39C1B450:
// single bool `worldRestrictOwnerChange` (vtbl+248).
public class SCWorldRestrictOwnerChangePacket(bool worldRestrictOwnerChange) : GamePacket(SCOffsets.SCWorldRestrictOwnerChangePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldRestrictOwnerChange);
        return stream;
    }
}
