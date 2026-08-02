using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Clears a bag/equip slot on the client.
/// Take body and <b>re-sets</b> the item into the slot — do not use it to destroy.
/// </summary>
public class ItemRemoveSlot : ItemTask
{
    private readonly ulong _itemId;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly byte _actionOwnerType;

    public ItemRemoveSlot(Item item, byte actionOwnerType = 0)
    {
        _type = ItemAction.Seize; // 14 = 0xE
        _itemId = item.Id;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
        _actionOwnerType = actionOwnerType;
    }

    public ItemRemoveSlot(ulong itemId, SlotType slotType, byte slot, byte actionOwnerType = 0)
    {
        _type = ItemAction.Seize; // 14 = 0xE
        _itemId = itemId;
        _slotType = slotType;
        _slot = slot;
        _actionOwnerType = actionOwnerType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_actionOwnerType);
        stream.Write((byte)_slotType);
        stream.Write(_slot);
        stream.Write(_itemId);
        return stream;
    }
}
