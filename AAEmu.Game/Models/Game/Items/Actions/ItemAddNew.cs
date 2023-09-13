using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemAddNew : ItemTask
    {
        private readonly Item _item;

        public ItemAddNew(Item item)
        {
            _item = item;
            _type = ItemAction.ChangeOwner; // 15
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
