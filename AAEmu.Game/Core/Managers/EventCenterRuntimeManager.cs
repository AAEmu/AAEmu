using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.EventCenter;
using AAEmu.Game.Models.Tasks.EventCenter;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Owns the event-center runtime view derived from the W09A schedule projection.
/// </summary>
/// <remarks>
/// This manager is deliberately state-only. It does not send an event-board packet: W09A reports that
/// every projected row lacks the text, link, reward and ordering sources required by <c>SC 0x2DE</c>.
/// The manager still advances its current/upcoming classification at the exact next projected boundary so
/// a later wire slice has a truthful state to consume. Every boundary it schedules is one of the row's own
/// projected instants; there is no polling interval or look-ahead constant.
/// </remarks>
public sealed class EventCenterRuntimeManager(
    IGameScheduleManager scheduleManager,
    ITaskManager taskManager,
    TimeProvider timeProvider) : Singleton<EventCenterRuntimeManager>, ILoadable
{
    private readonly IGameScheduleManager _scheduleManager = scheduleManager;
    private readonly ITaskManager _taskManager = taskManager;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly object _gate = new();
    private readonly object _scheduleGate = new();
    private EventCenterRuntimeMachine _machine;
    private EventCenterRuntimeBoundaryTask _scheduledTask;
    private long _scheduleGeneration;
    private bool _loaded;

    /// <summary>The most recent immutable runtime view, or an empty view before load.</summary>
    public EventCenterRuntimeSnapshot Snapshot => _machine?.Snapshot ?? EventCenterRuntimeSnapshot.Empty;

    /// <summary>Builds the W09A projection and reconstructs the runtime state at boot.</summary>
    public void Load()
    {
        lock (_gate)
        {
            if (_loaded)
                return;
            _loaded = true;
        }

        var catalog = EventCenterRowCatalog.Build(_scheduleManager);
        EventCenterRuntimeMachine machine;
        lock (_gate)
        {
            _machine = new EventCenterRuntimeMachine();
            machine = _machine;
        }

        var update = machine.Initialize(catalog, Now());
        BoundarySchedulePlan plan;
        lock (_scheduleGate)
        {
            plan = BuildPlanLocked(update.Snapshot);
        }

        ApplyPlan(plan);
    }

    /// <summary>Re-evaluates the runtime at an explicit instant without scheduling another task.</summary>
    public EventCenterRuntimeUpdate Refresh(DateTimeOffset now) => GetMachine().Refresh(now);

    /// <summary>Re-evaluates at the current wall clock and arms the next projected boundary.</summary>
    public EventCenterRuntimeUpdate RefreshAndReschedule()
    {
        EventCenterRuntimeUpdate update;
        BoundarySchedulePlan plan;
        lock (_scheduleGate)
        {
            update = Refresh(Now());
            plan = BuildPlanLocked(update.Snapshot);
        }

        ApplyPlan(plan);
        return update;
    }

    /// <summary>
    /// Consumes a scheduled task and builds its replacement while the schedule gate is held. The task
    /// manager is called only after the gate is released, so a stale task cannot clear a newer chain.
    /// </summary>
    internal void ExecuteBoundary(EventCenterRuntimeBoundaryTask task)
    {
        BoundarySchedulePlan plan;
        lock (_scheduleGate)
        {
            if (!IsCurrentLocked(task))
                return;

            _scheduledTask = null;
            var update = Refresh(Now());
            plan = BuildPlanLocked(update.Snapshot);
        }

        ApplyPlan(plan);
    }

    private EventCenterRuntimeMachine GetMachine()
    {
        lock (_gate)
        {
            return _machine ?? throw new InvalidOperationException(
                "Event-center runtime has not been loaded yet.");
        }
    }

    private BoundarySchedulePlan BuildPlanLocked(EventCenterRuntimeSnapshot snapshot)
    {
        var previous = _scheduledTask;
        _scheduledTask = null;
        var generation = ++_scheduleGeneration;

        if (snapshot.NextBoundaryUtc is not { } nextBoundaryUtc)
            return new BoundarySchedulePlan(previous, null, TimeSpan.Zero);

        // A boundary that is already due becomes a past-due one-shot task; TaskManager runs it on its
        // next tick. The delay is still derived from the projected boundary and the current clock.
        var next = new EventCenterRuntimeBoundaryTask(this, generation);
        _scheduledTask = next;
        return new BoundarySchedulePlan(previous, next, nextBoundaryUtc - Now());
    }

    private void ApplyPlan(BoundarySchedulePlan plan)
    {
        if (plan.Previous != null)
            _taskManager.Cancel(plan.Previous);

        if (plan.Next == null)
            return;

        lock (_scheduleGate)
        {
            if (!IsCurrentLocked(plan.Next))
                return;
        }

        bool scheduled;
        try
        {
            scheduled = _taskManager.Schedule(plan.Next, plan.Delay);
        }
        catch
        {
            ClearIfCurrent(plan.Next);
            throw;
        }

        if (!scheduled)
        {
            ClearIfCurrent(plan.Next);
            return;
        }

        bool stale;
        lock (_scheduleGate)
        {
            stale = !IsCurrentLocked(plan.Next);
        }

        if (stale)
            _taskManager.Cancel(plan.Next);
    }

    private void ClearIfCurrent(EventCenterRuntimeBoundaryTask task)
    {
        lock (_scheduleGate)
        {
            if (ReferenceEquals(_scheduledTask, task))
                _scheduledTask = null;
        }
    }

    private bool IsCurrentLocked(EventCenterRuntimeBoundaryTask task) =>
        ReferenceEquals(_scheduledTask, task) && task.Generation == _scheduleGeneration;

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private sealed record BoundarySchedulePlan(
        EventCenterRuntimeBoundaryTask Previous,
        EventCenterRuntimeBoundaryTask Next,
        TimeSpan Delay);
}
