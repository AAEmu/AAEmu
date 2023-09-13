using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAskRiskyTeamActionPacket : GamePacket
    {
        public CSAskRiskyTeamActionPacket() : base(CSOffsets.CSAskRiskyTeamActionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var targetId = stream.ReadUInt32();
            var riskyAction = (RiskyAction)stream.ReadByte(); // ra

            // _log.Warn("AskRiskyTeamAction, TeamId: {0}, Id: {1}, RiskyAction: {2}", teamId, targetId, riskyAction);
            TeamManager.Instance.AskRiskyTeam(Connection.ActiveChar, teamId, targetId, riskyAction);
        }
    }
}
