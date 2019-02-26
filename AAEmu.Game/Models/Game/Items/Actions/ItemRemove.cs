using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemove : ItemTask
    {
        private readonly Item _item;

        public ItemRemove(Item item)
        {
            _type = 7;
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            stream.Write(_item.Id);
            stream.Write(_item.TemplateId);
            return stream;
        }
    }
}
