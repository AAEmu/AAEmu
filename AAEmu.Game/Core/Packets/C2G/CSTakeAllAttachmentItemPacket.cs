using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAllAttachmentItemPacket : GamePacket
    {
        public CSTakeAllAttachmentItemPacket() : base(CSOffsets.CSTakeAllAttachmentItemPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            Connection.ActiveChar.Mails.GetAttached(mailId, true, true, true);
            _log.Debug("CSTakeAllAttachmentItemPacket");
        }
    }
}
