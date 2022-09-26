using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemove : ItemTask
    {
        private readonly ulong _itemId;
        private readonly SlotType _slotType;
        private readonly byte _slot;
        private readonly uint _templateId;

        public ItemRemove(Item item)
        {
            _type = ItemAction.Remove; // 7
            _itemId = item.Id;
            _slotType = item.SlotType;
            _slot = (byte)item.Slot;
            _templateId = item.TemplateId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_slotType); // type
            stream.Write(_slot);           // index
            stream.Write(_itemId);         // id
            stream.Write(_templateId);
            stream.Write((ulong)0); //removeReservationTime
            stream.Write((uint)0);  //type
            stream.Write((uint)0);  //dbSlaveId
            stream.Write((uint)0);  //type
            return stream;
        }
    }
}
