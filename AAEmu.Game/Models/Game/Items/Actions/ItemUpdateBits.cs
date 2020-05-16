using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUpdateBits : ItemTask
    {
        private readonly Item _item;
        private readonly byte _bits;

        public ItemUpdateBits(Item item, ItemFlag bits)
        {
            _item = item;
            _bits = (byte)bits;
            _type = 0xA; // 10
            // 10 image
            // 20 unwrapp
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            stream.Write(_item.Id);
            stream.Write(_bits);
            stream.Write((ulong)0);
            return stream;
        }
    }
}
