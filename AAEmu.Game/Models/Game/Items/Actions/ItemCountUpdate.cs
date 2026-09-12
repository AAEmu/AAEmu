using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemCountUpdate : ItemTask
{
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly ulong _itemId;
    private readonly int _count;
    private readonly uint _templateId;

    /// <summary>
    /// Add or subtracts count from the item count of a given item
    /// </summary>
    /// <param name="item">Item to update</param>
    /// <param name="count">Amount to add or subtract</param>
    public ItemCountUpdate(Item item, int count)
        : this(item.SlotType, checked((byte)item.Slot), item.Id, count, item.TemplateId)
    {
    }

    public ItemCountUpdate(SlotType slotType, byte slot, ulong itemId, int count, uint templateId)
    {
        // Case 4 (AddStack) is only templateId u32 + amount i64 — wrong for bag stack deltas.
        _type = ItemAction.Create;
        _slotType = slotType;
        _slot = slot;
        _itemId = itemId;
        _count = count;
        _templateId = templateId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_slotType);
        stream.Write(_slot);

        stream.Write(_itemId);
        stream.Write(_count);
        stream.Write(_templateId);
        return stream;
    }
}
