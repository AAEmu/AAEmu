using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>Where a scheduled conflict zone sits at a point in time: the state it is in, when that
/// state started, and when the schedule next changes state.</summary>
public readonly record struct ConflictZoneSchedulePosition(ZoneConflictType State, DateTime StateStart, DateTime NextChange);

/// <summary>
/// Calendar math for conflict zones. <c>conflict_zone_realtime_schedules</c> is a weekly wall-clock
/// table (day-of-week + HHMM), so the current state is always "the last entry at or before now",
/// wrapping into the previous week when now precedes that week's first entry. A zone with no
/// schedule keeps the kill-driven cycle in <see cref="ZoneConflict"/>, whose thresholds and state
/// order live in <see cref="ConflictZoneEscalationRules"/>.
/// </summary>
public static class ConflictZoneScheduleRules
{
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
