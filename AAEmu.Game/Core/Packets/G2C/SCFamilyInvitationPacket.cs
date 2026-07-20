using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyInvitationPacket(uint invitorId, string invitorName, uint familyId, string role)
    : GamePacket(SCOffsets.SCFamilyInvitationPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(invitorId);
        stream.Write(invitorName);
        stream.Write(familyId);
        stream.Write(role);
        return stream;
    }
}
