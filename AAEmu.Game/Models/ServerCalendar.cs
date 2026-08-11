namespace AAEmu.Game.Models;

/// <summary>
/// Single wall-clock source for day/week boundaries on World.
/// Always UTC (GMT). Restarts do not invent a local midnight — next fire is absolute UTC.
/// </summary>
public static class ServerCalendar
{
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Calendar day used for daily quests, Path of Destiny day_key, merchant daily limits.</summary>
    public static DateTime TodayUtc => UtcNow.Date;

    /// <summary>
    /// Start of the Monday–Sunday week containing <see cref="TodayUtc"/> (Monday 00:00:00 UTC).
    /// Matches merchant weekly limits and cron <c>0 0 0 * * 1</c>.
    /// </summary>
    public static DateTime WeekStartMondayUtc
    {
        get
        {
            var day = TodayUtc;
            var offset = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return day.AddDays(-offset);
        }
    }

    /// <summary>True when <paramref name="utc"/> is at or after the first moment of the current UTC day.</summary>
    public static bool IsBeforeToday(DateTime utcMoment) => utcMoment.ToUniversalTime().Date < TodayUtc;
}
