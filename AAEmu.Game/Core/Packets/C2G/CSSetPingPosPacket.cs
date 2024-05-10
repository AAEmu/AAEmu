using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSetPingPosPacket : GamePacket
{
    public CSSetPingPosPacket() : base(CSOffsets.CSSetPingPosPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var teamPingPos = new TeamPingPos();
        teamPingPos.Read(stream);

        Logger.Warn($"SetPingPos: teamId={teamPingPos.TeamId}, setPingType={teamPingPos.SetPingType}, flag={teamPingPos.Flag}");

        var owner = Connection.ActiveChar;
        owner.LocalPingPosition = teamPingPos;
        if (teamPingPos.TeamId > 0)
        {
            TeamManager.Instance.SetPingPos(owner, teamPingPos);
        }
        else
        {
            owner.SendPacket(new SCTeamPingPosPacket(teamPingPos));
        }
    }
}
