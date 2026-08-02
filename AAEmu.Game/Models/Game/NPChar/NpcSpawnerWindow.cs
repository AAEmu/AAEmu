using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Whether an <c>npc_spawners</c> placement is allowed to hold live NPCs at this instant.
/// </summary>
public enum NpcSpawnerWindowState
{
    /// <summary>
    /// The spawner carries neither a <c>game_schedule_spawners</c> row nor a day-night window,
    /// so nothing constrains it — the ordinary population of a zone.
    /// </summary>
    Unscheduled,

    /// <summary>Constrained, and inside its window right now.</summary>
    Open,

    /// <summary>Constrained, and outside its window right now.</summary>
    Closed
}

/// <summary>
/// Combines the two independent gates <c>npc_spawners</c> placements answer to:
/// the calendar period from <c>game_schedules</c> (live events, weekend dungeons, world bosses)
/// and the in-game day-night window carried on the spawner row itself (<c>startTime</c> /
/// <c>endTime</c>, e.g. nocturnal spawns).
/// </summary>
/// <remarks>
/// Kept free of manager state so the decision can be exercised directly in tests; callers supply
/// the period status, the spawner's window and the current in-game hour.
/// </remarks>
public static class NpcSpawnerWindow
{
    /// <summary>
    /// Resolves the combined state. Both gates must permit the spawner for it to be
    /// <see cref="NpcSpawnerWindowState.Open"/>; either one alone can close it.
    /// </summary>
    /// <param name="scheduleStatus">Period status for this spawner id from <see cref="GameScheduleManager"/>.</param>
    /// <param name="startTime">Spawner <c>startTime</c> in in-game hours; 0 with <paramref name="endTime"/> 0 means unset.</param>
    /// <param name="endTime">Spawner <c>endTime</c> in in-game hours.</param>
    /// <param name="gameTimeHours">Current in-game time of day, in hours.</param>
    public static NpcSpawnerWindowState Evaluate(
        GameScheduleManager.PeriodStatus scheduleStatus,
        float startTime,
        float endTime,
        float gameTimeHours)
    {
        var dayNight = EvaluateDayNightWindow(startTime, endTime, gameTimeHours);

        return scheduleStatus switch
        {
            // The spawner is owned by a game_schedule whose period is not running. 765 of the
            // 922 rows in game_schedules are dated live events that ended in 2013; without this
            // branch every one of their NPCs is permanently present.
            GameScheduleManager.PeriodStatus.NotStarted => NpcSpawnerWindowState.Closed,
            GameScheduleManager.PeriodStatus.Ended => NpcSpawnerWindowState.Closed,

            // Inside its calendar period, but a nocturnal spawner still waits for its hour.
            GameScheduleManager.PeriodStatus.InProgress => dayNight == NpcSpawnerWindowState.Closed
                ? NpcSpawnerWindowState.Closed
                : NpcSpawnerWindowState.Open,

            // No schedule row: the day-night window is the only constraint, and usually absent.
            _ => dayNight
        };
    }

    /// <summary>
    /// Evaluates the <c>npc_spawners.startTime</c> / <c>endTime</c> window against the in-game
    /// clock. 161 of the 22982 spawner rows carry one; the rest are unconstrained.
    /// </summary>
    public static NpcSpawnerWindowState EvaluateDayNightWindow(float startTime, float endTime, float gameTimeHours)
    {
        if (startTime <= 0.0f && endTime <= 0.0f)
            return NpcSpawnerWindowState.Unscheduled;

        var inside = NpcSpawner.IsTimeBetween(
            TimeSpan.FromHours(gameTimeHours),
            TimeSpan.FromHours(startTime),
            TimeSpan.FromHours(endTime));

        return inside ? NpcSpawnerWindowState.Open : NpcSpawnerWindowState.Closed;
    }
}
