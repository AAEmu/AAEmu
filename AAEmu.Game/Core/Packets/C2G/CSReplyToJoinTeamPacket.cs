using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyToJoinTeamPacket() : GamePacket(CSOffsets.CSReplyToJoinTeamPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 team, bool party, u64 type, bool isReject, string char (cap 0x80),
        // bool isArea, i8 teamRole, i64 logEventId.
        var teamId = stream.ReadInt32();
        var isParty = stream.ReadBoolean();
        var ownerId = stream.ReadUInt64();
        var isReject = stream.ReadBoolean();
        var charName = stream.ReadString();
        var isArea = stream.ReadBoolean();
        var teamRole = (TeamRoleType)stream.ReadSByte();
        var logEventId = stream.ReadInt64();

        TeamManager.Instance.ReplyToJoinTeam(
            Connection.ActiveChar, teamId, isParty, ownerId, isReject, charName, isArea, teamRole, logEventId);
    }
}
