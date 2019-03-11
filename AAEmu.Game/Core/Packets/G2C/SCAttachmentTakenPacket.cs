using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAttachmentTakenPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly bool _money;
        private readonly bool _aaPoint;
        private readonly bool _takeSequentially;
        private readonly ulong[] _itemsId;
        private readonly (SlotType slotType, byte slot)[] _itemSlots;

        public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially,
            ulong[] itemsId, (SlotType slotType, byte slot)[] itemSlots) : base(SCOffsets.SCAttachmentTakenPacket, 1)
        {
            _mailId = mailId;
            _money = money;
            _aaPoint = aaPoint;
            _takeSequentially = takeSequentially;
            _itemsId = itemsId;
            _itemSlots = itemSlots;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            stream.Write(_money);
            stream.Write(_aaPoint);
            stream.Write(_takeSequentially);
            stream.Write((byte)_itemsId.Length);
            foreach (var (slotType, slot) in _itemSlots) // TODO 10 items
            {
                stream.Write((byte)slotType);
                stream.Write(slot);
            }

            return stream;
        }
    }
}
