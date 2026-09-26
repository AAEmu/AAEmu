using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Models.Game.Schedules;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.UnitTests.Game.Models.Game.EventCenter;

/// <summary>
/// Pins the event-board row projection against the shapes the shipped schedule content actually has.
/// Every fixture below is a real row shape read from the content table; no fixture invents a field the
/// table does not carry.
/// </summary>
public class EventCenterRowProjectionTests
{
    [Test]
    public async Task AllDayRowWithCompletePeriod_ProjectsItsOwnInstantsInUtc()
    {
        // A shipped all-day period: stated start 2013-06-14 14:00, stated end 2013-06-17 14:00, any weekday.
        var row = Schedule(id: 1, start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.IsPeriodResolved).IsTrue();
        await Assert.That(projection.PeriodStart?.ToUnixTimeSeconds()).IsEqualTo(1371218400L);
        await Assert.That(projection.PeriodEnd?.ToUnixTimeSeconds()).IsEqualTo(1371477600L);
        await Assert.That(projection.PeriodStart?.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(projection.IsAllDay).IsTrue();
        await Assert.That(projection.WeekdayFilter).IsNull();
        await Assert.That(projection.ShapeGaps).IsEqualTo(EventCenterRowGap.None);
    }

    [Test]
    public async Task EndBoundAtEndOfDay_RollsIntoTheNextMidnight()
    {
        // The one shipped row whose end bound reads 24:00: the end of that day, not an invalid hour.
        var row = Schedule(id: 652, start: Bound(2018, 10, 2, 0, 0), end: Bound(2018, 10, 26, 24, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.IsPeriodResolved).IsTrue();
        await Assert.That(projection.PeriodEnd?.ToUnixTimeSeconds()).IsEqualTo(1540598400L);
        await Assert.That(projection.ShapeGaps).IsEqualTo(EventCenterRowGap.None);
    }

    [Test]
    public async Task TimeOfDayWindow_ReportsRecurringOverItsResolvedPeriod()
    {
        // A shipped windowed period: runs 14:00-16:00 every day of 2013-06-12 .. 2013-07-30. The stated
        // period still resolves; the gap says the pair is the envelope of many daily occurrences.
        var row = Schedule(
            id: 2,
            start: Bound(2013, 6, 12, 0, 0),
            end: Bound(2013, 7, 30, 0, 0),
            startTime: 14,
            endTime: 16);

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.IsAllDay).IsFalse();
        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.RecurringWithinPeriod)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsTrue();
        await Assert.That(projection.IsWireReady).IsFalse();
    }

