using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReportSpamPacket : GamePacket
    {
        public CSReportSpamPacket() : base(CSOffsets.CSReportSpamPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            
            _log.Debug("ReportSpam, mailId: {0}", mailId);
        }
    }
}
