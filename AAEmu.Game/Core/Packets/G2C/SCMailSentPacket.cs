using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailSentPacket : GamePacket
    {
        private readonly MailHeader _mail;
        private readonly (SlotType slotType, byte slot)[] _items;
        private readonly CountUnreadMail _count;

        public SCMailSentPacket(MailHeader mail, (SlotType slotType, byte slot)[] items, CountUnreadMail count)
            : base(SCOffsets.SCMailSentPacket, 5)
        {
            _mail = mail;
            _items = items;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mail);
            stream.Write(_count);
            foreach (var (slotType, slot) in _items) // TODO 10 items
            {
                stream.Write((byte)slotType); // type
                stream.Write(slot);           // index
            }

            return stream;
        }
    }
}
