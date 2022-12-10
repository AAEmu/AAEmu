using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveTeamPacket : GamePacket
    {
        public CSLeaveTeamPacket() : base(CSOffsets.CSLeaveTeamPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            
            _log.Warn("LeaveTeam, TeamId: {0}", teamId);
        }
    }
}
