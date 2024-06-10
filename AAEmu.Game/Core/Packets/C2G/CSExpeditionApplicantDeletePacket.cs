using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionApplicantDeletePacket : GamePacket
    {
        public CSExpeditionApplicantDeletePacket() : base(CSOffsets.CSExpeditionApplicantDeletePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSExpeditionApplicantDeletePacket");
        }
    }
}
