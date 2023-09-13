using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemCountUpdate : ItemTask
    {
        private readonly Item _item;
        private readonly int _count;

        /// <summary>
        /// Add or subtracts count from the item count of a given item
        /// </summary>
        /// <param name="item">Item to update</param>
        /// <param name="count">Amount to add or subtract</param>
        public ItemCountUpdate(Item item, int count)
        {
            _type = ItemAction.AddStack;
            _item = item;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);

            stream.Write(_item.Id);
            stream.Write(_count);
            stream.Write(_item.TemplateId);
            return stream;
        }
    }
}
