using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionInvitePacket : GamePacket
    {
        public CSExpeditionInvitePacket() : base(CSOffsets.CSExpeditionInvitePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionInvitePacket");
        }
    }
}
