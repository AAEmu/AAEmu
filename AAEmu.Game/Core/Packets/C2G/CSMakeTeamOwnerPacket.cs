using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMakeTeamOwnerPacket : GamePacket
    {
        public CSMakeTeamOwnerPacket() : base(CSOffsets.CSMakeTeamOwnerPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var memberId = stream.ReadUInt32();

            // _log.Warn("MakeTeamOwner, TeamId: {0}, MemberId: {1}", teamId, memberId);
            TeamManager.Instance.MakeTeamOwner(Connection.ActiveChar, teamId, memberId);
        }
    }
}
