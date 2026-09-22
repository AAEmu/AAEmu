using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Schedules;
using AAEmu.Game.Models.Game.Weather;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class WeatherManagerTests
{
    // 2026-09-20 is a Sunday, 09-21 a Monday, 09-22 a Tuesday (content weekday ids: Sunday=1 …
    // Saturday=7, Invalid=8 means every day).

    [Test]
    public async Task Refresh_NeverConfigured_DisablesLoudlyAndStaysClear()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);

        var result = manager.Refresh(Utc(2026, 9, 22, 12));

        await Assert.That(result.Disabled).IsTrue();
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("disabled");
        await Assert.That(manager.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task Refresh_EmptyPhaseList_DisablesLoudlyAndStaysClear()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(new WeatherConfig(), Lookup());

        var result = manager.Refresh(Utc(2026, 9, 22, 12));

        await Assert.That(result.Disabled).IsTrue();
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("no weather phases are configured");
        await Assert.That(manager.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task Refresh_SnowWindowStartsAndEndsOnContentTimes()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(Config((11, "Snow")), Lookup(Schedule(11, startHour: 8, endHour: 10)));

        var beforeWindow = manager.Refresh(Utc(2026, 9, 22, 7, 59));
        await Assert.That(beforeWindow.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(beforeWindow.Transitioned).IsFalse();

        var insideWindow = manager.Refresh(Utc(2026, 9, 22, 8, 1));
        await Assert.That(insideWindow.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(insideWindow.Transitioned).IsTrue();
        await Assert.That(insideWindow.ActiveScheduleId).IsEqualTo((int?)11);
        await Assert.That(insideWindow.Errors).IsEmpty();

        var afterWindow = manager.Refresh(Utc(2026, 9, 22, 10, 1));
        await Assert.That(afterWindow.CurrentState).IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
    }

    [Test]
    public async Task Refresh_WindowHonoursContentMinutes()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((12, "Snow")),
            Lookup(Schedule(12, startHour: 12, startMinute: 15, endHour: 14, endMinute: 45)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 12, 14)).CurrentState)
            .IsEqualTo(WeatherState.Clear);
        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 12, 15)).CurrentState)
            .IsEqualTo(WeatherState.Snow);
        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 14, 45)).CurrentState)
            .IsEqualTo(WeatherState.Snow);
        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 14, 46)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
    }

    [Test]
    public async Task Refresh_CycleWalksTwoSnowWindowsAcrossTheDay()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((1, "Snow"), (2, "Snow")),
            Lookup(
                Schedule(1, startHour: 8, endHour: 10),
                Schedule(2, startHour: 12, endHour: 14)));

        var states = new List<WeatherState>();
        foreach (var hour in new[] { 7, 9, 11, 13, 15 })
            states.Add(manager.Refresh(Utc(2026, 9, 22, hour)).CurrentState);

        await Assert.That(states).IsEquivalentTo(
        [
            WeatherState.Clear,
            WeatherState.Snow,
            WeatherState.Clear,
            WeatherState.Snow,
            WeatherState.Clear,
        ]);

        await Assert.That(transitions).HasCount().EqualTo(4);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
        await Assert.That(transitions[2]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[3]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
    }

    [Test]
    public async Task Refresh_WindowCrossingMidnight_StaysOpenAcrossUtcDateFlip()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(Config((21, "Snow")), Lookup(Schedule(21, startHour: 22, endHour: 2)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 21, 59)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 23, 30)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        // The window continues past the UTC date flip without a second transition.
        var afterFlip = manager.Refresh(Utc(2026, 9, 23, 1, 30));
        await Assert.That(afterFlip.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(afterFlip.Transitioned).IsFalse();

        await Assert.That(manager.Refresh(Utc(2026, 9, 23, 3, 30)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        // The next UTC day opens the window again.
        await Assert.That(manager.Refresh(Utc(2026, 9, 23, 22, 15)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        await Assert.That(transitions).HasCount().EqualTo(3);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
        await Assert.That(transitions[2]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
    }

    [Test]
    public async Task Refresh_UnspecifiedKindMoment_IsReadAsUtcNotLocal()
    {
        var manager = new WeatherManager();
        manager.Configure(Config((31, "Snow")), Lookup(Schedule(31, startHour: 22, endHour: 2)));

        // MySQL-style Unspecified timestamps must be taken at face value as UTC.
        var moment = new DateTime(2026, 9, 22, 23, 30, 0, DateTimeKind.Unspecified);
        var result = manager.Refresh(moment);

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Snow);
    }

    [Test]
    public async Task Refresh_BoundedPeriod_RemainsActiveAcrossUtcMidnight()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((41, "Snow")),
            Lookup(Schedule(41, startDate: new DateTime(2026, 9, 20), endDate: new DateTime(2026, 9, 25))));

        await Assert.That(manager.Refresh(Utc(2026, 9, 21, 23, 59)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        var afterMidnight = manager.Refresh(Utc(2026, 9, 22, 0, 1));
        await Assert.That(afterMidnight.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(afterMidnight.Transitioned).IsFalse();

        await Assert.That(manager.Refresh(Utc(2026, 9, 26, 0, 1)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
    }

    [Test]
    public async Task Refresh_WeekdayWindow_FlipsAtUtcDateBoundary()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((51, "Snow")),
            Lookup(Schedule(51, day: DayOfWeek.Monday)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 20, 23, 59)).CurrentState) // Sunday
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(manager.Refresh(Utc(2026, 9, 21, 0, 1)).CurrentState) // Monday
            .IsEqualTo(WeatherState.Snow);

        var laterOnMonday = manager.Refresh(Utc(2026, 9, 21, 23, 59));
        await Assert.That(laterOnMonday.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(laterOnMonday.Transitioned).IsFalse();

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 0, 1)).CurrentState) // Tuesday
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
    }

    [Test]
    public async Task Refresh_ClockJump_EmitsNoEdgesForTheGapItSkipped()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((1, "Snow"), (2, "Snow")),
            Lookup(
                Schedule(1, startHour: 8, endHour: 10),
                Schedule(2, startHour: 12, endHour: 14)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 9)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        // Jump forward over the clear gap into the next window: the state never visibly left snow.
        var jumped = manager.Refresh(Utc(2026, 9, 22, 13));
        await Assert.That(jumped.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(jumped.Transitioned).IsFalse();
        await Assert.That(jumped.ActiveScheduleId).IsEqualTo((int?)2);

        // Jump to the small hours of the next day: back to clear.
        await Assert.That(manager.Refresh(Utc(2026, 9, 23, 3)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
    }

    [Test]
    public async Task Refresh_MissingScheduleRow_SkipsLoudlyButKeepsValidPhase()
    {
        var manager = new WeatherManager();
        manager.Configure(
            Config((999, "Snow"), (11, "Snow")),
            Lookup(Schedule(11, startHour: 8, endHour: 10)));

        var result = manager.Refresh(Utc(2026, 9, 22, 9));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("999");
        await Assert.That(result.Errors[0]).Contains("has no row");
    }

    [Test]
    public async Task Refresh_EveryPhaseMissingItsRow_StaysClearAndReports()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(Config((999, "Snow")), Lookup());

        var result = manager.Refresh(Utc(2026, 9, 22, 9));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("999");
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task Refresh_UnknownStateName_SkipsThatPhaseAndReports()
    {
        var manager = new WeatherManager();
        manager.Configure(
            Config((11, "Hail"), (12, "Snow")),
            Lookup(
                Schedule(11, startHour: 8, endHour: 10),
                Schedule(12, startHour: 8, endHour: 10)));

        var result = manager.Refresh(Utc(2026, 9, 22, 9));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Snow);
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("Hail");
        await Assert.That(result.Errors[0]).Contains("not a weather state");
    }

    [Test]
    public async Task Refresh_RainAndWindAreNotWeatherStates()
    {
        // The client has no packet for either, so a phase naming one is refused instead of being
        // tracked as a state nothing can show.
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((11, "Rain"), (12, "Wind")),
            Lookup(
                Schedule(11, startHour: 8, endHour: 10),
                Schedule(12, startHour: 8, endHour: 10)));

        var result = manager.Refresh(Utc(2026, 9, 22, 9));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(result.Errors).HasCount().EqualTo(2);
        await Assert.That(result.Errors.All(error => error.Contains("not a weather state"))).IsTrue();
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task Refresh_OverlappingPhases_KeepPreviousStateAndReport()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(
            Config((1, "Snow"), (2, "Snow")),
            Lookup(
                Schedule(1, startHour: 9, endHour: 11),
                Schedule(2, startHour: 9, endHour: 12)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 8, 30)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        // Both windows are open at 09:30: the conflict is reported and the previous state stands.
        var overlap = manager.Refresh(Utc(2026, 9, 22, 9, 30));
        await Assert.That(overlap.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(overlap.Transitioned).IsFalse();
        await Assert.That(overlap.Errors).HasSingleItem();
        await Assert.That(overlap.Errors[0]).Contains("overlap");

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 11, 30)).CurrentState)
            .IsEqualTo(WeatherState.Snow);
        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 12, 30)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
    }

    [Test]
    public async Task Refresh_MalformedContentRow_SkipsPhaseWithError()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);

        // A calendar bound that cannot form a date: the row itself is un-evaluable.
        var malformed = Schedule(71);
        malformed.StYear = 2026;
        malformed.StMonth = 13;
        malformed.StDay = 1;
        manager.Configure(Config((71, "Snow")), Lookup(malformed));

        var result = manager.Refresh(Utc(2026, 9, 22, 9));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(result.Errors).HasSingleItem();
        await Assert.That(result.Errors[0]).Contains("cannot be evaluated");
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task Restart_FreshManagerReDerivesStateFromClockAndContent()
    {
        var config = Config((11, "Snow"));
        var lookup = Lookup(Schedule(11, startHour: 8, endHour: 10));

        var firstBoot = new WeatherManager();
        firstBoot.Configure(config, lookup);
        await Assert.That(firstBoot.Refresh(Utc(2026, 9, 22, 9)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        // A restart starts from scratch and still lands on the state the clock implies.
        var secondBoot = new WeatherManager();
        var transitions = RecordTransitions(secondBoot);
        secondBoot.Configure(config, lookup);
        await Assert.That(secondBoot.Refresh(Utc(2026, 9, 22, 9)).CurrentState)
            .IsEqualTo(WeatherState.Snow);
        await Assert.That(transitions).HasCount().EqualTo(1);

        await Assert.That(secondBoot.Refresh(Utc(2026, 9, 22, 15)).CurrentState)
            .IsEqualTo(WeatherState.Clear);
    }

    [Test]
    public async Task Restart_DoesNotCarryRuntimeStateIntoTheNewBoot()
    {
        var config = Config((11, "Snow"));
        var lookup = Lookup(Schedule(11, startHour: 8, endHour: 10));

        var beforeRestart = new WeatherManager();
        beforeRestart.Configure(config, lookup);
        await Assert.That(beforeRestart.Refresh(Utc(2026, 9, 22, 9)).CurrentState)
            .IsEqualTo(WeatherState.Snow);

        var afterRestart = new WeatherManager();
        var transitions = RecordTransitions(afterRestart);
        afterRestart.Configure(config, lookup);

        var result = afterRestart.Refresh(Utc(2026, 9, 22, 5));

        await Assert.That(result.CurrentState).IsEqualTo(WeatherState.Clear);
        await Assert.That(transitions).IsEmpty();
    }

    [Test]
    public async Task SnowPhase_StateNameIsCaseInsensitive()
    {
        var manager = new WeatherManager();
        var transitions = RecordTransitions(manager);
        manager.Configure(Config((3, "snow")), Lookup(Schedule(3, startHour: 8, endHour: 10)));

        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 9)).CurrentState)
            .IsEqualTo(WeatherState.Snow);
        await Assert.That(manager.Refresh(Utc(2026, 9, 22, 11)).CurrentState)
            .IsEqualTo(WeatherState.Clear);

        await Assert.That(transitions).HasCount().EqualTo(2);
        await Assert.That(transitions[0]).IsEqualTo((WeatherState.Clear, WeatherState.Snow));
        await Assert.That(transitions[1]).IsEqualTo((WeatherState.Snow, WeatherState.Clear));
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static GameSchedules Schedule(
        int id,
        int startHour = 0,
        int startMinute = 0,
        int endHour = 0,
        int endMinute = 0,
        DayOfWeek day = DayOfWeek.Invalid,
        DateTime? startDate = null,
        DateTime? endDate = null) => new()
    {
        Id = id,
        Name = $"schedule {id}",
        DayOfWeekId = day,
        StartTime = startHour,
        StartTimeMin = startMinute,
        EndTime = endHour,
        EndTimeMin = endMinute,
        StYear = startDate?.Year ?? 0,
        StMonth = startDate?.Month ?? 0,
        StDay = startDate?.Day ?? 0,
        EdYear = endDate?.Year ?? 0,
        EdMonth = endDate?.Month ?? 0,
        EdDay = endDate?.Day ?? 0,
    };

    private static Func<int, GameSchedules> Lookup(params GameSchedules[] rows) =>
        id => Array.Find(rows, row => row.Id == id);

    private static WeatherConfig Config(params (int ScheduleId, string State)[] phases) => new()
    {
        Phases = phases
            .Select(phase => new WeatherPhaseConfig
            {
                ScheduleId = phase.ScheduleId,
                State = phase.State,
            })
            .ToList(),
    };

    private static List<(WeatherState From, WeatherState To)> RecordTransitions(WeatherManager manager)
    {
        var transitions = new List<(WeatherState, WeatherState)>();
        manager.StateChanged += (from, to) => transitions.Add((from, to));
        return transitions;
    }
}
