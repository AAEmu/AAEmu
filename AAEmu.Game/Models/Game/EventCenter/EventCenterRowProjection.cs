using AAEmu.Game.Models.Game.Schedules;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.Models.Game.EventCenter;

/// <summary>
/// Why a projected event row is not yet a board entry. Every bit is a statement about evidence, not a
/// runtime state: a row that carries one of these gaps is reported by the diagnostics surface and is
/// refused by anything that would put it on the wire.
/// </summary>
/// <remarks>
/// The board entry itself is one <c>s32</c> main order plus three sub-structs whose fields are the
/// window, the text trio and the reward block. A content schedule row owns the window and nothing else:
/// no table carries a board title, body, web link or reward, and the row's own display name has no
/// localized row, so those four sub-structs are recorded as sources that are missing rather than filled
/// from a nearby column. The main order is a server-owned position and no content column ranks the rows,
/// so it is missing for every row too.
/// </remarks>
[Flags]
public enum EventCenterRowGap
{
    /// <summary>Nothing is missing.</summary>
    None = 0,

    /// <summary>At least one calendar component of a period bound is absent, so the bound is not a date.</summary>
    IncompleteCalendarPeriod = 1 << 0,

    /// <summary>A calendar or clock component is outside the range a date can hold.</summary>
    MalformedCalendarBound = 1 << 1,

    /// <summary>The row's time-of-day window is outside the range a clock can hold.</summary>
    MalformedDailyWindow = 1 << 2,

    /// <summary>The period ends at or before it starts, so it has no positive length.</summary>
    EndNotAfterStart = 1 << 3,

    /// <summary>
    /// The row repeats on a time-of-day window inside its period, so a single start/end instant pair
    /// describes only the envelope, not the occurrences.
    /// </summary>
    RecurringWithinPeriod = 1 << 4,

    /// <summary>
    /// The row is limited to one weekday inside its period, so its occurrences are weekly rather than
    /// one continuous run.
    /// </summary>
    RecurringByWeekday = 1 << 5,

    /// <summary>The row's weekday column names a day outside the catalogued week.</summary>
    MalformedWeekdayFilter = 1 << 6,

    /// <summary>No content column ranks board rows, so the main order has no source.</summary>
    MissingMainOrderSource = 1 << 7,

    /// <summary>No content column supplies the entry's title.</summary>
    MissingTitleSource = 1 << 8,

    /// <summary>No content column supplies the entry's body text.</summary>
    MissingBodySource = 1 << 9,

    /// <summary>No content column supplies the entry's web link.</summary>
    MissingLinkSource = 1 << 10,

    /// <summary>No content column supplies the entry's reward block.</summary>
    MissingRewardSource = 1 << 11
}

/// <summary>
/// One content schedule row projected onto the fields an event-board row would need. This is a
/// description, not a packet: nothing here is written to a client, and the text and reward fields are
/// absent by construction because no content column carries them.
/// </summary>
public sealed record EventCenterRowProjection
{
    /// <summary>The <c>game_schedules</c> row id this projection came from.</summary>
    public int ScheduleId { get; init; }

    /// <summary>
    /// The row's own display name, exactly as content authors it. Diagnostics only: it is not localized
    /// and nothing proves the retail board used it as the entry title.
    /// </summary>
    public string ContentName { get; init; } = string.Empty;

    /// <summary>The weekday the row is limited to, or null when the row runs on any day.</summary>
    public DayOfWeek? WeekdayFilter { get; init; }

    /// <summary>True when the row has no time-of-day window and so runs for the whole of each day.</summary>
    public bool IsAllDay { get; init; }

    /// <summary>
    /// The row's period start in UTC, or null when the period is not a complete date pair. The value is
    /// the row's own bound, read as UTC because the columns carry no offset.
    /// </summary>
    public DateTimeOffset? PeriodStart { get; init; }

    /// <summary>
    /// The row's period end in UTC. It is the bound exactly as the row states it, except that a bound
    /// written as <c>24:00</c> means the end of that day and so resolves to the following midnight — the
    /// first moment outside the period rather than its last.
    /// </summary>
    public DateTimeOffset? PeriodEnd { get; init; }

    /// <summary>
    /// Spawner template ids bound to this schedule through the spawner link table. Diagnostics only: it
    /// shows which schedules actually drive world content and is not a board field.
    /// </summary>
    public IReadOnlyList<uint> BoundSpawnerTemplateIds { get; init; } = [];

    /// <summary>Every gap this row carries.</summary>
    public EventCenterRowGap Gaps { get; init; } = EventCenterRowGap.None;

    /// <summary>True when both period instants resolved and describe a positive-length period.</summary>
    public bool IsPeriodResolved => PeriodStart.HasValue && PeriodEnd.HasValue;

    /// <summary>True only when the row could be written to the client as it stands.</summary>
    public bool IsWireReady => Gaps == EventCenterRowGap.None;

    /// <summary>The subset of <see cref="Gaps"/> that describes the row's own schedule shape.</summary>
    public EventCenterRowGap ShapeGaps =>
        Gaps & (EventCenterRowGap.IncompleteCalendarPeriod
                | EventCenterRowGap.MalformedCalendarBound
                | EventCenterRowGap.MalformedDailyWindow
                | EventCenterRowGap.EndNotAfterStart
                | EventCenterRowGap.RecurringWithinPeriod
                | EventCenterRowGap.RecurringByWeekday
                | EventCenterRowGap.MalformedWeekdayFilter);

    /// <summary>The subset of <see cref="Gaps"/> that names content columns this slice could not find.</summary>
    public EventCenterRowGap ContentSourceGaps =>
        Gaps & (EventCenterRowGap.MissingMainOrderSource
                | EventCenterRowGap.MissingTitleSource
                | EventCenterRowGap.MissingBodySource
                | EventCenterRowGap.MissingLinkSource
                | EventCenterRowGap.MissingRewardSource);
}
