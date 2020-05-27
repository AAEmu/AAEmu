using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConvertToRaidTeamPacket : GamePacket
    {
        public CSConvertToRaidTeamPacket() : base(0x80, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();

            // _log.Warn("ConvertToRaidTeam, TeamId: {0}", teamId);
            TeamManager.Instance.ConvertToRaid(DbLoggerCategory.Database.Connection.ActiveChar, teamId);
        }
    }
}
