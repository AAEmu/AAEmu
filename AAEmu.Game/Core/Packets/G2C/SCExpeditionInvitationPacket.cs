using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionInvitationPacket(uint invitorId, string invitorName, uint factionId, string factionName)
    : GamePacket(SCOffsets.SCExpeditionInvitationPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // invitorId is a plain 8-byte persistent Character.Id, not a Bc-encoded reference and not ObjId.
        stream.Write((ulong)invitorId);
        stream.Write(invitorName);
        stream.Write(factionId);
        stream.Write(factionName);
        return stream;
    }
}
