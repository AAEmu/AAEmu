namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How a synthesis effect-swap pays for the cast.
/// </summary>
/// <remarks>
/// The piece must still have <c>EvolveChance</c> left — zero remaining blocks the swap
/// whether or not a stone is present. With attempts left, basic replace may use a free
/// attempt or a luck stone; lucid always needs its stone. A successful swap always
/// decrements <c>EvolveChance</c>; a stone pay also consumes the stone.
/// </remarks>
public enum ItemEvolvingReRollPayment : byte
{
    /// <summary>Cast has nothing to spend — refuse.</summary>
    None = 0,

    /// <summary>No stone — success still spends one <c>EvolveChance</c>.</summary>
    ChangeAttempt = 1,

    /// <summary>Spend the luck / lucid stone; success also spends one <c>EvolveChance</c>.</summary>
    LuckStone = 2
}

/// <summary>
/// How the synthesis effect-swap cast names the line to replace and, for the selectable stone,
/// the replacement group.
/// </summary>
/// <remarks>
/// Live lucid casts send flag 9 with exactly two ints: slot index then group id. Looking one
/// field further left the chosen group as zero and the swap fell back to a random roll.
/// </remarks>
public static class ItemEvolvingReRollRules
{
    /// <summary>Radio index of the line being replaced (zero-based).</summary>
    public const int SlotIndexValue = 0;

    /// <summary>Named replacement group id on the lucid / selectable stone.</summary>
    public const int ChosenGroupValue = 1;

    /// <summary>
    /// Picks how the cast pays. Zero remaining changes refuse the swap even with a stone.
    /// With attempts left, a stone marks a stone-paid cast; otherwise it is a free attempt.
    /// Lucid swaps still demand a stone. The caller always decrements <c>EvolveChance</c>
    /// on success.
    /// </summary>
    public static ItemEvolvingReRollPayment ResolvePayment(bool hasStone, ushort evolveChance,
        bool requiresStone)
    {
        if (evolveChance == 0)
            return ItemEvolvingReRollPayment.None;

        if (hasStone)
            return ItemEvolvingReRollPayment.LuckStone;

        if (requiresStone)
            return ItemEvolvingReRollPayment.None;

        return ItemEvolvingReRollPayment.ChangeAttempt;
    }

    /// <summary>
    /// Reads the slot the player radioed. False when the cast carried no usable index.
    /// </summary>
    public static bool TryReadSlotIndex(IReadOnlyList<int> values, int readCount, int usedLineCount,
        out int index)
    {
        index = -1;
        if (values == null || usedLineCount <= 0 || readCount <= SlotIndexValue ||
            SlotIndexValue >= values.Count)
            return false;

        var chosen = values[SlotIndexValue];
        if (chosen < 0 || chosen >= usedLineCount)
            return false;

        index = chosen;
        return true;
    }

    /// <summary>
    /// Reads the replacement group the lucid stone named. False when absent or non-positive.
    /// </summary>
    public static bool TryReadChosenGroupId(IReadOnlyList<int> values, int readCount, out uint groupId)
    {
        groupId = 0;
        if (values == null || readCount <= ChosenGroupValue || ChosenGroupValue >= values.Count)
            return false;

        var chosen = values[ChosenGroupValue];
        if (chosen <= 0)
            return false;

        groupId = (uint)chosen;
        return true;
    }
}
