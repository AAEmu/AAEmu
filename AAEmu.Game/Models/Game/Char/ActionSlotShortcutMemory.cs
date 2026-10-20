namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Shortcuts the client cleared while a skill or buff item was about to change slots.
/// </summary>
public sealed class ActionSlotShortcutMemory
{
    private readonly List<ActionSlotShortcutRules.ClearedShortcut> _pending = [];
    private readonly List<(ulong ItemId, uint TemplateId, DateTime Utc)> _moves = [];
    private readonly List<(ulong ItemId, uint TemplateId, DateTime Utc)> _uses = [];

    public void Remember(byte slot, ActionSlotType type, ulong actionId, bool keepsShortcut, DateTime utc)
    {
        _pending.RemoveAll(pending =>
            pending.Slot == slot ||
            !ActionSlotShortcutRules.WithinWindow(pending.Utc, utc, ActionSlotShortcutRules.RestoreWindow));

        if (!keepsShortcut)
            return;
        if (type is not (ActionSlotType.ItemId or ActionSlotType.ItemType) || actionId == 0)
            return;

        _pending.Add(new ActionSlotShortcutRules.ClearedShortcut(slot, type, actionId, utc));
    }

    public List<ActionSlotShortcutRules.ClearedShortcut> TakeRestores(ulong itemId, uint templateId, DateTime utc)
    {
        var matched = new List<ActionSlotShortcutRules.ClearedShortcut>();
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            if (!ActionSlotShortcutRules.WithinWindow(pending.Utc, utc, ActionSlotShortcutRules.RestoreWindow))
            {
                _pending.RemoveAt(i);
                continue;
            }

            if (!ActionSlotShortcutRules.MatchesMove(pending.Type, pending.ActionId, itemId, templateId))
                continue;

            matched.Add(pending);
            _pending.RemoveAt(i);
        }

        return matched;
    }

    /// <summary>
    /// A skill-item move that may be followed by the client's shortcut clear.
    /// </summary>
    public void NoteMove(ulong itemId, uint templateId, DateTime utc)
    {
        _moves.RemoveAll(move =>
            !ActionSlotShortcutRules.WithinWindow(move.Utc, utc, ActionSlotShortcutRules.RestoreWindow));
        if (itemId == 0 && templateId == 0)
            return;
        _moves.Add((itemId, templateId, utc));
    }

    public bool WasMoved(ActionSlotType type, ulong actionId, DateTime utc)
    {
        foreach (var move in _moves)
        {
            if (!ActionSlotShortcutRules.WithinWindow(move.Utc, utc, ActionSlotShortcutRules.RestoreWindow))
                continue;
            if (ActionSlotShortcutRules.MatchesMove(type, actionId, move.ItemId, move.TemplateId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The item just cast its use-skill / buff. The client often clears the shortcut right after.
    /// </summary>
    public void NoteUse(ulong itemId, uint templateId, DateTime utc)
    {
        _uses.RemoveAll(use =>
            !ActionSlotShortcutRules.WithinWindow(use.Utc, utc, ActionSlotShortcutRules.UseProtectWindow));
        if (itemId == 0 && templateId == 0)
            return;
        _uses.Add((itemId, templateId, utc));
    }

    public bool WasUsed(ActionSlotType type, ulong actionId, DateTime utc)
    {
        foreach (var use in _uses)
        {
            if (!ActionSlotShortcutRules.WithinWindow(use.Utc, utc, ActionSlotShortcutRules.UseProtectWindow))
                continue;
            if (ActionSlotShortcutRules.MatchesMove(type, actionId, use.ItemId, use.TemplateId))
                return true;
        }

        return false;
    }
}
