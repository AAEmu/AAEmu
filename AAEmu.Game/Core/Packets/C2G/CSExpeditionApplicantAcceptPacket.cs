using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantAcceptPacket : GamePacket
    {
        public CSExpeditionApplicantAcceptPacket() : base(CSOffsets.CSExpeditionApplicantAcceptPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionApplicantAcceptPacket");
        }
    }
}
