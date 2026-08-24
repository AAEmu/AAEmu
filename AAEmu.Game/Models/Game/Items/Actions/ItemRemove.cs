using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Destroy/remove an item from the client bag.
/// 10.0.2.13: ItemAction.Remove (7) shares Take's full-Item body and the apply path
/// This type now emits Seize so existing <c>new ItemRemove(item)</c> call sites keep working.
/// </summary>
public class ItemRemove : ItemTask
{
    private readonly ulong _itemId;
    private readonly SlotType _slotType;
    private readonly byte _slot;
    private readonly byte _actionOwnerType;

    public ItemRemove(Item item, byte actionOwnerType = 0)
    {
        _type = ItemAction.Seize; // 14 — clear slot (not Remove=7)
        _itemId = item.Id;
        _slotType = item.SlotType;
        _slot = (byte)item.Slot;
        _actionOwnerType = actionOwnerType;
    }

    public ItemRemove(ulong itemId, SlotType slotType, byte slotNumber, uint itemTemplateId, byte actionOwnerType = 0)
    {
        _ = itemTemplateId; // retained for call-site compat; Seize wire has no templateId
        _type = ItemAction.Seize;
        _itemId = itemId;
        _slotType = slotType;
        _slot = slotNumber;
        _actionOwnerType = actionOwnerType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        SeizeBody.Write(stream, _actionOwnerType, _slotType, _slot, _itemId);
        return stream;
    }
}
