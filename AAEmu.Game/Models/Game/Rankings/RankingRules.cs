namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>
/// The decisions the ranking boards need: which equipped item an item board measures, and what the
/// server can honestly put in a board. Nothing here invents a value — a board with no source is left out
/// of the answer rather than sent as a zero.
/// </summary>
public static class RankingRules
{
    /// <summary>
    /// The item boards, by the detail id the shipped table gives them, mapped to the holdable slot types
    /// they measure. The board rows carry only that id (24/25/26), so the mapping is written out from the
    /// shipped `holdables` rows: slot 14/15/17 hold the one-hand weapons (including the offhand-only
    /// ones), 16 the two-hand weapons and 18 the ranged ones.
    /// </summary>
    private static readonly Dictionary<uint, byte[]> ItemBoardSlotTypes = new()
    {
        [24] = [14, 15, 17], // 한손무기, one-hand
        [25] = [16],         // 양손무기, two-hand
        [26] = [18]          // 원거리무기, ranged
    };

    /// <summary>The holdable slot types an item board measures, or null when the board is not an item board.</summary>
    public static byte[] ItemBoardSlots(uint boardId)
    {
        return ItemBoardSlotTypes.TryGetValue(boardId, out var slots) ? slots : null;
    }

    /// <summary>
    /// The best-scoring of the given equipped items that sits in one of the board's slots. Null when the
    /// character wears nothing of that kind, which is the honest answer for a board like "two-hand weapon"
    /// on a character who uses one hand.
    /// </summary>
    public static (ulong ItemId, int Score)? BestItem(IEnumerable<(ulong ItemId, byte SlotTypeId, int Score)> items,
        IEnumerable<byte> slotTypes)
    {
        if (items == null || slotTypes == null)
            return null;

        var wanted = new HashSet<byte>(slotTypes);
        (ulong ItemId, int Score)? best = null;
        foreach (var item in items)
        {
            if (!wanted.Contains(item.SlotTypeId))
                continue;

            if (best == null || item.Score > best.Value.Score)
                best = (item.ItemId, item.Score);
        }

        return best;
    }
}
