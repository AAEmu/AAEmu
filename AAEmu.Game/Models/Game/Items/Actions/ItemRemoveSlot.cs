using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemRemoveSlot : ItemTask
{
    private readonly ulong _itemId;
    private readonly SlotType _slotType;
    private readonly byte _slot;

    public ItemRemoveSlot(Item item)
    {
        _type = ItemAction.Seize; // 13

        _itemId = item.Id;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public ItemRemoveSlot(ulong itemId, SlotType slotType, byte slot)
    {
        _type = ItemAction.Seize; // 13

        _itemId = itemId;
        _slotType = slotType;
        _slot = slot;
        _tLogt = SetTlogT(_type, SlotType.Bag); // установим tLogt по значению ItemAction
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_slotType); // type
        stream.Write(_slot);           // index
        stream.Write(_itemId);         // itemId
        return stream;
    }
}
