using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyMemberAddedPacket(Family family, int addedIndex)
    : GamePacket(SCOffsets.SCFamilyMemberAddedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(family);
        stream.Write(addedIndex);
        return stream;
    }
}
