using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemGradeChange : ItemTask
    {
        private readonly Item _item;
        private readonly byte _grade;

        public ItemGradeChange(Item item, byte newGrade)
        {
            _item = item;
            _grade = newGrade;
            _type = ItemAction.ChangeGrade; // 14
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write((byte)_item.SlotType);
            stream.Write((byte)_item.Slot);
            stream.Write(_item.Id);
            stream.Write(_grade);
            return stream;
        }
    }
}
