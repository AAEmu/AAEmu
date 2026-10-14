namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// What a destroyed regrade pays back, when it pays anything.
/// </summary>
/// <param name="ItemId">Compensation template.</param>
/// <param name="Count">How many of it.</param>
public readonly record struct GradeEnchantBreakRewardPayout(uint ItemId, int Count)
{
    public bool HasItem => ItemId != 0 && Count > 0;
}

/// <summary>
/// Pure decisions for fail-break compensation: which category row fits the gear, which grade
/// multiple applies, and how many of the reward item a break rolls.
/// </summary>
/// <remarks>
/// Matching is <c>item_category_id</c> + level band + wearable slot (slot 0 = any). The five
/// <c>item_grade_*_multiple</c> columns are the Artifact..Mythic ladder (grade ids 7..11); anything
/// below Artifact has no multiple and pays nothing. Chance uses the reward's <c>ratio</c> per 10000;
/// count is <c>random(min..max) × multiple / 100</c>, and a zero result is a no-pay.
/// </remarks>
public static class GradeEnchantBreakRewardRules
{
    /// <summary>Lowest grade id that has a multiple column (Artifact).</summary>
    public const byte FirstCompensatedGradeId = 7;

    /// <summary>
    /// The locale-mail key under <c>locale.mail</c>. Title is <c>title(brokenItemType)</c>; body is
    /// <c>body(brokenItemType, rewardItemType, rewardGrade, count)</c>.
    /// </summary>
    public const string OverflowMailSender = ".itemGradeEnchantFailBreakReward";

    public static string OverflowMailTitle(uint brokenItemType) => $"title({brokenItemType})";

    public static string OverflowMailBody(uint brokenItemType, uint rewardItemType, int rewardGrade, int count) =>
        $"body({brokenItemType}, {rewardItemType}, {rewardGrade}, {count})";

    /// <summary>Whether a category row covers this piece of gear.</summary>
    public static bool MatchesCategory(
        GradeEnchantFailBreakRewardCategory category,
        int itemCategoryId,
        int itemLevel,
        uint wearableSlotId)
    {
        if (category == null)
            return false;

        if (category.ItemCategoryId != itemCategoryId)
            return false;

        if (itemLevel < category.MinLv || itemLevel > category.MaxLv)
            return false;

        // Slot 0 is the weapon / unslotted band; a non-zero slot must match exactly.
        if (category.WearableSlotId != 0 && category.WearableSlotId != wearableSlotId)
            return false;

        return true;
    }

    /// <summary>
    /// The grade multiple for a broken item, or 0 when that grade has no compensation column.
    /// </summary>
    public static int MultipleForGrade(GradeEnchantFailBreakRewardCategory category, byte gradeId)
    {
        if (category == null)
            return 0;

        return gradeId switch
        {
            7 => category.ArtifactMultiple,
            8 => category.WonderMultiple,
            9 => category.EpicMultiple,
            10 => category.LegendaryMultiple,
            >= 11 => category.MythicMultiple,
            _ => 0
        };
    }

    /// <summary>
    /// Rolls a payout, or null when the chance misses / the grade has no multiple / the count
    /// rounds to zero.
    /// </summary>
    /// <param name="nextBelow">
    /// Draws <c>[0, bound)</c> — same shape as <c>Random.Shared.Next(bound)</c>.
    /// </param>
    /// <param name="nextInclusive">
    /// Draws an inclusive <c>[min, max]</c> — same shape as
    /// <c>Random.Shared.Next(min, max + 1)</c>.
    /// </param>
    public static GradeEnchantBreakRewardPayout? Roll(
        GradeEnchantFailBreakReward reward,
        int gradeMultiple,
        Func<int, int> nextBelow,
        Func<int, int, int> nextInclusive)
    {
        if (reward == null || reward.ItemId == 0 || gradeMultiple <= 0 || reward.Ratio <= 0)
            return null;

        if (nextBelow(10000) >= reward.Ratio)
            return null;

        var min = Math.Min(reward.Min, reward.Max);
        var max = Math.Max(reward.Min, reward.Max);
        if (max < 0)
            return null;

        min = Math.Max(0, min);
        var baseCount = nextInclusive(min, max);
        var count = (int)((long)baseCount * gradeMultiple / 100);
        if (count <= 0)
            return null;

        return new GradeEnchantBreakRewardPayout(reward.ItemId, count);
    }

    /// <summary>Bag when the whole stack fits; mail otherwise — same rule as achievement rewards.</summary>
    public static bool GoesToMail(int freeSpaceForItem, int itemCount) => freeSpaceForItem < itemCount;
}
