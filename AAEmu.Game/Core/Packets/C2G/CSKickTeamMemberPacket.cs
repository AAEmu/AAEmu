using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSKickTeamMemberPacket : GamePacket
    {
        public CSKickTeamMemberPacket() : base(0x07e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var memberId = stream.ReadUInt32();

            _log.Warn("KickTeamMember, TeamId: {0}, MemberId: {1}", teamId, memberId);
        }
    }
}
