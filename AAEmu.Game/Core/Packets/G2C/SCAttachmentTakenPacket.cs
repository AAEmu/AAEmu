using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAttachmentTakenPacket : GamePacket
    {
        public readonly long _mailId;
        public readonly bool _money;
        public readonly bool _aaPoint;
        public readonly bool _takeSequentially;
        // public readonly ulong[] _itemId;
        // public readonly (SlotType slotType, byte slot)[] _itemSlots;
        public readonly List<ItemIdAndLocation> _itemsList;

        // public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, ulong[] itemId, (SlotType slotType, byte slot)[] itemSlots) : base(SCOffsets.SCAttachmentTakenPacket, 1)
        public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, List<ItemIdAndLocation> itemsList) : base(SCOffsets.SCAttachmentTakenPacket, 1)
        {
            _mailId = mailId;
            _money = money;
            _aaPoint = aaPoint;
            _takeSequentially = takeSequentially;
            //_itemId = itemId;
            //_itemSlots = itemSlots;
            _itemsList = itemsList;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            stream.Write(_money);
            stream.Write(_aaPoint);
            stream.Write(_takeSequentially);
            stream.Write((byte)_itemsList.Count);
            for (var i = 0; i < 10; i++)
            {
                if (i < _itemsList.Count)
                {
                    var item = _itemsList[i];
                    stream.Write(item.Id);
                    stream.Write((byte)item.SlotType);
                    stream.Write(item.Slot);
                }
                else
                {
                    stream.Write((ulong)0);
                    stream.Write((byte)0);
                    stream.Write((byte)0);
                }
            }
            /*
            stream.Write((byte)_itemId.Length);
            for (int i = 0; i < 10; i++)
            {
                if (_itemId.Length != 0 && i < _itemId.Length)
                    stream.Write(_itemId[i]);

                if (i < _itemSlots.Length)
                {
                    stream.Write((byte)_itemSlots[i].slotType);
                    stream.Write(_itemSlots[i].slot);
                }
                else
                {
                    stream.Write((byte)SlotType.None);
                    stream.Write(0);
                }
            }
            */

            return stream;
        }
    }
}
