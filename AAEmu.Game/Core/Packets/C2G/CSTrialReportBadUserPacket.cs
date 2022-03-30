using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTrialReportBadUserPacket : GamePacket
    {
        public CSTrialReportBadUserPacket() : base(CSOffsets.CSTrialReportBadUserPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTrialReportBadUserPacket");
        }
    }
}
