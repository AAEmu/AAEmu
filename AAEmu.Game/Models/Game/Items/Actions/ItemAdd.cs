using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemAdd : ItemTask
{
    private readonly Item _item;

    public ItemAdd(Item item)
    {
        _type = ItemAction.Create; // 5
        _item = item;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);

        stream.Write(_item);

        return stream;
    }
}
