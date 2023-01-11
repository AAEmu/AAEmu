using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUpdate : ItemTask
    {
        private readonly Item _item;

        public ItemUpdate(Item item)
        {
            _type = ItemAction.UpdateDetail; // 9
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType); // type
            stream.Write((byte)_item.Slot);     // index
            stream.Write(_item.Id);             // item
            var details = new PacketStream();
            details.Write((byte)_item.DetailType);
            _item.WriteDetails(details);
            stream.Write((short)128);
            stream.Write(details, false);   // detail
            stream.Write(new byte[128 - details.Count]);
            return stream;
        }
    }
}
