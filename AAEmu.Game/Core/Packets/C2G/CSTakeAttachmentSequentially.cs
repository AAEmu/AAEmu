using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentSequentially : GamePacket
    {
        public CSTakeAttachmentSequentially() : base(0x09f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            _log.Debug("TakeAttachmentSequentially, mailId: {0}", mailId);
        }
    }
}
