using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAskToJoinTeamPacket(
    int teamId,
    ulong inviterId,
    string inviterName,
    TeamRoleType teamRoleType,
    long logEventId)
    : GamePacket(SCOffsets.SCAskToJoinTeamPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 team, u64 type, string name (cap 0x80), i8 teamRoleType, i64 logEventId.
        stream.Write(teamId);
        stream.Write(inviterId);
        stream.Write(inviterName);
        stream.Write((sbyte)teamRoleType);
        stream.Write(logEventId);
        return stream;
    }
}
