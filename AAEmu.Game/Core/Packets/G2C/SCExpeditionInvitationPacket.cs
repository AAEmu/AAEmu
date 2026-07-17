using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionInvitationPacket(uint invitorId, string invitorName, uint factionId, string factionName)
    : GamePacket(SCOffsets.SCExpeditionInvitationPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(invitorId);
        stream.Write(invitorName);
        stream.Write(factionId);
        stream.Write(factionName);
        return stream;
    }
}
