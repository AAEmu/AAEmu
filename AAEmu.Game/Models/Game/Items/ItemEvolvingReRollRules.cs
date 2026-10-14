namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How a synthesis effect-swap pays for the cast.
/// </summary>
/// <remarks>
/// A luck / lucid / serendipity stone pays on its own and never reads or spends
/// <c>EvolveChance</c>. Without a stone, basic replace needs a remaining change
/// attempt; lucid always needs its stone. Only a free-attempt success decrements
/// <c>EvolveChance</c>; a stone success consumes the stone only.
/// </remarks>
public enum ItemEvolvingReRollPayment : byte
{
    /// <summary>Cast has nothing to spend — refuse.</summary>
    None = 0,

    /// <summary>No stone — success spends one <c>EvolveChance</c>.</summary>
    ChangeAttempt = 1,

    /// <summary>Spend the luck / lucid stone; leave <c>EvolveChance</c> alone.</summary>
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
    /// Whether an item may pay as a luck / lucid / serendipity stone for this cast.
    /// </summary>
    /// <remarks>
    /// <c>CSStartSkillPacket</c> lets <c>BindOnPickup</c> items start a skill even when
    /// <c>UseSkillId</c> differs (fireplace portal recording). That must not turn an
    /// unrelated bound item into unlimited stone payment that bypasses <c>EvolveChance</c>.
    /// </remarks>
    public static bool IsStoneForSkill(uint itemUseSkillId, uint castSkillId) =>
        itemUseSkillId != 0 && castSkillId != 0 && itemUseSkillId == castSkillId;

    /// <summary>
    /// Picks how the cast pays. A stone always wins and ignores remaining changes.
    /// Without one, lucid is refused and basic replace needs <c>EvolveChance</c> &gt; 0.
    /// </summary>
    public static ItemEvolvingReRollPayment ResolvePayment(bool hasStone, ushort evolveChance,
        bool requiresStone)
    {
        if (hasStone)
            return ItemEvolvingReRollPayment.LuckStone;

        if (requiresStone || evolveChance == 0)
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
