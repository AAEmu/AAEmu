using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReportSpammerPacket : GamePacket
    {
        public CSReportSpammerPacket() : base(CSOffsets.CSReportSpammerPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSReportSpammerPacket");
        }
    }
}
