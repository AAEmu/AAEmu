using System;

using AAEmu.Commons.Network;

using Discord;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemove : ItemTask
    {
        private readonly Item _item;

        public ItemRemove(Item item)
        {
            _type = ItemAction.Remove; // 7
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_item.SlotType); // type
            stream.Write((byte)_item.Slot);     // index
            stream.Write(_item.Id);             // id
            stream.Write(_item.Count);          // stack ? 
            stream.Write(DateTime.MinValue);    // removeReservationTime
            stream.Write(_item.TemplateId);     // type ?
            stream.Write((uint)0);              // dbSlaveId
            stream.Write((uint)0);              // type
            return stream;
        }
    }
}
