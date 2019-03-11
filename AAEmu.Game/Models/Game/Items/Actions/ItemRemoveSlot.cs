using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemoveSlot : ItemTask
    {
        private readonly Item _item;

        public ItemRemoveSlot(Item item)
        {
            _item = item;
            _type = 0xD; // 13
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            stream.Write(_item.Id);
            return stream;
        }
    }
}
