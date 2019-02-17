using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetTeamMemberRolePacket : GamePacket
    {
        public CSSetTeamMemberRolePacket() : base(0x085, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var memberId = stream.ReadUInt32();
            var role = stream.ReadByte();

            _log.Warn("SetTeamMemberRole, TeamId: {0}, MemberId: {1}, Role: {2}", teamId, memberId, role);
        }
    }
}
