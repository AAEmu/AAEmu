using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConvertToRaidTeamPacket : GamePacket
    {
        public CSConvertToRaidTeamPacket() : base(CSOffsets.CSConvertToRaidTeamPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();

            // _log.Warn("ConvertToRaidTeam, TeamId: {0}", teamId);
            TeamManager.Instance.ConvertToRaid(Connection.ActiveChar, teamId);
        }
    }
}
