using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyRemovedPacket(uint familyId) : GamePacket(SCOffsets.SCFamilyRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(familyId);
        return stream;
    }
}
