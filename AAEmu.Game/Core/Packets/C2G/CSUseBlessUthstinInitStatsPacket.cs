using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUseBlessUthstinInitStatsPacket : GamePacket
    {
        public CSUseBlessUthstinInitStatsPacket() : base(CSOffsets.CSUseBlessUthstinInitStatsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSUseBlessUthstinInitStatsPacket");
        }
    }
}
