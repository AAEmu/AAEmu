using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailAttachmentTakenPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly bool _money;
        private readonly bool _aaPoint;
        private readonly bool _takeSequentially;
        private readonly ulong[] _itemsId;
        private readonly (SlotType slotType, byte slot)[] _items;

        public SCMailAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, ulong[] itemsId, (SlotType slotType, byte slot)[] items)
            : base(SCOffsets.SCMailAttachmentTakenPacket, 5)
        {
            _mailId = mailId;
            _money = money;
            _aaPoint = aaPoint;
            _takeSequentially = takeSequentially;
            _itemsId = itemsId;
            _items = items;

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);              // type
            stream.Write(_money);               // money
            stream.Write(_aaPoint);             // aaPoint
            stream.Write(_takeSequentially);    // takeSequentially
            stream.Write((byte)_itemsId.Length); // num
            foreach (var itemId in _itemsId)
                stream.Write(itemId);           // id

            foreach (var (slotType, slot) in _items) // TODO should be 10 items
            {
                stream.Write((byte)slotType);   // type
                stream.Write(slot);             // index
            }

            return stream;
        }
    }
}
