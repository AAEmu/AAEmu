using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemoveSlot : ItemTask
    {
        private readonly ulong _itemId;
        private readonly SlotType _slotType;
        private readonly byte _slot;

        public ItemRemoveSlot(Item item)
        {
            _type = 0xD; // 13

            _itemId = item.Id;
            _slotType = item.SlotType;
            _slot = (byte)item.Slot;
        }

        public ItemRemoveSlot(ulong itemId,SlotType slotType,byte slot)
        {
            _type = 0xD; // 13

            _itemId = itemId;
            _slotType = slotType;
            _slot = slot;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_slotType);
            stream.Write(_slot);
            stream.Write(_itemId);
            return stream;
        }
    }
}
