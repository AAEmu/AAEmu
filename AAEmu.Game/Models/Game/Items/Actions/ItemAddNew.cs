using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemAddNew : ItemTask
    {
        private readonly Item _item;

        public ItemAddNew(Item item)
        {
            _type = ItemAction.ChangeOwner; // 15
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType); // type
            stream.Write((byte)_item.Slot); // index
            WriteDetails(stream, _item);
            return stream;
        }
    }
}
