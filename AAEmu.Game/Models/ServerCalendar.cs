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

    /// <summary>
    /// Normalize a persisted timestamp to UTC without treating <see cref="DateTimeKind.Unspecified"/>
    /// as local (MySQL readers often materialize UTC columns as Unspecified).
    /// </summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    /// <summary>True when <paramref name="moment"/> is strictly before the current UTC calendar day.</summary>
    public static bool IsBeforeToday(DateTime moment) => AsUtc(moment).Date < TodayUtc;

    /// <summary>Monday 00:00 UTC of the week containing <paramref name="moment"/>.</summary>
    public static DateTime WeekStartMondayContaining(DateTime moment)
    {
        var day = AsUtc(moment).Date;
        var offset = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return day.AddDays(-offset);
    }
}
