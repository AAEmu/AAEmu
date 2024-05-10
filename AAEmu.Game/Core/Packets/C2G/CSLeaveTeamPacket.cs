using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveTeamPacket : GamePacket
{
    public CSLeaveTeamPacket() : base(CSOffsets.CSLeaveTeamPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadUInt32();

        Logger.Warn("LeaveTeam, TeamId: {0}", teamId);

        TeamManager.Instance.MemberRemoveFromTeam(Connection.ActiveChar, Connection.ActiveChar, Models.Game.Team.RiskyAction.Leave);
    }
}
