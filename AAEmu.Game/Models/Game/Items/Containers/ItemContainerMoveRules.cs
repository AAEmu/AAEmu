namespace AAEmu.Game.Models.Game.Items.Containers;

/// <summary>
/// Chooses the client item-task shape when an existing item crosses containers.
/// </summary>
/// <remarks>
/// Equipment → Inventory must be a single SwapSlot. The client's SwapSlot apply rebinds the
/// action bar for that direction; RemoveSlot + Add never runs that path, so a skill/buff item
/// forced off (e.g. 2H unequip when a shield equips) stays grey on the hotbar until re-dragged.
/// </remarks>
public static class ItemContainerMoveRules
{
    /// <summary>
    /// True when the wire must carry SwapSlot instead of RemoveSlot + Add.
    /// </summary>
    public static bool UsesSwapSlot(SlotType from, SlotType to) =>
        from == SlotType.Equipment && to == SlotType.Inventory;
}
