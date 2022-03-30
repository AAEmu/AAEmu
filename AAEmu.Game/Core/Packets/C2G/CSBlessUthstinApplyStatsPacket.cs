using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBlessUthstinApplyStatsPacket : GamePacket
    {
        public CSBlessUthstinApplyStatsPacket() : base(CSOffsets.CSBlessUthstinApplyStatsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSBlessUthstinApplyStatsPacket");
        }
    }
}
