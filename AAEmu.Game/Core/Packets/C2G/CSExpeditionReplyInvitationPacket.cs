using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionReplyInvitationPacket : GamePacket
    {
        public CSExpeditionReplyInvitationPacket() : base(CSOffsets.CSExpeditionReplyInvitationPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionReplyInvitationPacket");
        }
    }
}
