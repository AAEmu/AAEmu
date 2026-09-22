using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>Where a scheduled conflict zone sits at a point in time: the state it is in, when that
/// state started, and when the schedule next changes state.</summary>
public readonly record struct ConflictZoneSchedulePosition(ZoneConflictType State, DateTime StateStart, DateTime NextChange);

public readonly record struct ConflictZoneDailyWarStart(int Hour, int Minute);

/// <summary>
/// Calendar math for conflict zones. <c>conflict_zone_realtime_schedules</c> is a weekly wall-clock
/// table (day-of-week + HHMM), so the current state is always "the last entry at or before now",
/// wrapping into the previous week when now precedes that week's first entry. A zone with no
/// schedule keeps the kill-driven cycle in <see cref="ZoneConflict"/>, whose thresholds and state
/// order live in <see cref="ConflictZoneEscalationRules"/>.
/// </summary>
public static class ConflictZoneScheduleRules
{
    public static TimeSpan? DecodeDailyWarStart(ConflictZoneDailyWarStart start)
    {
        if (start.Hour is < 0 or > 23 || start.Minute is < 0 or > 59)
            return null;

        return new TimeSpan(start.Hour, start.Minute, 0);
    }

    /// <summary>
    /// Resolves the legacy <c>war_st_hour/min_N</c> columns as daily local-time War starts. A row is
    /// usable only when every interval between starts equals its authored
    /// <c>war_min + peace_min + conflict_min</c> cycle. The three shipped rows satisfy that invariant
    /// exactly (24, 6, and 4 hours), so War and Peace boundaries leave the remaining interval in
    /// Conflict without inventing a fallback schedule.
    /// </summary>
    public static ConflictZoneSchedulePosition? ResolveDailyWarWindow(
        IReadOnlyList<ConflictZoneDailyWarStart> starts,
        int conflictMinutes,
        int warMinutes,
        int peaceMinutes,
        DateTime nowLocal)
    {
        if (starts == null || starts.Count == 0 || conflictMinutes < 0 || warMinutes < 0 || peaceMinutes < 0)
            return null;

        var times = starts
            .Select(DecodeDailyWarStart)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        if (times.Length == 0)
            return null;

        var cycleMinutes = conflictMinutes + warMinutes + peaceMinutes;
        for (var i = 0; i < times.Length; i++)
        {
            var nextStart = i + 1 < times.Length ? times[i + 1] : times[0].Add(TimeSpan.FromDays(1));
            if ((nextStart - times[i]).TotalMinutes != cycleMinutes)
                return null;
        }

        var boundaries = new List<(DateTime When, ZoneConflictType State)>();
        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var date = nowLocal.Date.AddDays(dayOffset);
            foreach (var time in times)
            {
                var warStart = date.Add(time);
                boundaries.Add((warStart, ZoneConflictType.War));
                var afterWar = warStart.AddMinutes(warMinutes);
                if (peaceMinutes > 0)
                {
                    boundaries.Add((afterWar, ZoneConflictType.Peace));
                    boundaries.Add((afterWar.AddMinutes(peaceMinutes), ZoneConflictType.Conflict));
                }
                else
                {
                    boundaries.Add((afterWar, ZoneConflictType.Conflict));
                }
            }
        }

        if (boundaries.Count == 0)
            return null;

        boundaries.Sort((a, b) => a.When != b.When ? a.When.CompareTo(b.When) : a.State.CompareTo(b.State));
        (DateTime When, ZoneConflictType State)? current = null;
        DateTime? next = null;
        foreach (var boundary in boundaries)
        {
            if (boundary.When <= nowLocal)
                current = boundary;
            else
            {
                next = boundary.When;
                break;
            }
        }

        return current is { } active && next is { } nextChange
            ? new ConflictZoneSchedulePosition(active.State, active.When, nextChange)
            : null;
    }

    /// <summary>
    /// <c>enum_day_of_weeks</c>: 1 = sunday … 7 = saturday. 8 (invalid) and anything else are null.
    /// </summary>
    public static DayOfWeek? DecodeDayOfWeek(int dayOfWeekId) =>
        dayOfWeekId is >= 1 and <= 7 ? (DayOfWeek)(dayOfWeekId - 1) : null;

    /// <summary>
    /// <c>time_of_day</c> is military HHMM (2320 = 23:20, 20 = 00:20). Values whose minute field is
    /// not 0–59 or whose hour is not 0–23 are invalid.
    /// </summary>
    public static TimeSpan? DecodeTimeOfDay(int hhmm)
    {
        if (hhmm < 0)
            return null;

        var hours = hhmm / 100;
        var minutes = hhmm % 100;
        if (hours > 23 || minutes > 59)
            return null;

        return new TimeSpan(hours, minutes, 0);
    }

    /// <summary>
    /// The state <paramref name="nowLocal"/> falls in, or null when the schedule has no usable
    /// entries. Occurrences from the previous and next week are included so a weekly table is
    /// continuous across the week boundary.
    /// </summary>
    public static ConflictZoneSchedulePosition? Resolve(IReadOnlyList<ConflictZoneScheduleEntry> schedule, DateTime nowLocal)
    {
        if (schedule == null || schedule.Count == 0)
            return null;

        var weekStart = nowLocal.Date.AddDays(-(int)nowLocal.DayOfWeek); // sunday 00:00 local
        var occurrences = new List<(DateTime When, ZoneConflictType State)>(schedule.Count * 3);
        for (var weekOffset = -1; weekOffset <= 1; weekOffset++)
        {
            var baseDate = weekStart.AddDays(weekOffset * 7);
            foreach (var entry in schedule)
            {
                if (DecodeDayOfWeek(entry.DayOfWeekId) is not { } day ||
                    DecodeTimeOfDay(entry.TimeOfDayHhmm) is not { } time)
                    continue;
                occurrences.Add((baseDate.AddDays((int)day).Add(time), entry.WarState));
            }
        }

        if (occurrences.Count == 0)
            return null;

        occurrences.Sort((a, b) => a.When != b.When ? a.When.CompareTo(b.When) : a.State.CompareTo(b.State));

        DateTime? stateStart = null;
        DateTime? nextChange = null;
        var state = ZoneConflictType.Tension;
        foreach (var (when, entryState) in occurrences)
        {
            if (when <= nowLocal)
            {
                stateStart = when;
                state = entryState;
                continue;
            }

            nextChange = when;
            break;
        }

        // The -1/+1 week padding guarantees both brackets unless every entry is invalid.
        if (stateStart is not { } start || nextChange is not { } next)
            return null;

        return new ConflictZoneSchedulePosition(state, start, next);
    }
}
