using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Models.Game.Schedules;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.UnitTests.Game.Models.Game.EventCenter;

/// <summary>
/// Pins the W09B runtime state machine against the row shapes W09A projects.
/// No test invents a title, body, link, reward or client packet: W09A marks every such row unwritable.
/// </summary>
public class EventCenterRuntimeStateTests
{
    [Test]
    public async Task CurrentPeriod_UsesHalfOpenBoundsAndReportsProjectedNextBoundary()
    {
        var start = Utc(2030, 1, 2, 10, 0);
        var end = Utc(2030, 1, 2, 12, 0);
        var machine = new EventCenterRuntimeMachine();

        var before = machine.Initialize(Catalog(Row(42, start, end)), start.AddMinutes(-1));
        await Assert.That(before.IsReplay).IsTrue();
        await Assert.That(before.Snapshot.UpcomingScheduleIds).IsEquivalentTo(new[] { 42 });
        await Assert.That(before.Snapshot.CurrentScheduleIds).IsEmpty();
        await Assert.That(before.Snapshot.NextBoundaryUtc).IsEqualTo(start);

        var atStart = machine.Refresh(start);
        await Assert.That(atStart.StartedScheduleIds).IsEquivalentTo(new[] { 42 });
        await Assert.That(atStart.Snapshot.CurrentScheduleIds).IsEquivalentTo(new[] { 42 });
        await Assert.That(atStart.Snapshot.UpcomingScheduleIds).IsEmpty();
        await Assert.That(atStart.Snapshot.NextBoundaryUtc).IsEqualTo(end);

        var atEnd = machine.Refresh(end);
        await Assert.That(atEnd.EndedScheduleIds).IsEquivalentTo(new[] { 42 });
        await Assert.That(atEnd.Snapshot.CurrentScheduleIds).IsEmpty();
        await Assert.That(atEnd.Snapshot.EndedScheduleIds).IsEquivalentTo(new[] { 42 });
        await Assert.That(atEnd.Snapshot.NextBoundaryUtc).IsNull();
    }

    [Test]
    public async Task UpcomingSelection_TakesTheEarliestStartAndKeepsTies()
    {
        var firstStart = Utc(2030, 2, 1, 12, 0);
        var laterStart = Utc(2030, 2, 1, 13, 0);
        var end = Utc(2030, 2, 1, 14, 0);
        var now = Utc(2030, 2, 1, 11, 0);
        var machine = new EventCenterRuntimeMachine();

        var update = machine.Initialize(
            Catalog(
                Row(9, firstStart, end),
                Row(3, laterStart, end),
                Row(7, firstStart, end)),
            now);

        await Assert.That(update.Snapshot.UpcomingScheduleIds).IsEquivalentTo(new[] { 7, 9 });
        await Assert.That(update.Snapshot.CurrentScheduleIds).IsEmpty();
        await Assert.That(update.Snapshot.NextBoundaryUtc).IsEqualTo(firstStart);
    }

    [Test]
    public async Task RecurringRows_AreDeferredWithoutChoosingAnOccurrencePolicy()
    {
        var periodStart = Utc(2030, 3, 1, 0, 0);
        var periodEnd = Utc(2030, 3, 8, 0, 0);
        var windowed = Row(11, periodStart, periodEnd, startTime: 14, endTime: 16);
        var weekday = Row(12, periodStart, periodEnd, weekday: DayOfWeek.Friday);
        var machine = new EventCenterRuntimeMachine();

        var update = machine.Initialize(
            Catalog(windowed, weekday),
            Utc(2030, 3, 4, 15, 0));

        await Assert.That(update.Snapshot.CurrentScheduleIds).IsEmpty();
        await Assert.That(update.Snapshot.UpcomingScheduleIds).IsEmpty();
        await Assert.That(update.Snapshot.DeferredScheduleIds).IsEquivalentTo(new[] { 11, 12 });
        await Assert.That(update.Snapshot.Rows.Single(row => row.ScheduleId == 11).DeferReason)
            .IsEqualTo(EventCenterRuntimeDeferReason.RecurringOccurrence);
        await Assert.That(update.Snapshot.Rows.Single(row => row.ScheduleId == 12).DeferReason)
            .IsEqualTo(EventCenterRuntimeDeferReason.RecurringOccurrence);
        await Assert.That(update.Snapshot.NextBoundaryUtc).IsNull();
    }

    [Test]
    public async Task UnresolvedPeriod_IsDeferredAndNeverBecomesUpcoming()
    {
        var unresolved = new GameSchedules
        {
            Id = 14,
            Name = "period-less",
            DayOfWeekId = DayOfWeek.Invalid,
            StartTime = 0,
            EndTime = 0,
            StYear = 0,
            StMonth = 0,
            StDay = 0,
            EdYear = 0,
            EdMonth = 1,
            EdDay = 1
        };
        var machine = new EventCenterRuntimeMachine();

        var update = machine.Initialize(Catalog(unresolved), Utc(2030, 4, 1, 0, 0));

        await Assert.That(update.Snapshot.DeferredScheduleIds).IsEquivalentTo(new[] { 14 });
        await Assert.That(update.Snapshot.Rows.Single().DeferReason)
            .IsEqualTo(EventCenterRuntimeDeferReason.PeriodUnresolved);
        await Assert.That(update.Snapshot.NextBoundaryUtc).IsNull();
    }

