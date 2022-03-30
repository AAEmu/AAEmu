using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentSequentiallyPacket : GamePacket
    {
        public CSTakeAttachmentSequentiallyPacket() : base(CSOffsets.CSTakeAttachmentSequentiallyPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTakeAttachmentSequentiallyPacket");
        }
    }
}
