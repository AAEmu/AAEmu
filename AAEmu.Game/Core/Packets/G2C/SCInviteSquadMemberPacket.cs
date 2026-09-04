using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInviteSquadMemberPacket(
    uint squadId,
    ulong worldCharKey,
    string inviterCharName,
    uint invitationId,
    SquadFieldType field)
    : GamePacket(SCOffsets.SCInviteSquadMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(squadId);
        stream.Write(worldCharKey);
        stream.Write(inviterCharName);
        SquadFieldTypeWire.WriteInline(stream, field);
        stream.Write(invitationId);
        return stream;
    }
}
