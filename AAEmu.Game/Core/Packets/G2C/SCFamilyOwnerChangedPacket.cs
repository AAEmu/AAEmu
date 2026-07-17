using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyOwnerChangedPacket(uint familyId, uint memberId)
    : GamePacket(SCOffsets.SCFamilyOwnerChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(familyId);
        stream.Write(memberId);
        return stream;
    }
}
