using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUseBlessUthstinExtendMaxStatsPacket : GamePacket
    {
        public CSUseBlessUthstinExtendMaxStatsPacket() : base(CSOffsets.CSUseBlessUthstinExtendMaxStatsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSUseBlessUthstinExtendMaxStatsPacket");
        }
    }
}
