using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentItemPacket : GamePacket
    {
        public CSTakeAttachmentItemPacket() : base(CSOffsets.CSTakeAttachmentItemPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();

            var item = new Item();
            item.Read(stream);

            stream.ReadByte();
            var slotType = stream.ReadByte();
            stream.ReadByte();
            var slotIndex = stream.ReadByte();

            Connection.ActiveChar.Mails.GetAttached(mailId, false, true, false);

            _log.Debug("TakeAttachmentItem");
        }
    }
}
