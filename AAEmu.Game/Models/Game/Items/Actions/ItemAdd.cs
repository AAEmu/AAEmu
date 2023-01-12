using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemAdd : ItemTask
    {
        private readonly Item _item;

        public ItemAdd(Item item)
        {
            _type = ItemAction.Create;
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            WriteDetails(stream, _item);

            return stream;
        }
    }
}
