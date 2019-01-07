using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemove : ItemTask
    {
        private Item _item;

        public ItemRemove(Item item)
        {
            _type = 7;
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte) 0); // v
            stream.Write((byte) _item.SlotType); // v
            stream.Write((byte) 0); // v
            stream.Write((byte) _item.Slot); // v
            stream.Write(_item.Id);
            stream.Write(_item.TemplateId);
            return stream;
        }
    }
}