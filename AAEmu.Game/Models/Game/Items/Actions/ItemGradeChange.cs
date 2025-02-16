using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemGradeChange : ItemTask
{
    private readonly Item _item;
    private readonly byte _grade;

    public ItemGradeChange(Item item, byte newGrade)
    {
        _type = ItemAction.ChangeGrade; // 14
        _item = item;
        _grade = newGrade;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType); // type
        stream.Write((byte)_item.Slot);     // index
        stream.Write(_item.Id);             // itemId
        stream.Write(_grade);               // grade
        return stream;
    }
}
