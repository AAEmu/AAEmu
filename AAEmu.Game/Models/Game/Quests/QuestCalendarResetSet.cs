using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Calendar roll types for completed_quests bits (World UTC boundaries).
/// Daily / weekly membership matches the client quest-detail classifier used by IsDailyQuest /
/// IsWeeklyQuest (detail_id on the quest context).
/// </summary>
public static class QuestCalendarResetSet
{
    /// <summary>
    /// Cleared each day at 00:00 UTC: daily, daily_hunt, daily_livelihood, daily_group, today.
    /// (Not livelihood=8 or group=9; those are different product types.)
    /// </summary>
    public static readonly QuestDetail[] Daily =
    [
        QuestDetail.Daily,
        QuestDetail.DailyHunt,
        QuestDetail.DailyLivelihood,
        QuestDetail.DailyGroup,
        QuestDetail.Today
    ];

    /// <summary>Cleared at Monday 00:00 UTC: weekly only.</summary>
    public static readonly QuestDetail[] Weekly =
    [
        QuestDetail.Weekly
    ];
}