    [Test]
    public async Task WeekdayFilter_ReportsRecurringOverItsResolvedPeriod()
    {
        // A shipped all-day period limited to one weekday: 2013-06-01 .. 2013-06-30, Fridays only.
        var row = Schedule(
            id: 4,
            start: Bound(2013, 6, 1, 0, 0),
            end: Bound(2013, 6, 30, 0, 0),
            dayOfWeek: DayOfWeek.Friday);

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.WeekdayFilter).IsEqualTo(DayOfWeek.Friday);
        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.RecurringByWeekday)).IsTrue();
        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.RecurringWithinPeriod)).IsFalse();
        await Assert.That(projection.IsPeriodResolved).IsTrue();
    }

    [Test]
    public async Task RowWithoutCalendarPeriod_IsReportedIncomplete()
    {
        // The recurring daily rows carry no period at all (0/0/0 open, 0/1/1 close).
        var row = Schedule(
            id: 16,
            start: Bound(0, 0, 0, 0, 0),
            end: Bound(0, 1, 1, 0, 0),
            dayOfWeek: DayOfWeek.Tuesday);

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.IncompleteCalendarPeriod)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsFalse();
    }

    [Test]
    public async Task OutOfRangeCalendarPart_IsReportedMalformedRatherThanClamped()
    {
        var row = Schedule(id: 900, start: Bound(2013, 13, 1, 0, 0), end: Bound(2013, 12, 1, 0, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.MalformedCalendarBound)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsFalse();
    }

    [Test]
    public async Task EndOfDayHourOutsideTheShippedShape_IsReportedMalformed()
    {
        // 24:00 is a shipped end shape; 25:00 is not a clock, so it is reported, never rolled.
        var row = Schedule(id: 901, start: Bound(2013, 6, 1, 0, 0), end: Bound(2013, 6, 2, 25, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.MalformedCalendarBound)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsFalse();
    }

    [Test]
    public async Task PeriodEndingBeforeItStarts_ReportsTheReasonAndDropsTheInstants()
    {
        // A shipped row whose stated end precedes its stated start.
        var row = Schedule(id: 14, start: Bound(2013, 7, 22, 0, 0), end: Bound(2013, 1, 1, 0, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.EndNotAfterStart)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsFalse();
    }

    [Test]
    public async Task ZeroLengthPeriod_ReportsTheReasonAndDropsTheInstants()
    {
        var row = Schedule(id: 271, start: Bound(2099, 4, 14, 0, 0), end: Bound(2099, 4, 14, 0, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.EndNotAfterStart)).IsTrue();
        await Assert.That(projection.IsPeriodResolved).IsFalse();
    }

    [Test]
    public async Task WeekdayOutsideTheCataloguedWeek_IsReportedMalformed()
    {
        var row = Schedule(id: 902, start: Bound(2013, 6, 1, 0, 0), end: Bound(2013, 6, 2, 0, 0),
            dayOfWeek: (DayOfWeek)99);

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.MalformedWeekdayFilter)).IsTrue();
        await Assert.That(projection.WeekdayFilter).IsNull();
    }

    [Test]
    public async Task TimeOfDayWindowOutsideTheShippedRange_IsReportedMalformed()
    {
        var row = Schedule(id: 903, start: Bound(2013, 6, 1, 0, 0), end: Bound(2013, 6, 2, 0, 0),
            startTime: 24, endTime: 30);

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.MalformedDailyWindow)).IsTrue();
    }

    [Test]
    public async Task NoContentColumn_LeavesTheRowUnwritableWithNamedGaps()
    {
        var row = Schedule(id: 1, start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0));

        var projection = EventCenterRowProjector.Project(row);

        // The period is proven; the title, body, link, reward and the board order have no source at all,
        // so the row stays unwritable instead of borrowing the schedule's display name.
        await Assert.That(projection.IsPeriodResolved).IsTrue();
        await Assert.That(projection.IsWireReady).IsFalse();
        await Assert.That(projection.ContentSourceGaps).IsEqualTo(EventCenterRowProjector.UnprovenBoardFields);
        await Assert.That(EventCenterRowProjector.UnprovenBoardFields.HasFlag(EventCenterRowGap.MissingTitleSource)).IsTrue();
        await Assert.That(EventCenterRowProjector.UnprovenBoardFields.HasFlag(EventCenterRowGap.MissingBodySource)).IsTrue();
        await Assert.That(EventCenterRowProjector.UnprovenBoardFields.HasFlag(EventCenterRowGap.MissingLinkSource)).IsTrue();
        await Assert.That(EventCenterRowProjector.UnprovenBoardFields.HasFlag(EventCenterRowGap.MissingRewardSource)).IsTrue();
        await Assert.That(EventCenterRowProjector.UnprovenBoardFields.HasFlag(EventCenterRowGap.MissingMainOrderSource)).IsTrue();
    }

    [Test]
    public async Task ContentName_IsCarriedAsWrittenAndNeverBecomesTheTitle()
    {
        var row = Schedule(id: 1, name: "an authored row name",
            start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0));

        var projection = EventCenterRowProjector.Project(row);

        await Assert.That(projection.ContentName).IsEqualTo("an authored row name");
        await Assert.That(projection.Gaps.HasFlag(EventCenterRowGap.MissingTitleSource)).IsTrue();
    }

    [Test]
    public async Task BoundSpawnerIds_AreCarriedAsDiagnostics()
    {
        var row = Schedule(id: 1, start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0));

        var projection = EventCenterRowProjector.Project(row, [11u, 22u]);

        await Assert.That(projection.BoundSpawnerTemplateIds).IsEquivalentTo(new[] { 11u, 22u });
    }

    [Test]
    public async Task Catalog_TalliesEveryGapAndKeepsRowsOrdered()
    {
        var rows = new[]
        {
            Schedule(id: 23, start: Bound(2013, 7, 17, 6, 0), end: Bound(2013, 7, 31, 6, 0)),
            Schedule(id: 1, start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0)),
            Schedule(id: 2, start: Bound(2013, 6, 12, 0, 0), end: Bound(2013, 7, 30, 0, 0), startTime: 14, endTime: 16)
        };

        var catalog = EventCenterRowCatalog.Build(rows);

        await Assert.That(catalog.Rows.Select(row => row.ScheduleId)).IsEquivalentTo(new[] { 1, 2, 23 });
        await Assert.That(catalog.RowsWithPeriod).IsEqualTo(3);
        await Assert.That(catalog.WireReadyRows).IsEqualTo(0);
        await Assert.That(catalog.CountOf(EventCenterRowGap.RecurringWithinPeriod)).IsEqualTo(1);
        await Assert.That(catalog.CountOf(EventCenterRowGap.MissingTitleSource)).IsEqualTo(3);
        await Assert.That(catalog.CountOf(EventCenterRowGap.None)).IsEqualTo(0);
    }

    [Test]
    public async Task Catalog_ReportsTheShippedRowShapesItWasGiven()
    {
        // One row of every shape the shipped content holds: all-day period, windowed period, weekday
        // period, period-less row, and a row whose end precedes its start.
        var rows = new[]
        {
            Schedule(id: 1, start: Bound(2013, 6, 14, 14, 0), end: Bound(2013, 6, 17, 14, 0)),
            Schedule(id: 6, start: Bound(2013, 7, 3, 0, 0), end: Bound(2013, 7, 31, 0, 0), startTime: 12, endTime: 13),
            Schedule(id: 4, start: Bound(2013, 6, 1, 0, 0), end: Bound(2013, 6, 30, 0, 0), dayOfWeek: DayOfWeek.Friday),
            Schedule(id: 16, start: Bound(0, 0, 0, 0, 0), end: Bound(0, 1, 1, 0, 0)),
            Schedule(id: 14, start: Bound(2013, 7, 22, 0, 0), end: Bound(2013, 1, 1, 0, 0))
        };

        var catalog = EventCenterRowCatalog.Build(rows);

        await Assert.That(catalog.Rows.Count).IsEqualTo(5);
        await Assert.That(catalog.RowsWithPeriod).IsEqualTo(3);
        await Assert.That(catalog.CountOf(EventCenterRowGap.RecurringWithinPeriod)).IsEqualTo(1);
        await Assert.That(catalog.CountOf(EventCenterRowGap.RecurringByWeekday)).IsEqualTo(1);
        await Assert.That(catalog.CountOf(EventCenterRowGap.IncompleteCalendarPeriod)).IsEqualTo(1);
        await Assert.That(catalog.CountOf(EventCenterRowGap.EndNotAfterStart)).IsEqualTo(1);
    }

    [Test]
    public async Task Catalog_RefusesTwoRowsWithTheSameId()
    {
        var rows = new[]
        {
            Schedule(id: 5, start: Bound(2013, 6, 1, 0, 0), end: Bound(2013, 6, 2, 0, 0)),
            Schedule(id: 5, start: Bound(2013, 7, 1, 0, 0), end: Bound(2013, 7, 2, 0, 0))
        };

        var thrown = Assert.Throws<InvalidOperationException>(() => EventCenterRowCatalog.Build(rows));

        await Assert.That(thrown?.Message).Contains("5");
    }

    [Test]
    public async Task EmptyCatalog_HasNoRowsAndNoWritableRow()
    {
        await Assert.That(EventCenterRowCatalog.Empty.Rows.Count).IsEqualTo(0);
        await Assert.That(EventCenterRowCatalog.Empty.RowsWithPeriod).IsEqualTo(0);
        await Assert.That(EventCenterRowCatalog.Empty.WireReadyRows).IsEqualTo(0);
        await Assert.That(EventCenterRowCatalog.Empty.CountOf(EventCenterRowGap.MissingBodySource)).IsEqualTo(0);
    }

    private static (int Year, int Month, int Day, int Hour, int Minute) Bound(
        int year, int month, int day, int hour, int minute) => (year, month, day, hour, minute);

    private static GameSchedules Schedule(
        int id,
        (int Year, int Month, int Day, int Hour, int Minute) start,
        (int Year, int Month, int Day, int Hour, int Minute) end,
        string name = null,
        DayOfWeek dayOfWeek = DayOfWeek.Invalid,
        int startTime = 0,
        int endTime = 0,
        int startTimeMin = 0,
        int endTimeMin = 0) => new()
        {
            Id = id,
            Name = name ?? $"row {id}",
            DayOfWeekId = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            StartTimeMin = startTimeMin,
            EndTimeMin = endTimeMin,
            StYear = start.Year,
            StMonth = start.Month,
            StDay = start.Day,
            StHour = start.Hour,
            StMin = start.Minute,
            EdYear = end.Year,
            EdMonth = end.Month,
            EdDay = end.Day,
            EdHour = end.Hour,
            EdMin = end.Minute
        };
}
