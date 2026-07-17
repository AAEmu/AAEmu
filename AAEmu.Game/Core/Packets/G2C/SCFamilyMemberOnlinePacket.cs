using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyMemberOnlinePacket(uint familyId, uint memberId, bool online)
    : GamePacket(SCOffsets.SCFamilyMemberOnlinePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(familyId);
        stream.Write(memberId);
        stream.Write(online);
        return stream;
    }
}
