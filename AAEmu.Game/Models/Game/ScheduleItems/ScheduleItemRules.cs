using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.ScheduleItems;

/// <summary>
/// HUD timer buttons on the bottom bar. Which rows exist, how often they pay, and the
/// mail text all come from <c>schedule_items</c>. This type only does calendar math and
/// the update-packet layout.
/// </summary>
public static class ScheduleItemRules
{
    /// <summary>Client reads a leading <c>u8</c> count, so the list cannot exceed 255; it also
    /// stops at 32 rows when building the HUD list.</summary>
    public const int MaxItemsInPacket = 32;

    public const int RowBytes = 21;

    public static int BodyBytes(int count) => 1 + Math.Clamp(count, 0, MaxItemsInPacket) * RowBytes;

    public static bool IsOnAir(DateTime utcNow, DateTime? start, DateTime? end)
    {
        var now = ServerCalendar.AsUtc(utcNow);
        if (start.HasValue)
        {
            if (end.HasValue)
                return start.Value < now && end.Value > now;
            return start.Value < now;
        }

        return !end.HasValue || end.Value > now;
    }

    public static DateTime? MakeLocalStamp(int year, int month, int day, int hour, int minute)
    {
        if (year <= 0 || month <= 0 || day <= 0)
            return null;
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    public static bool NeedsDailyReset(DateTime lastUpdatedUtc, DateTime utcNow) =>
        ServerCalendar.IsNewDailyPeriod(lastUpdatedUtc, utcNow);

    public static int SecondsForTerm(int giveTermMinutes) =>
        (int)TimeSpan.FromMinutes(Math.Max(0, giveTermMinutes)).TotalSeconds;

    public static bool CanTake(byte gave, int giveMax, long cumulated, int giveTermMinutes) =>
        gave < giveMax && cumulated >= SecondsForTerm(giveTermMinutes);

    /// <summary>
    /// Silent on-air rows that pay themselves (no HUD take button). Only membership
    /// <see cref="ScheduleItemKind.AccountBuff"/> rows auto-grant; leftover event and
    /// old ArcheLife packs stay off this path.
    /// </summary>
    public static bool ShouldAutoGrant(int kind, bool activeTake, int giveTermMinutes, byte gave, int giveMax) =>
        !activeTake
        && (ScheduleItemKind)kind == ScheduleItemKind.AccountBuff
        && CanTake(gave, giveMax, 0, giveTermMinutes);

    public static long TickCumulated(long cumulated, int giveTermMinutes, int addSeconds)
    {
        var cap = SecondsForTerm(giveTermMinutes);
        if (cap <= 0)
            return 0;
        return Math.Min(cap, Math.Max(0, cumulated + addSeconds));
    }

    /// <summary>Unset until the character is in-world this session.</summary>
    public static bool HasSessionTick(DateTime lastOnlineTick) => lastOnlineTick != default;

    /// <summary>
    /// Playtime between two in-world samples. A cleared session tick (logout) adds nothing,
    /// so a same-day reconnect cannot count the offline gap.
    /// </summary>
    public static int SessionAddSeconds(DateTime lastOnlineTick, DateTime now)
    {
        if (!HasSessionTick(lastOnlineTick))
            return 0;
        return (int)Math.Max(0, (ServerCalendar.AsUtc(now) - ServerCalendar.AsUtc(lastOnlineTick)).TotalSeconds);
    }
}
