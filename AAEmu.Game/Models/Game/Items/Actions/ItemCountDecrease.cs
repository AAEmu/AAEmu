using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Subtracts a positive amount from an existing AA10 r575 item stack.
/// </summary>
public sealed class ItemCountDecrease : ItemTask
{
    private readonly Item _item;
    private readonly int _amount;

    public ItemCountDecrease(Item item, int amount)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A stack decrease must be positive.");

        _item = item;
        _amount = amount;
        _type = ItemAction.Create;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);
        stream.Write(_item.Id);
        stream.Write(-_amount);
        stream.Write(_item.TemplateId);

        return stream;
    }
}
