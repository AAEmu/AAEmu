using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTeamAcceptHandOverOwnerPacket : GamePacket
    {
        public CSTeamAcceptHandOverOwnerPacket() : base(CSOffsets.CSTeamAcceptHandOverOwnerPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTeamAcceptHandOverOwnerPacket");
        }
    }
}
