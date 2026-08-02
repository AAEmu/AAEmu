using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// single bool `worldRestrictOwnerChange` (vtbl+248).
public class SCWorldRestrictOwnerChangePacket(bool worldRestrictOwnerChange) : GamePacket(SCOffsets.SCWorldRestrictOwnerChangePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldRestrictOwnerChange);
        return stream;
    }
}
