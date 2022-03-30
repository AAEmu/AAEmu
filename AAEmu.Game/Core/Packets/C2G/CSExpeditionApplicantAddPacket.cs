using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantAddPacket : GamePacket
    {
        public CSExpeditionApplicantAddPacket() : base(CSOffsets.CSExpeditionApplicantAddPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionApplicantAddPacket");
        }
    }
}
