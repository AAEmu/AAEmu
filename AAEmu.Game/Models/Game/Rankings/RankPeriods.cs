namespace AAEmu.Game.Models.Game.Rankings;

/// <summary>A board's scoring window: when it opened and when the next one replaces it.</summary>
public readonly record struct RankPeriod(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// The windows a board's scores are counted in. A board names its cycle in <c>rank_resets</c>: how often
/// it starts over, and the day it does so.
/// </summary>
/// <remarks>
/// The shipped day values behave as the weekday index the client itself uses (0 sunday … 6 saturday), so a
/// board naming 7 starts its window on the sunday — which is the window the ranking window displays for
/// those boards (2026-09-13 → 2026-09-20 for the day-7 boards on 2026-09-17). A monthly board names no day
/// (8) and starts on the first of the month, again as the window shows it (2026-09-01 → 2026-10-01).
/// </remarks>
public static class RankPeriods
{
    /// <summary>The cycle value a weekly board carries (<c>enum_reset_interval_kinds</c>).</summary>
    public const int Weekly = 1;

    /// <summary>The cycle value a monthly board carries.</summary>
    public const int Monthly = 2;

    /// <summary>The day value a board carries when its cycle names no day.</summary>
    public const int NoDay = 8;

    /// <summary>The window a board is in at the given moment.</summary>
    public static RankPeriod For(DateTime momentUtc, int resetIntervalId, int dayOfWeekId)
    {
        var moment = momentUtc.Kind == DateTimeKind.Local ? momentUtc.ToUniversalTime() : momentUtc;
        return resetIntervalId == Weekly ? Week(moment, dayOfWeekId) : Month(moment);
    }

    private static RankPeriod Month(DateTime moment)
    {
        var start = new DateTime(moment.Year, moment.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return new RankPeriod(start, start.AddMonths(1));
    }

    private static RankPeriod Week(DateTime moment, int dayOfWeekId)
    {
        var start = moment.Date;
        var wanted = (DayOfWeek)(((dayOfWeekId % 7) + 7) % 7);
        while (start.DayOfWeek != wanted)
            start = start.AddDays(-1);

        return new RankPeriod(start, start.AddDays(7));
    }
}
