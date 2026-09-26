using AAEmu.Game.Models.Game.Schedules;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.Models.Game.EventCenter;

/// <summary>
/// Turns one content schedule row into the event-board projection the row can actually support, and
/// records every reason the rest of a board entry has no source yet.
/// </summary>
/// <remarks>
/// The rules read only the row's own columns. A period resolves when both bounds name a real date with
/// the end after the start, which is what a single start/end instant pair on the wire can describe. A
/// row that also carries a time-of-day window or a weekday filter repeats inside its period, so its
/// instants would be the envelope of many occurrences rather than the occurrence itself, and the row is
/// reported instead. The text and reward gaps are unconditional: the row's display name is not
/// localized and no table carries a board title, body, link or reward, so those fields stay absent
/// rather than being filled from a neighbouring column.
/// </remarks>
public static class EventCenterRowProjector
{
    /// <summary>
    /// A clock is a real time of day. These are the clock's own structure, not content: a day has this
    /// many hours and an hour this many minutes, and this is the single place the feature states them.
    /// </summary>
    private const int HoursPerDay = 24;
    private const int MinutesPerHour = 60;

    /// <summary>Gaps every row carries until a content column for the missing board fields exists.</summary>
    public const EventCenterRowGap UnprovenBoardFields =
        EventCenterRowGap.MissingMainOrderSource
        | EventCenterRowGap.MissingTitleSource
        | EventCenterRowGap.MissingBodySource
        | EventCenterRowGap.MissingLinkSource
        | EventCenterRowGap.MissingRewardSource;

    /// <summary>
    /// Projects <paramref name="row"/>. <paramref name="boundSpawnerTemplateIds"/> is diagnostics only
    /// and may be null when no spawner link is being reported.
    /// </summary>
    public static EventCenterRowProjection Project(
        GameSchedules row,
        IReadOnlyList<uint> boundSpawnerTemplateIds = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        var gaps = EventCenterRowGap.None;
        var weekdayFilter = ResolveWeekday(row.DayOfWeekId, ref gaps);
        var isAllDay = IsAllDay(row, ref gaps);

        var start = ResolveBound(row.StYear, row.StMonth, row.StDay, row.StHour, row.StMin,
            allowEndOfDay: false, ref gaps);
        var end = ResolveBound(row.EdYear, row.EdMonth, row.EdDay, row.EdHour, row.EdMin,
            allowEndOfDay: true, ref gaps);

        if (start.HasValue && end.HasValue && end.Value <= start.Value)
        {
            gaps |= EventCenterRowGap.EndNotAfterStart;
        }

        if (isAllDay && weekdayFilter.HasValue)
        {
            gaps |= EventCenterRowGap.RecurringByWeekday;
        }

        if (!isAllDay)
        {
            gaps |= EventCenterRowGap.RecurringWithinPeriod;
        }

        if (start.HasValue && end.HasValue && end.Value <= start.Value)
        {
            // A period that does not move forward has no instants to report, only the reason.
            start = null;
            end = null;
        }

        return new EventCenterRowProjection
        {
            ScheduleId = row.Id,
            ContentName = row.Name ?? string.Empty,
            WeekdayFilter = weekdayFilter,
            IsAllDay = isAllDay,
            PeriodStart = start,
            PeriodEnd = end,
            BoundSpawnerTemplateIds = boundSpawnerTemplateIds ?? [],
            Gaps = gaps | UnprovenBoardFields
        };
    }

    private static DayOfWeek? ResolveWeekday(DayOfWeek dayOfWeek, ref EventCenterRowGap gaps)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            gaps |= EventCenterRowGap.MalformedWeekdayFilter;
            return null;
        }

        return dayOfWeek == DayOfWeek.Invalid ? null : dayOfWeek;
    }

    private static bool IsAllDay(GameSchedules row, ref EventCenterRowGap gaps)
    {
        if (!IsClock(row.StartTime, row.StartTimeMin) || !IsClock(row.EndTime, row.EndTimeMin))
        {
            gaps |= EventCenterRowGap.MalformedDailyWindow;
        }

        return row.StartTime == row.EndTime
               && row.StartTimeMin == 0
               && row.EndTimeMin == 0;
    }

    private static bool IsClock(int hour, int minute) =>
        hour >= 0 && hour < HoursPerDay && minute >= 0 && minute < MinutesPerHour;

    /// <summary>
    /// Reads one period bound as a UTC instant. A bound whose date is complete but whose clock is
    /// <c>24:00</c> is the end of that day and rolls into the next midnight; anything else that no date
    /// can hold is reported rather than clamped.
    /// </summary>
    private static DateTimeOffset? ResolveBound(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        bool allowEndOfDay,
        ref EventCenterRowGap gaps)
    {
        if (year <= 0 || month <= 0 || day <= 0)
        {
            gaps |= EventCenterRowGap.IncompleteCalendarPeriod;
            return null;
        }

        if (allowEndOfDay && IsEndOfDay(hour, minute))
        {
            if (!TryBuild(year, month, day, 0, 0, out var endOfDay))
            {
                gaps |= EventCenterRowGap.MalformedCalendarBound;
                return null;
            }

            return endOfDay.AddDays(1);
        }

        if (!TryBuild(year, month, day, hour, minute, out var bound))
        {
            gaps |= EventCenterRowGap.MalformedCalendarBound;
            return null;
        }

        return bound;
    }

    private static bool IsEndOfDay(int hour, int minute) => hour == HoursPerDay && minute == 0;

    private static bool TryBuild(int year, int month, int day, int hour, int minute, out DateTimeOffset value)
    {
        try
        {
            value = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }
}
