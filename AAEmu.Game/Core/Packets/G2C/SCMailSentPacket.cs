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

        public SCMailSentPacket(MailHeader mail, (SlotType slotType, byte slot)[] items) : base(SCOffsets.SCMailSentPacket, 1)
        {
            _mail = mail;
            _items = items;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mail);
            foreach (var (slotType, slot) in _items) // TODO 10 items
            {
                stream.Write((byte)slotType);
                stream.Write(slot);
            }

            return stream;
        }
    }
}
