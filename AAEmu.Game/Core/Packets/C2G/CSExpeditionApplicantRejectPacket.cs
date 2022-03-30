using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantRejectPacket : GamePacket
    {
        public CSExpeditionApplicantRejectPacket() : base(CSOffsets.CSExpeditionApplicantRejectPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionApplicantRejectPacket");
        }
    }
}
