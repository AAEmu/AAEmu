using AAEmu.Game.Models.Game.Schedules;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.Models.Game.Weather;

/// <summary>
/// Decides whether a <c>game_schedules</c> row is open at a given UTC moment. The row owns every
/// timing: the daily window (<c>start_time</c>/<c>end_time</c> plus minutes), the weekday filter,
/// and the optional calendar bounds (<c>st_*</c>/<c>ed_*</c>). No timing constant lives here.
/// </summary>
/// <remarks>
/// The calendar and weekday rules mirror the schedule evaluator the rest of the server uses for
/// content periods. The daily window is additionally applied on every day of the period so a
/// content row can open a weather phase at a time of day, and a window whose start is later than
/// its end (for example a late-evening window that runs past midnight) is treated as crossing the
/// UTC day boundary. Moments with <see cref="DateTimeKind.Unspecified"/> are read as UTC rather
/// than local time, matching how persisted timestamps are normalized elsewhere.
/// </remarks>
public static class WeatherScheduleWindow
{
    public static bool Evaluate(GameSchedules schedule, DateTime moment)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var now = ServerCalendar.AsUtc(moment);
        var date = now.Date;
        var timeOfDay = now.TimeOfDay;

        var windowStart = new TimeSpan(schedule.StartTime, schedule.StartTimeMin, 0);
        var windowEnd = new TimeSpan(schedule.EndTime, schedule.EndTimeMin, 0);
        var crossesMidnight = windowStart > windowEnd;
        var allDay = windowStart == windowEnd;

        var dailyOpen = allDay
            || (crossesMidnight
                ? timeOfDay >= windowStart || timeOfDay < windowEnd
                : timeOfDay >= windowStart && timeOfDay <= windowEnd);

        var startDate = schedule is { StYear: > 0, StMonth: > 0, StDay: > 0 }
            ? new DateTime(schedule.StYear, schedule.StMonth, schedule.StDay)
            : DateTime.MinValue;
        var endDate = schedule is { EdYear: > 0, EdMonth: > 0, EdDay: > 0 }
            ? new DateTime(schedule.EdYear, schedule.EdMonth, schedule.EdDay)
            : DateTime.MaxValue;

        var afterStart = startDate == DateTime.MinValue
            || date > startDate
            || (date == startDate && timeOfDay >= windowStart);

        var beforeEnd = endDate == DateTime.MaxValue
            || date < endDate
            || (date == endDate && (crossesMidnight
                ? timeOfDay < windowEnd || timeOfDay >= windowStart
                : timeOfDay <= windowEnd));

        var weekdayOpen = schedule.DayOfWeekId == DayOfWeek.Invalid
            || CurrentDayOfWeek(now) == schedule.DayOfWeekId;

        return afterStart && beforeEnd && weekdayOpen && dailyOpen;
    }

    private static DayOfWeek CurrentDayOfWeek(DateTime utcMoment) =>
        (DayOfWeek)((int)utcMoment.DayOfWeek + 1);
}
