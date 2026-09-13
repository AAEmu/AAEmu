using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Clears a bag or equipment slot on the client by its owner, location, and item id.
/// </summary>
public class ItemRemoveSlot : ItemTask
{
    private readonly ulong _itemId;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly byte _actionOwnerType;

    public ItemRemoveSlot(Item item, byte actionOwnerType = 0)
    {
        _type = ItemAction.Seize;
        _itemId = item.Id;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
        _actionOwnerType = actionOwnerType;
    }

    public ItemRemoveSlot(ulong itemId, SlotType slotType, byte slot, byte actionOwnerType = 0)
    {
        _type = ItemAction.Seize;
        _itemId = itemId;
        _slotType = slotType;
        _slot = slot;
        _actionOwnerType = actionOwnerType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        SeizeBody.Write(stream, _actionOwnerType, _slotType, _slot, _itemId);
        return stream;
    }
}
