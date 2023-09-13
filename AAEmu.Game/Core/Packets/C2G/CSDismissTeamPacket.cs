using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDismissTeamPacket : GamePacket
    {
        public CSDismissTeamPacket() : base(CSOffsets.CSDismissTeamPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();

            _log.Warn("DismissTeam, TeamId: {0}", teamId);
        }
    }
}
