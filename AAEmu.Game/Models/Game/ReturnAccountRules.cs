using AAEmu.Game.GameData;
using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// Account-return ("welcome back") reward rules. Every gameplay number - how many days the account
/// must have been away, how many days away before the reward ages out, and which item template the reward
/// carries - comes from <c>content_configs</c> by catalog name. This class holds only the key strings
/// and the day arithmetic; a missing row fails loudly through
/// <see cref="ContentConfigGameData.RequireInt"/>.
/// </summary>
/// <remarks>
/// Semantics pinned by the content key names and the client's
/// <c>return_account_reward_wnd</c> / <c>IsReturnAccount</c> surface: rest day is the minimum absence,
/// reward block day the maximum absence after which the reward is blocked. Shipped content carries 0
/// for all three rows, so the shipped world never offers a return reward.
/// </remarks>
public static class ReturnAccountRules
{
    /// <summary>Days of absence required before a return claim is eligible.</summary>
    public const string RestDayKey = "return_account_rest_day";

    /// <summary>The item template id the reward grants.</summary>
    public const string RewardItemTypeKey = "return_account_reward_item_type";

    /// <summary>Days of absence after which the reward is blocked.</summary>
    public const string RewardBlockDayKey = "return_account_reward_block_day";

    /// <summary>Named sentinel: the shipped content ships 0, meaning "no reward configured".</summary>
    public const int NoRewardItemType = 0;

    /// <summary>Named sentinel: 0 disables the block-day upper bound.</summary>
    public const int NoBlockDay = 0;

    /// <summary>Days of absence the content requires. Missing row throws.</summary>
    public static int RestDays => ContentConfigGameData.Instance.RequireInt(RestDayKey);

    /// <summary>Content's reward item template id. Missing row throws.</summary>
    public static int RewardItemType => ContentConfigGameData.Instance.RequireInt(RewardItemTypeKey);

    /// <summary>Days of absence after which the reward is blocked. Missing row throws.</summary>
    public static int RewardBlockDays => ContentConfigGameData.Instance.RequireInt(RewardBlockDayKey);

    /// <summary>Whether the content defines a reward at all.</summary>
    public static bool HasReward => RewardItemType != NoRewardItemType;

    /// <summary>Calendar days between the last sighting and <paramref name="nowUtc"/>, both
    /// normalized through <see cref="ServerCalendar.AsUtc"/>.</summary>
    public static int DaysSinceSighting(DateTime lastSeenUtc, DateTime nowUtc) =>
        (ServerCalendar.AsUtc(nowUtc).Date - ServerCalendar.AsUtc(lastSeenUtc).Date).Days;

    /// <summary>The content rest days have passed.</summary>
    public static bool RestDaysMet(DateTime lastSeenUtc, DateTime nowUtc) =>
        DaysSinceSighting(lastSeenUtc, nowUtc) >= RestDays;

    /// <summary>The absence passed the content block days, so the reward aged out.
    /// <see cref="NoBlockDay"/> means it never ages out.</summary>
    public static bool RewardBlocked(DateTime lastSeenUtc, DateTime nowUtc)
    {
        var blockDays = RewardBlockDays;
        if (blockDays == NoBlockDay)
            return false;
        return DaysSinceSighting(lastSeenUtc, nowUtc) > blockDays;
    }

    /// <summary>What <c>SCReturnAccountStatus</c> reports and what the claim enforces: a reward
    /// exists, the rest days are met and the block window has not passed.</summary>
    public static bool IsEligible(DateTime lastSeenUtc, DateTime nowUtc) =>
        HasReward && RestDaysMet(lastSeenUtc, nowUtc) && !RewardBlocked(lastSeenUtc, nowUtc);
}
