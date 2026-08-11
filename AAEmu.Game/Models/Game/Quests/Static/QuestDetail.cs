namespace AAEmu.Game.Models.Game.Quests.Static;

/// <summary>
/// quest_contexts.detail_id (client name map: normal/main/saga/tutorial/hidden, then daily* family).
/// </summary>
public enum QuestDetail : uint
{
    Normal = 1,
    Main = 2,
    Saga = 3,
    Tutorial = 4,
    Hidden = 5,
    Task = 6,
    /// <summary>Standard dailies (Crimson/Halcyona, Grimghast, kill/clean loops, …).</summary>
    Daily = 7,
    /// <summary>Livelihood story/work; not calendar-daily in client IsDailyQuest.</summary>
    Livelihood = 8,
    /// <summary>Weekday-named commissions (Monday…Sunday); not weekly-bit 15.</summary>
    Group = 9,
    DailyHunt = 10,
    DailyLivelihood = 11,
    DailyGroup = 12,
    /// <summary>Path of Destiny / today_quest catalog contracts.</summary>
    Today = 13,
    Hero = 14,
    /// <summary>Weekly quests; cleared Monday 00:00 UTC with the weekly calendar rollover.</summary>
    Weekly = 15,
    Expedition = 16
}
