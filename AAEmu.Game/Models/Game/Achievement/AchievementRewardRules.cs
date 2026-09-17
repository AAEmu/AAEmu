namespace AAEmu.Game.Models.Game.Achievement;

/// <summary>
/// What an achievement pays when it is earned: an item (2,535 of them carry one) and/or a title
/// (214 carry one). Either half may be absent.
/// </summary>
/// <param name="ItemId">The reward item, or 0.</param>
/// <param name="ItemCount">How many of it. Content carries 1..50; never negative.</param>
/// <param name="AppellationId">The title, or 0.</param>
public readonly record struct AchievementReward(uint ItemId, int ItemCount, uint AppellationId)
{
    public bool HasItem => ItemId != 0 && ItemCount > 0;

    public bool HasAppellation => AppellationId != 0;
}

/// <summary>
/// What an achievement is worth, and where its item goes.
/// </summary>
/// <remarks>
/// The window draws both halves from its own copy of the content, so this only has to hand them over. The item
/// goes to the bag when the whole stack fits and by mail when it does not, which is what the client's
/// "the reward was sent" packet distinguishes (<c>byMail</c>) and what every other reward path in the tree
/// does; an item that goes to the bag is announced by the achievement's own packet, so its item task type is
/// left at the neutral one rather than borrowed from another system.
/// </remarks>
public static class AchievementRewardRules
{
    /// <summary>The reward the content states for an achievement.</summary>
    public static AchievementReward RewardOf(Achievements achievement) => achievement == null
        ? default
        : new AchievementReward(
            achievement.ItemId,
            (int)Math.Clamp(achievement.ItemNum, 0u, int.MaxValue),
            achievement.AppellationId);

    /// <summary>Whether that many of an item have to be mailed rather than put in the bag.</summary>
    public static bool GoesToMail(int freeSpaceForItem, int itemCount) => freeSpaceForItem < itemCount;
}