    [Test]
    public async Task RestartReplay_ReconstructsTheSameStateWithoutPersistedRuntimeRows()
    {
        var start = Utc(2030, 5, 1, 10, 0);
        var end = Utc(2030, 5, 1, 12, 0);
        var catalog = Catalog(Row(21, start, end));
        var now = Utc(2030, 5, 1, 11, 0);

        var running = new EventCenterRuntimeMachine();
        running.Initialize(catalog, start.AddMinutes(-1));
        var advanced = running.Refresh(now);

        var restarted = new EventCenterRuntimeMachine();
        var replay = restarted.Initialize(catalog, now);

        await Assert.That(replay.IsReplay).IsTrue();
        await Assert.That(advanced.Snapshot.CurrentScheduleIds)
            .IsEquivalentTo(replay.Snapshot.CurrentScheduleIds);
        await Assert.That(advanced.Snapshot.UpcomingScheduleIds)
            .IsEquivalentTo(replay.Snapshot.UpcomingScheduleIds);
        await Assert.That(advanced.Snapshot.NextBoundaryUtc)
            .IsEqualTo(replay.Snapshot.NextBoundaryUtc);
    }

    [Test]
    public async Task RuntimeState_DoesNotPromoteUnwritableRowsToAClientRow()
    {
        var start = Utc(2030, 6, 1, 10, 0);
        var end = Utc(2030, 6, 1, 12, 0);
        var machine = new EventCenterRuntimeMachine();

        var update = machine.Initialize(Catalog(Row(31, start, end)), start.AddMinutes(1));

        await Assert.That(update.Snapshot.CurrentScheduleIds).IsEquivalentTo(new[] { 31 });
        await Assert.That(update.Snapshot.WireReadyRows).IsEqualTo(0);
        await Assert.That(update.Snapshot.Rows.Single().Projection.IsWireReady).IsFalse();
    }

    [Test]
    public async Task Refresh_ReportsUpcomingChangesAtAProjectedStartBoundary()
    {
        var firstStart = Utc(2030, 7, 1, 12, 0);
        var firstEnd = Utc(2030, 7, 1, 13, 0);
        var secondStart = Utc(2030, 7, 1, 13, 30);
        var secondEnd = Utc(2030, 7, 1, 14, 0);
        var machine = new EventCenterRuntimeMachine();
        machine.Initialize(
            Catalog(Row(1, firstStart, firstEnd), Row(2, secondStart, secondEnd)),
            firstStart.AddMinutes(-1));

        var atFirstStart = machine.Refresh(firstStart);
        await Assert.That(atFirstStart.StartedScheduleIds).IsEquivalentTo(new[] { 1 });
        await Assert.That(atFirstStart.Snapshot.UpcomingScheduleIds).IsEquivalentTo(new[] { 2 });

        var atFirstEnd = machine.Refresh(firstEnd);
        await Assert.That(atFirstEnd.EndedScheduleIds).IsEquivalentTo(new[] { 1 });
        await Assert.That(atFirstEnd.Snapshot.UpcomingScheduleIds).IsEquivalentTo(new[] { 2 });

        var atSecondStart = machine.Refresh(secondStart);
        await Assert.That(atSecondStart.StartedScheduleIds).IsEquivalentTo(new[] { 2 });
        await Assert.That(atSecondStart.Snapshot.UpcomingScheduleIds).IsEmpty();
    }

    [Test]
    public async Task InvalidResolvedProjection_FailsLoudlyInsteadOfClamping()
    {
        var invalid = new EventCenterRowProjection
        {
            ScheduleId = 99,
            PeriodStart = Utc(2030, 8, 1, 12, 0),
            PeriodEnd = Utc(2030, 8, 1, 12, 0),
            Gaps = EventCenterRowGap.None
        };

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            EventCenterRuntimeEvaluator.Evaluate([invalid], Utc(2030, 8, 1, 0, 0)));

        await Assert.That(thrown?.Message).Contains("99");
    }

    private static EventCenterRowCatalog Catalog(params GameSchedules[] rows) =>
        EventCenterRowCatalog.Build(rows);

    private static GameSchedules Row(
        int id,
        DateTimeOffset start,
        DateTimeOffset end,
        DayOfWeek? weekday = null,
        int startTime = 0,
        int endTime = 0) => new()
    {
        Id = id,
        Name = $"row {id}",
        DayOfWeekId = weekday ?? DayOfWeek.Invalid,
        StartTime = startTime,
        EndTime = endTime,
        StartTimeMin = 0,
        EndTimeMin = 0,
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

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
