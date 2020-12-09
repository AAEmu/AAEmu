using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUpdateBits : ItemTask
    {
        private readonly ulong _itemId;
        private readonly SlotType _slotType;
        private readonly byte _slot;
        private readonly byte _bits;

        public ItemUpdateBits(Item item)
        {
            _itemId = item.Id;
            _slotType = item.SlotType;
            _slot = (byte)item.Slot;
            _bits = (byte)item.ItemFlags;
            _type = ItemAction.SetFlagsBits; // 10
            // 10 image
            // 20 unwrapp
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_slotType);
            stream.Write(_slot);
            stream.Write(_itemId);
            stream.Write(_bits);
            stream.Write((ulong)0);
            return stream;
        }
    }
}
