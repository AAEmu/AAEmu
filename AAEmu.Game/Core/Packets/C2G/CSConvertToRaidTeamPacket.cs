using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

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
            
            _log.Warn("ConvertToRaidTeam, TeamId: {0}", teamId);
        }
    }
}
