using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInviteAreaToTeamPacket : GamePacket
    {
        public CSInviteAreaToTeamPacket() : base(0x07b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var isParty = stream.ReadBoolean();

            // _log.Warn("InviteAreaToTeam, TeamId: {0}, IsParty: {1}", teamId, isParty);
            TeamManager.Instance.InviteAreaToTeam(DbLoggerCategory.Database.Connection.ActiveChar, teamId, isParty);
        }
    }
}
