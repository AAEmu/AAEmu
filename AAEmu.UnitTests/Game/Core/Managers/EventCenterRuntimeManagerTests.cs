using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Schedules;

using GameTask = AAEmu.Game.Models.Tasks.Task;
using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;
using SystemTask = System.Threading.Tasks.Task;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class EventCenterRuntimeManagerTests
{
    [Test]
    public async Task DuplicateReschedule_CancelsTheOldTaskAndStaleTaskCannotFire()
    {
        var now = Utc(2031, 1, 1, 10, 0);
        var tasks = new RecordingTaskManager();
        var manager = NewManager(
            Row(1, now.AddHours(1), now.AddHours(2)),
            tasks,
            new FixedTimeProvider(now));
        manager.Load();

        var first = tasks.Active.Single();
        manager.RefreshAndReschedule();
        var second = tasks.Active.Single();

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(tasks.Cancelled.Contains(first)).IsTrue();
        await Assert.That(tasks.Active).HasCount(1);

        first.Execute();

        await Assert.That(tasks.Active).HasCount(1);
        await Assert.That(tasks.Active[0]).IsEqualTo(second);
    }

    [Test]
    public async Task InterleavedBoundaryApply_StaleTaskCannotClearTheNewerChain()
    {
        var now = Utc(2031, 1, 15, 10, 0);
        var tasks = new RecordingTaskManager();
        var manager = NewManager(
            Row(5, now.AddHours(1), now.AddHours(2)),
            tasks,
            new FixedTimeProvider(now));
        manager.Load();

        var first = tasks.Active.Single();
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var blockNextSchedule = 1;
        tasks.BeforeSchedule = _ =>
        {
            if (Interlocked.Exchange(ref blockNextSchedule, 0) != 0)
            {
                entered.Set();
                release.Wait();
            }
        };

        tasks.Active.Remove(first);
        var firstApply = SystemTask.Run(first.Execute);
        try
        {
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();

            manager.RefreshAndReschedule();
            var newer = tasks.Active.Single();
            release.Set();
            await firstApply;

            await Assert.That(tasks.Active).HasCount(1);
            await Assert.That(tasks.Active[0]).IsEqualTo(newer);
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public async Task PastDueBoundary_SchedulesFromTheProjectedBoundWithoutClampingToAPollInterval()
    {
        var baseTime = Utc(2031, 2, 1, 10, 0);
        var tasks = new RecordingTaskManager();
        var provider = new AdvancingTimeProvider(baseTime, TimeSpan.FromMinutes(2));
        var manager = NewManager(
            Row(2, baseTime.AddHours(-1), baseTime.AddMinutes(1)),
            tasks,
            provider);

        manager.Load();

        await Assert.That(tasks.Delays).HasCount(1);
        await Assert.That(tasks.Delays[0] < TimeSpan.Zero).IsTrue();
    }

    [Test]
    public async Task ManagerRestart_ReplaysTheSameStateAtTheSameInstant()
    {
        var start = Utc(2031, 3, 1, 10, 0);
        var end = Utc(2031, 3, 1, 12, 0);
        var now = start.AddMinutes(-1);
        var replayTime = start.AddMinutes(1);
        var runningTasks = new RecordingTaskManager();
        var running = NewManager(
            Row(3, start, end),
            runningTasks,
            new FixedTimeProvider(now));
        running.Load();
        running.Refresh(replayTime);

        var restarted = NewManager(
            Row(3, start, end),
            new RecordingTaskManager(),
            new FixedTimeProvider(replayTime));
        restarted.Load();

        await Assert.That(running.Snapshot.CurrentScheduleIds)
            .IsEquivalentTo(restarted.Snapshot.CurrentScheduleIds);
        await Assert.That(running.Snapshot.UpcomingScheduleIds)
            .IsEquivalentTo(restarted.Snapshot.UpcomingScheduleIds);
        await Assert.That(running.Snapshot.NextBoundaryUtc)
            .IsEqualTo(restarted.Snapshot.NextBoundaryUtc);
    }

    [Test]
    public async Task TaskScheduleFailure_ClearsTheHandleAndAllowsARetriedChain()
    {
        var now = Utc(2031, 4, 1, 10, 0);
        var tasks = new RecordingTaskManager();
        var manager = NewManager(
            Row(4, now.AddHours(1), now.AddHours(2)),
            tasks,
            new FixedTimeProvider(now));
        manager.Load();
        tasks.ThrowOnSchedule = true;

        var thrown = Assert.Throws<InvalidOperationException>(() => manager.RefreshAndReschedule());
        await Assert.That(thrown?.Message).Contains("scheduled failure");
        await Assert.That(tasks.Active).IsEmpty();

        tasks.ThrowOnSchedule = false;
        manager.RefreshAndReschedule();
        await Assert.That(tasks.Active).HasCount(1);
    }

    private static EventCenterRuntimeManager NewManager(
        GameSchedules row,
        RecordingTaskManager tasks,
        TimeProvider provider) =>
        new(new FakeScheduleManager(row), tasks, provider);

    private static GameSchedules Row(int id, DateTimeOffset start, DateTimeOffset end) => new()
    {
        Id = id,
        Name = $"row {id}",
        DayOfWeekId = DayOfWeek.Invalid,
        StartTime = 0,
        EndTime = 0,
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

    private sealed class FakeScheduleManager(GameSchedules row) : IGameScheduleManager
    {
        private readonly IReadOnlyDictionary<int, GameSchedules> _rows = new Dictionary<int, GameSchedules>
        {
            [row.Id] = row
        };

        public void Load() { }

        public void LoadGameSchedules(Dictionary<int, GameSchedules> gameSchedules) { }

        public void LoadGameScheduleSpawners(Dictionary<int, GameScheduleSpawners> gameScheduleSpawners) { }

        public void LoadGameScheduleDoodads(Dictionary<int, GameScheduleDoodads> gameScheduleDoodads) { }

        public NpcSpawnerWindowState GetSpawnerWindowState(uint spawnerTemplateId) => default;

        public HashSet<uint> GetClosedSpawnerTemplateIds() => [];

        public HashSet<uint> GetScheduledSpawnerTemplateIds() => [];

        public HashSet<int> GetRunningGameScheduleIds() => [];

        public IReadOnlyList<uint> GetSpawnerIdsForSchedule(int gameScheduleId) => [];

        public IReadOnlyDictionary<int, GameSchedules> GetSchedules() => _rows;
    }

    private sealed class RecordingTaskManager : ITaskManager
    {
        private uint _nextId;

        public List<GameTask> Active { get; } = [];

        public List<GameTask> Cancelled { get; } = [];

        public List<TimeSpan> Delays { get; } = [];

        public bool ThrowOnSchedule { get; set; }

        public Action<GameTask> BeforeSchedule { get; set; }

        public void Initialize() { }

        public void Start() { }

        public SystemTask StopAsync(CancellationToken cancellationToken) => SystemTask.CompletedTask;

        public bool Schedule(GameTask task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1)
        {
            if (ThrowOnSchedule)
                throw new InvalidOperationException("scheduled failure");

            BeforeSchedule?.Invoke(task);
            task.Id = ++_nextId;
            Active.Add(task);
            Delays.Add(startTime ?? TimeSpan.Zero);
            return true;
        }

        public bool Cancel(GameTask task)
        {
            var removed = Active.Remove(task);
            if (removed)
                Cancelled.Add(task);
            return removed;
        }

        public bool CronSchedule(GameTask task, string cronExpression, TimeSpan? startDelay = null, int count = -1) =>
            false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AdvancingTimeProvider(DateTimeOffset now, TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private readonly TimeSpan _step = step;

        public override DateTimeOffset GetUtcNow()
        {
            var value = _now;
            _now = _now.Add(_step);
            return value;
        }
    }
}
