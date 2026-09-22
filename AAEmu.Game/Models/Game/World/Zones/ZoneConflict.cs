using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Tasks.Zones;

using NLog;

namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneConflict(
    ZoneGroup owner,
    Action<ushort, ZoneConflictType, ZoneConflictType> stateChanged = null,
    Action<ConflictZoneRuntimeState> persist = null,
    Action<DateTime> scheduleOverride = null)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan PersistenceRetryDelay = TimeSpan.FromSeconds(5);

    private readonly object _transitionLock = new();
    private readonly object _stateLock = new();
    // ReSharper disable once NotAccessedField.Local
    private ZoneGroup _owner = owner;
    private readonly Action<ushort, ZoneConflictType, ZoneConflictType> _stateChanged = stateChanged;
    private readonly Action<ConflictZoneRuntimeState> _persist = persist;
    private readonly Action<DateTime> _scheduleOverride = scheduleOverride;
    private ZoneConflictType _currentZoneState = ZoneConflictType.Tension;
    private DateTime _nextStateTime = DateTime.MinValue;

    /// <summary><c>conflict_zone_realtime_schedules</c> rows for this zone, or empty for a
    /// participation-driven zone. Bound once at boot by <see cref="BindSchedule"/>.</summary>
    private IReadOnlyList<ConflictZoneScheduleEntry> _schedule = [];
    private IReadOnlyList<ConflictZoneDailyWarStart> _dailyWarStarts = [];
    private DateTime _scheduledStateTime = DateTime.MinValue;
    public ushort ZoneGroupId { get; set; }
    public int[] NumKills { get; } = new int[5];
    public int[] NoKillMin { get; } = new int[5];
    public int[] NumNpcKills { get; } = new int[5];
    public int[] NumQuestCompletions { get; } = new int[5];
    public ConflictZoneDailyWarStart[] DailyWarStarts { get; } =
        Enumerable.Repeat(new ConflictZoneDailyWarStart(-1, 0), 12).ToArray();

    public int ConflictMin { get; set; }
    public int WarMin { get; set; }
    public int PeaceMin { get; set; }

    public uint PeaceProtectedFactionId { get; set; }
    public uint NuiaReturnPointId { get; set; }
    public uint HariharaReturnPointId { get; set; }
    public uint WarTowerDefId { get; set; }
    public uint PeaceTowerDefId { get; set; } // 10.0.2.13: conflict_zones.peace_tower_def_id present again
    public bool Closed { get; set; } = false;

    public ZoneConflictType CurrentZoneState
    {
        get
        {
            lock (_stateLock)
                return _currentZoneState;
        }
        protected set
        {
            lock (_stateLock)
                _currentZoneState = value;
        }
    }

    public DateTime NextStateTime
    {
        get
        {
            lock (_stateLock)
                return _nextStateTime;
        }
        protected set
        {
            lock (_stateLock)
                _nextStateTime = value;
        }
    }

    public uint KillCount { get; protected set; }
    public uint NpcKillCount { get; protected set; }
    public uint QuestCompletionCount { get; protected set; }

    /// <summary>
    /// True when this zone's states come from <c>conflict_zone_realtime_schedules</c> rather than
    /// from participation counters. Scheduled zones have no kill thresholds in the shipped data.
    /// </summary>
    public bool IsScheduleDriven
    {
        get
        {
            lock (_stateLock)
                return _schedule.Count > 0 || _dailyWarStarts.Count > 0;
        }
    }

    /// <summary>
    /// Call this when a player kills a non-friendly player in the zone (<see cref="ConflictZoneEscalationRules.CountsPvpKill"/>).
    /// Counts only in the trouble states of an unscheduled zone that has <c>num_kills_N</c> thresholds.
    /// </summary>
    public void AddZoneKill(uint NumberOfKills = 1)
    {
        lock (_transitionLock)
        {
            StatePublication? publication;
            ConflictZoneRuntimeState before;
            lock (_stateLock)
            {
                if (!ConflictZoneEscalationRules.AcceptsParticipation(_currentZoneState, IsScheduleDrivenLocked(), NumKills))
                    return;

                before = CaptureRuntimeStateLocked();
                var previousState = _currentZoneState;
                KillCount += NumberOfKills;
                publication = ApplyParticipationLocked(previousState);
            }

            if (!TryPersist(before))
                return;

            if (publication.HasValue)
                PublishState(publication.Value);
        }
    }

    /// <summary>
    /// Call this when a listed NPC (<c>conflict_zone_npc_kills.npc_id</c>) dies in the zone.
    /// </summary>
    public void AddNpcKill(uint NumberOfKills = 1)
    {
        lock (_transitionLock)
        {
            StatePublication? publication;
            ConflictZoneRuntimeState before;
            lock (_stateLock)
            {
                if (!ConflictZoneEscalationRules.AcceptsParticipation(_currentZoneState, IsScheduleDrivenLocked(), NumNpcKills))
                    return;

                before = CaptureRuntimeStateLocked();
                var previousState = _currentZoneState;
                NpcKillCount += NumberOfKills;
                publication = ApplyParticipationLocked(previousState);
            }

            if (!TryPersist(before))
                return;

            if (publication.HasValue)
                PublishState(publication.Value);
        }
    }

    /// <summary>
    /// Call this when a listed quest (<c>conflict_zone_quest_completions.context_id</c>) is finished
    /// inside the zone.
    /// </summary>
    public void AddQuestCompletion(uint NumberOfCompletions = 1)
    {
        lock (_transitionLock)
        {
            StatePublication? publication;
            ConflictZoneRuntimeState before;
            lock (_stateLock)
            {
                if (!ConflictZoneEscalationRules.AcceptsParticipation(_currentZoneState, IsScheduleDrivenLocked(), NumQuestCompletions))
                    return;

                before = CaptureRuntimeStateLocked();
                var previousState = _currentZoneState;
                QuestCompletionCount += NumberOfCompletions;
                publication = ApplyParticipationLocked(previousState);
            }

            if (!TryPersist(before))
                return;

            if (publication.HasValue)
                PublishState(publication.Value);
        }
    }

    /// <summary>
    /// Highest state reached by any participation counter. Entering Conflict starts the
    /// Conflict → War → Peace timer chain and clears the counters, so the next cycle needs a fresh
    /// round of kills; intermediate steps only clear <see cref="NextStateTime"/>.
    /// </summary>
    private StatePublication? ApplyParticipationLocked(ZoneConflictType previousState)
    {
        var next = ConflictZoneEscalationRules.AdvanceByParticipation(
            _currentZoneState,
            KillCount, NumKills,
            NpcKillCount, NumNpcKills,
            QuestCompletionCount, NumQuestCompletions);

        if (next == _currentZoneState)
            return null;

        _currentZoneState = next;

        if (next == ZoneConflictType.Conflict)
        {
            ResetParticipationCounters();
            _nextStateTime = DateTime.UtcNow.AddMinutes(
                ConflictZoneEscalationRules.TimedStateMinutes(next, ConflictMin, WarMin, PeaceMin));
        }
        else
        {
            _nextStateTime = DateTime.MinValue;
        }

        Logger.Info($"ZoneGroup {ZoneGroupId} escalated {previousState} → {next}");
        return new StatePublication(previousState, next, _nextStateTime, true);
    }

    /// <summary>
    /// Binds this zone's <c>conflict_zone_realtime_schedules</c> rows and moves it to the state the
    /// schedule says it is in at <paramref name="nowLocal"/>. Called once at boot; a zone with no
    /// schedule keeps its participation-driven cycle.
    /// </summary>
    public void BindSchedule(IReadOnlyList<ConflictZoneScheduleEntry> schedule, DateTime nowLocal)
    {
        lock (_transitionLock)
        {
            lock (_stateLock)
                _schedule = schedule ?? [];
            if (_schedule.Count == 0)
                return;

            ApplyScheduledStateLocked(nowLocal);
        }
    }

    public bool BindDailyWarWindows(IReadOnlyList<ConflictZoneDailyWarStart> starts, DateTime nowLocal)
    {
        lock (_transitionLock)
        {
            var candidate = starts?.Where(x => ConflictZoneScheduleRules.DecodeDailyWarStart(x).HasValue).ToArray() ?? [];
            if (ConflictZoneScheduleRules.ResolveDailyWarWindow(
                    candidate, ConflictMin, WarMin, PeaceMin, nowLocal) is null)
            {
                Logger.Warn("ZoneGroup {0}: ignored invalid daily conflict-zone war window", ZoneGroupId);
                return false;
            }

            lock (_stateLock)
                _dailyWarStarts = candidate;

            ApplyScheduledStateLocked(nowLocal);
            return true;
        }
    }

    /// <summary>
    /// Re-resolves a scheduled zone against the wall clock and arms the timer for the next entry.
    /// </summary>
    public void ApplyScheduledState(DateTime nowLocal)
    {
        lock (_transitionLock)
            ApplyScheduledStateLocked(nowLocal);
    }

    private void ApplyScheduledStateLocked(DateTime nowLocal)
    {
        StatePublication publication;
        DateTime nextChangeLocal;
        lock (_stateLock)
        {
            var position = _schedule.Count > 0
                ? ConflictZoneScheduleRules.Resolve(_schedule, nowLocal)
                : ConflictZoneScheduleRules.ResolveDailyWarWindow(
                    _dailyWarStarts, ConflictMin, WarMin, PeaceMin, nowLocal);
            if (position is not { } resolved)
                return;

            var previousState = _currentZoneState;
            _currentZoneState = resolved.State;
            _nextStateTime = resolved.NextChange.ToUniversalTime();
            nextChangeLocal = resolved.NextChange;
            publication = new StatePublication(
                previousState,
                _currentZoneState,
                _nextStateTime,
                previousState != _currentZoneState);
        }

        // Deliberately not persisted: a schedule-driven zone recomputes its state and its next
        // transition from the wall clock at boot, and StartConflictCycles never reads its row back,
        // so the transition must not wait on the store.
        if (publication.StateChanged)
        {
            Logger.Info(
                $"ZoneGroup {ZoneGroupId} scheduled transition {publication.PreviousState} → {publication.CurrentState} " +
                $"(next at {nextChangeLocal:yyyy-MM-dd HH:mm})");
        }

        PublishState(publication);
    }

    public void SetTimerTask()
    {
        lock (_transitionLock)
        {
            DateTime nextStateTime;
            lock (_stateLock)
                nextStateTime = _nextStateTime;
            ScheduleNextState(nextStateTime);
        }
    }

    private void ScheduleNextState(DateTime nextStateTime)
    {
        lock (_stateLock)
        {
            if (nextStateTime <= DateTime.MinValue)
            {
                _scheduledStateTime = DateTime.MinValue;
                Logger.Debug($"ZoneGroup {ZoneGroupId}: no NextStateTime set - timer chain stopped.");
                return;
            }

            if (_scheduledStateTime == nextStateTime)
                return;
            _scheduledStateTime = nextStateTime;
        }

        var task = new ZoneStateChangeTask(this);
        var delay = nextStateTime - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        Logger.Debug(
            $"ZoneGroup {ZoneGroupId}: scheduling next state check in {delay.TotalMinutes:F1} min " +
            $"(NextStateTime={nextStateTime:HH:mm:ss})");
        if (_scheduleOverride != null)
        {
            _scheduleOverride(nextStateTime);
            return;
        }
        try
        {
            if (TaskManager.Instance.Schedule(task, delay))
                return;
            Logger.Error("ZoneGroup {0}: failed to schedule the next state check.", ZoneGroupId);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "ZoneGroup {0}: failed to schedule the next state check.", ZoneGroupId);
        }

        lock (_stateLock)
        {
            if (_scheduledStateTime == nextStateTime)
                _scheduledStateTime = DateTime.MinValue;
        }
    }

    public void SendSwitchZoneState()
    {
        lock (_transitionLock)
        {
            StatePublication publication;
            lock (_stateLock)
            {
                publication = new StatePublication(
                    _currentZoneState,
                    _currentZoneState,
                    _nextStateTime,
                    false);
            }
            PublishState(publication);
        }
    }

    private void PublishState(StatePublication publication)
    {
        // Preserve the timer chain even when a listener, packet broadcast, or zone relay fails.
        ScheduleNextState(publication.NextStateTime);

        if (publication.StateChanged)
            NotifyStateChanged(publication.PreviousState, publication.CurrentState);

        try
        {
            WorldManager.Instance.BroadcastPacketToServer(
                new SCConflictZoneStatePacket(ZoneGroupId, publication.CurrentState, publication.NextStateTime));
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "Failed to broadcast zone state for ZoneGroup {0}, State={1}",
                ZoneGroupId,
                publication.CurrentState);
        }

        // Under ZoneAuthority the Zone hosts own NPC spawning, so they need the state to arm the
        // conflict_zone_npc_spawners rows for peace/war. Null in the monolithic server.
        try
        {
            WorldIntegration.RelayConflictZoneStateToZone?.Invoke(ZoneGroupId, (byte)publication.CurrentState);
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "Failed to relay zone state to zone hosts for ZoneGroup {0}, State={1}",
                ZoneGroupId,
                publication.CurrentState);
        }
    }

    public void CheckTimer()
    {
        lock (_transitionLock)
        {
            bool scheduleDriven;
            StatePublication? publication = null;
            ConflictZoneRuntimeState before = default;
            lock (_stateLock)
            {
                if (_nextStateTime <= DateTime.MinValue || DateTime.UtcNow < _nextStateTime)
                    return;

                _scheduledStateTime = DateTime.MinValue;
                scheduleDriven = IsScheduleDrivenLocked();
                if (!scheduleDriven)
                {
                    before = CaptureRuntimeStateLocked();
                    Logger.Debug(
                        $"ZoneGroup {ZoneGroupId}: timer elapsed, current state={_currentZoneState}, advancing...");
                    publication = SetStateLocked(GetNextStateLocked());
                }
            }

            if (scheduleDriven)
            {
                Logger.Debug($"ZoneGroup {ZoneGroupId}: scheduled timer elapsed, re-resolving schedule...");
                ApplyScheduledStateLocked(DateTime.Now);
            }
            else if (!TryPersist(before))
            {
                ScheduleNextState(DateTime.UtcNow.Add(PersistenceRetryDelay));
            }
            else if (publication.HasValue)
            {
                PublishState(publication.Value);
            }
        }
    }

    public void SetState(ZoneConflictType state)
    {
        lock (_transitionLock)
        {
            StatePublication? publication;
            ConflictZoneRuntimeState before;
            lock (_stateLock)
            {
                before = CaptureRuntimeStateLocked();
                publication = SetStateLocked(state);
            }

            if (publication.HasValue && !TryPersist(before))
                publication = null;

            if (publication.HasValue)
                PublishState(publication.Value);
        }
    }

    private StatePublication? SetStateLocked(ZoneConflictType state)
    {
        if (state == _currentZoneState)
            return null;

        var previousState = _currentZoneState;
        if (ConflictZoneEscalationRules.IsTroubleState(state))
        {
            _nextStateTime = DateTime.MinValue;
        }
        else
        {
            // Entering a timed state closes the participation round; the next cycle needs a fresh
            // count. A 0-minute state (peace_min 0) arms an already-due timer and moves straight on.
            ResetParticipationCounters();
            _nextStateTime = DateTime.UtcNow.AddMinutes(
                ConflictZoneEscalationRules.TimedStateMinutes(state, ConflictMin, WarMin, PeaceMin));
        }

        _currentZoneState = state;
        Logger.Info(
            $"ZoneGroup {ZoneGroupId} changed from {previousState} → {state} " +
            $"(next state at {_nextStateTime:HH:mm:ss})");
        return new StatePublication(previousState, state, _nextStateTime, true);
    }

    private void ResetParticipationCounters()
    {
        KillCount = 0;
        NpcKillCount = 0;
        QuestCompletionCount = 0;
    }

    private void NotifyStateChanged(ZoneConflictType previousState, ZoneConflictType currentState)
    {
        try
        {
            _stateChanged?.Invoke(ZoneGroupId, previousState, currentState);
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                "ZoneGroup {0}: state-change callback failed for {1} -> {2}",
                ZoneGroupId,
                previousState,
                currentState);
        }
    }

    public void ForceNextState()
    {
        lock (_transitionLock)
        {
            StatePublication? publication;
            ConflictZoneRuntimeState before;
            lock (_stateLock)
            {
                before = CaptureRuntimeStateLocked();
                publication = SetStateLocked(GetNextStateLocked());
            }

            if (publication.HasValue && !TryPersist(before))
                publication = null;

            if (publication.HasValue)
                PublishState(publication.Value);
        }
    }

    private ZoneConflictType GetNextStateLocked()
    {
        var hasParticipationCounters = ConflictZoneEscalationRules.HasThresholds(NumKills) ||
                                       ConflictZoneEscalationRules.HasThresholds(NumNpcKills) ||
                                       ConflictZoneEscalationRules.HasThresholds(NumQuestCompletions);
        return ConflictZoneEscalationRules.NextTimedState(_currentZoneState, hasParticipationCounters, PeaceMin);
    }

    private bool IsScheduleDrivenLocked() => _schedule.Count > 0 || _dailyWarStarts.Count > 0;

    private bool HasParticipationCountersLocked() =>
        ConflictZoneEscalationRules.HasThresholds(NumKills) ||
        ConflictZoneEscalationRules.HasThresholds(NumNpcKills) ||
        ConflictZoneEscalationRules.HasThresholds(NumQuestCompletions);

    public ConflictZoneRuntimeState CaptureRuntimeState()
    {
        lock (_stateLock)
            return CaptureRuntimeStateLocked();
    }

    public void RestoreRuntimeState(ConflictZoneRuntimeState state, DateTime nowUtc)
    {
        if (state.ZoneGroupId != ZoneGroupId || state.State is < ZoneConflictType.Tension or > ZoneConflictType.Peace)
            return;

        lock (_transitionLock)
        {
            StatePublication publication;
            ConflictZoneRuntimeState persisted;
            lock (_stateLock)
            {
                var previous = _currentZoneState;
                ApplyRuntimeStateLocked(state);
                while (_nextStateTime > DateTime.MinValue && _nextStateTime <= nowUtc)
                {
                    if (_currentZoneState == ZoneConflictType.Conflict && !HasParticipationCountersLocked())
                    {
                        var cycleMinutes = ConflictMin + WarMin + Math.Max(0, PeaceMin);
                        if (cycleMinutes <= 0)
                        {
                            _nextStateTime = DateTime.MinValue;
                            break;
                        }

                        var completedCycles = (long)((nowUtc - _nextStateTime).TotalMinutes / cycleMinutes);
                        if (completedCycles > 0)
                            _nextStateTime = _nextStateTime.AddMinutes(completedCycles * cycleMinutes);
                    }

                    var boundary = _nextStateTime;
                    var next = GetNextStateLocked();
                    _currentZoneState = next;
                    ResetParticipationCounters();
                    var duration = ConflictZoneEscalationRules.TimedStateMinutes(next, ConflictMin, WarMin, PeaceMin);
                    _nextStateTime = ConflictZoneEscalationRules.IsTroubleState(next)
                        ? DateTime.MinValue
                        : boundary.AddMinutes(duration);
                }

                _scheduledStateTime = DateTime.MinValue;
                persisted = CaptureRuntimeStateLocked();
                publication = new StatePublication(previous, _currentZoneState, _nextStateTime, false);
            }

            _persist?.Invoke(persisted);
            PublishState(publication);
        }
    }

    private ConflictZoneRuntimeState CaptureRuntimeStateLocked() => new(
        ZoneGroupId, _currentZoneState, KillCount, NpcKillCount, QuestCompletionCount, _nextStateTime);

    private void ApplyRuntimeStateLocked(ConflictZoneRuntimeState state)
    {
        _currentZoneState = state.State;
        KillCount = state.KillCount;
        NpcKillCount = state.NpcKillCount;
        QuestCompletionCount = state.QuestCompletionCount;
        _nextStateTime = state.NextStateTimeUtc;
    }

    /// <summary>
    /// Writes the state the caller just mutated. Called with <see cref="_transitionLock"/> held but
    /// never with <see cref="_stateLock"/>: the combat and skill readers that take
    /// <see cref="_stateLock"/> must not wait on a slow or unreachable store. A failed write rolls the
    /// in-memory mutation back to <paramref name="before"/>.
    /// </summary>
    private bool TryPersist(ConflictZoneRuntimeState before)
    {
        if (_persist == null)
            return true;

        ConflictZoneRuntimeState state;
        lock (_stateLock)
            state = CaptureRuntimeStateLocked();

        try
        {
            _persist(state);
            return true;
        }
        catch (Exception exception)
        {
            lock (_stateLock)
                ApplyRuntimeStateLocked(before);
            Logger.Error(exception, "ZoneGroup {0}: runtime state persistence failed; mutation rolled back", ZoneGroupId);
            return false;
        }
    }

    private readonly record struct StatePublication(
        ZoneConflictType PreviousState,
        ZoneConflictType CurrentState,
        DateTime NextStateTime,
        bool StateChanged);
}
