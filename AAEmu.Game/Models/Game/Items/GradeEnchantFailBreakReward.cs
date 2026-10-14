namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One payout row from <c>item_grade_enchant_fail_break_rewards</c>.
/// </summary>
public class GradeEnchantFailBreakReward
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }

    /// <summary>
    /// Chance the break pays anything, in the same per-10000 space as regrade ratios.
    /// </summary>
    public int Ratio { get; set; }
}

/// <summary>
/// A gear band from <c>item_grade_enchant_fail_break_reward_categories</c> that points at a
/// <see cref="GradeEnchantFailBreakReward"/> and scales its count by the broken item's grade.
/// </summary>
public class GradeEnchantFailBreakRewardCategory
{
    public uint Id { get; set; }
    public int ItemCategoryId { get; set; }
    public uint RewardId { get; set; }
    public uint WearableSlotId { get; set; }
    public int MinLv { get; set; }
    public int MaxLv { get; set; }
    public int ArtifactMultiple { get; set; }
    public int WonderMultiple { get; set; }
    public int EpicMultiple { get; set; }
    public int LegendaryMultiple { get; set; }
    public int MythicMultiple { get; set; }
}
