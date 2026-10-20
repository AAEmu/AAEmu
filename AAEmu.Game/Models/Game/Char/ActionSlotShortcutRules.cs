namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// A hotbar shortcut for an item that casts a skill or grants a buff.
/// </summary>
/// <remarks>
/// Using the item skill makes the client clear the shortcut immediately after the cast. Swapping
/// the item off can clear before or after the move. A plain clear, with no recent use or move of
/// that item, is the player taking the icon off the bar and must stay cleared.
/// </remarks>
public static class ActionSlotShortcutRules
{
    public static readonly TimeSpan RestoreWindow = TimeSpan.FromSeconds(1);
    /// <summary>How long after a skill-item cast a clear of that shortcut is treated as the use clear.</summary>
    public static readonly TimeSpan UseProtectWindow = TimeSpan.FromSeconds(5);

    public readonly record struct ClearedShortcut(byte Slot, ActionSlotType Type, ulong ActionId, DateTime Utc);

    public static bool KeepsShortcut(uint useSkillId, uint buffId) => useSkillId != 0 || buffId != 0;

    public static bool MatchesMove(ActionSlotType type, ulong actionId, ulong itemId, uint templateId) =>
        type switch
        {
            ActionSlotType.ItemId => itemId != 0 && actionId == itemId,
            ActionSlotType.ItemType => templateId != 0 && actionId == templateId,
            _ => false
        };

    public static bool WithinWindow(DateTime earlierUtc, DateTime laterUtc, TimeSpan window) =>
        laterUtc >= earlierUtc && laterUtc - earlierUtc <= window;

    /// <summary>
    /// A clear that arrives right after that item's skill cast, or while the item is mid-move,
    /// must not wipe the hotbar. A clear with neither behind it is intentional.
    /// When the same item already sits on another hotbar slot, the clear is a drag-to-slot move
    /// (not the use-clear), so keep is skipped even inside the use window.
    /// </summary>
    public static bool ShouldKeepAgainstClear(
        bool keepsShortcut,
        bool usedRecently,
        bool movedRecently,
        bool sameItemAlreadyOnAnotherSlot = false) =>
        keepsShortcut && (movedRecently || (usedRecently && !sameItemAlreadyOnAnotherSlot));

    /// <summary>
    /// Writes remembered shortcuts back into slots that are still empty. A slot filled since the
    /// clear is left as the player set it.
    /// </summary>
    public static bool ApplyRestore(ActionSlot[] slots, IReadOnlyList<ClearedShortcut> restores)
    {
        if (slots == null || restores == null || restores.Count == 0)
            return false;

        var changed = false;
        foreach (var restore in restores)
        {
            if (restore.Slot >= slots.Length)
                continue;
            var slot = slots[restore.Slot];
            if (slot == null || slot.Type != ActionSlotType.None)
                continue;
            slot.Type = restore.Type;
            slot.ActionId = restore.ActionId;
            changed = true;
        }

        return changed;
    }
}
