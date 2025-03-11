using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

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
        _type = ItemAction.AddStack; // 4
        _item = item;
        _count = count;
        _tLogt = SetTlogT(_type, SlotType.Bag, _count < 0); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_item.SlotType); // type
        stream.Write((byte)_item.Slot);     // index
        stream.Write(_item.Id);             // id
        stream.Write(_count);               // amount
        stream.Write(_item.TemplateId);     // type
        return stream;
    }
}
