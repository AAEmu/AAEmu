using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemAdd : ItemTask
    {
        private readonly Item _item;

        public ItemAdd(Item item)
        {
            _type = ItemAction.Create; // 5
            _item = item;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType); // type
            stream.Write((byte)_item.Slot);     // index

            stream.Write(_item.TemplateId);     // type
            stream.Write(_item.Id);             // id
            stream.Write(_item.Grade);          // type
            stream.Write((byte)_item.ItemFlags); // bounded
            stream.Write(_item.Count);           // stack

            var details = new PacketStream();
            details.Write((byte)_item.DetailType);
            _item.WriteDetails(details);

            stream.Write((short)128); // length details?
            stream.Write(details, false);
            stream.Write(new byte[128 - details.Count]);

            stream.Write(_item.CreateTime);   // creationTime
            stream.Write(_item.LifespanMins); // lifespanMins
            stream.Write(_item.MadeUnitId);   // type
            stream.Write(_item.WorldId);      // worldId
            stream.Write(_item.UnsecureTime); // unsecureDateTime
            stream.Write(_item.UnpackTime);   // unpackDateTime
            stream.Write(_item.ChargeUseSkillTime); // chargeUseSkillTime add in 3+
            return stream;
        }
    }
}
