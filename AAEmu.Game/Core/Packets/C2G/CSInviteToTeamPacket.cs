using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInviteToTeamPacket() : GamePacket(CSOffsets.CSInviteToTeamPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 tid, i8 teamRole, string char (cap 0x80), i8 worldId.
        var teamId = stream.ReadInt32();
        var teamRole = (TeamRoleType)stream.ReadSByte();
        var targetName = stream.ReadString();
        var worldId = stream.ReadSByte();

        TeamManager.Instance.AskToJoin(Connection.ActiveChar, targetName, teamId, teamRole, worldId);
    }
}
