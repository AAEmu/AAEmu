using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Tasks.Zones;

using NLog;

namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneConflict(ZoneGroup owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // ReSharper disable once NotAccessedField.Local
    private ZoneGroup _owner = owner;

    /// <summary><c>conflict_zone_realtime_schedules</c> rows for this zone, or empty for a
    /// participation-driven zone. Bound once at boot by <see cref="BindSchedule"/>.</summary>
    private IReadOnlyList<ConflictZoneScheduleEntry> _schedule = [];

    public ushort ZoneGroupId { get; set; }
    public int[] NumKills { get; } = new int[5];
    public int[] NoKillMin { get; } = new int[5];
    public int[] NumNpcKills { get; } = new int[5];
    public int[] NumQuestCompletions { get; } = new int[5];

    public int ConflictMin { get; set; }
    public int WarMin { get; set; }
    public int PeaceMin { get; set; }

    public uint PeaceProtectedFactionId { get; set; }
    public uint NuiaReturnPointId { get; set; }
    public uint HariharaReturnPointId { get; set; }
    public uint WarTowerDefId { get; set; }
    public uint PeaceTowerDefId { get; set; } // 10.0.2.13: conflict_zones.peace_tower_def_id present again
    public bool Closed { get; set; } = false;

    public ZoneConflictType CurrentZoneState { get; protected set; } = ZoneConflictType.Tension;
    public DateTime NextStateTime { get; protected set; } = DateTime.MinValue;
    public uint KillCount { get; protected set; }
    public uint NpcKillCount { get; protected set; }
    public uint QuestCompletionCount { get; protected set; }

    /// <summary>
    /// True when this zone's states come from <c>conflict_zone_realtime_schedules</c> rather than
    /// from participation counters. Scheduled zones have no kill thresholds in the shipped data.
    /// </summary>
    public bool IsScheduleDriven => _schedule.Count > 0;

    /// <summary>
    /// Call this function if a PvP kill happens in a zone
    /// </summary>
    public void AddZoneKill(uint NumberOfKills = 1)
    {
        // Ignore when in conflict, war or peace
        if (CurrentZoneState >= ZoneConflictType.Conflict)
            return;

        // Ignore if this zone doesn't have a kill counter mechanic
        if (AllZero(NumKills))
            return;

        var LastState = CurrentZoneState;
        KillCount += NumberOfKills;
        ApplyParticipation();
        if (LastState != CurrentZoneState)
            SendSwitchZoneState();
    }

    /// <summary>
    /// Call this when a listed NPC (<c>conflict_zone_npc_kills.npc_id</c>) dies in the zone.
    /// </summary>
    public void AddNpcKill(uint NumberOfKills = 1)
    {
        if (CurrentZoneState >= ZoneConflictType.Conflict)
            return;

        // Ignore if this zone doesn't count NPC kills toward its state
        if (AllZero(NumNpcKills))
            return;

        var LastState = CurrentZoneState;
        NpcKillCount += NumberOfKills;
        ApplyParticipation();
        if (LastState != CurrentZoneState)
            SendSwitchZoneState();
    }

    /// <summary>
    /// Call this when a listed quest (<c>conflict_zone_quest_completions.context_id</c>) is finished
    /// inside the zone.
    /// </summary>
    public void AddQuestCompletion(uint NumberOfCompletions = 1)
    {
        if (CurrentZoneState >= ZoneConflictType.Conflict)
            return;

        // Ignore if this zone doesn't count quest completions toward its state
        if (AllZero(NumQuestCompletions))
            return;

        var LastState = CurrentZoneState;
        QuestCompletionCount += NumberOfCompletions;
        ApplyParticipation();
        if (LastState != CurrentZoneState)
            SendSwitchZoneState();
    }

    /// <summary>
    /// Highest state reached by any participation counter. Entering Conflict starts the
    /// Conflict → War → Peace timer chain and clears the counters, so the next cycle needs a fresh
    /// round of kills; intermediate steps only clear <see cref="NextStateTime"/>.
    /// </summary>
    private void ApplyParticipation()
    {
        var next = ConflictZoneScheduleRules.AdvanceByParticipation(
            CurrentZoneState,
            KillCount, NumKills,
            NpcKillCount, NumNpcKills,
            QuestCompletionCount, NumQuestCompletions);

        if (next == CurrentZoneState)
            return;

        var previous = CurrentZoneState;
        CurrentZoneState = next;

        if (next == ZoneConflictType.Conflict)
        {
            KillCount = 0;
            NpcKillCount = 0;
            QuestCompletionCount = 0;
            NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
        }
        else
        {
            NextStateTime = DateTime.MinValue;
        }

        Logger.Info($"ZoneGroup {ZoneGroupId} escalated {previous} → {next}");
    }

    private static bool AllZero(int[] values)
    {
        foreach (var value in values)
        {
            if (value != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Binds this zone's <c>conflict_zone_realtime_schedules</c> rows and moves it to the state the
    /// schedule says it is in at <paramref name="nowLocal"/>. Called once at boot; a zone with no
    /// schedule keeps its participation-driven cycle.
    /// </summary>
    public void BindSchedule(IReadOnlyList<ConflictZoneScheduleEntry> schedule, DateTime nowLocal)
    {
        _schedule = schedule ?? [];
        if (_schedule.Count == 0)
            return;

        ApplyScheduledState(nowLocal);
    }

    /// <summary>
    /// Re-resolves a scheduled zone against the wall clock and arms the timer for the next entry.
    /// </summary>
    public void ApplyScheduledState(DateTime nowLocal)
    {
        if (ConflictZoneScheduleRules.Resolve(_schedule, nowLocal) is not { } position)
            return;

        var previous = CurrentZoneState;
        CurrentZoneState = position.State;
        NextStateTime = position.NextChange.ToUniversalTime();

        if (previous != position.State)
            Logger.Info($"ZoneGroup {ZoneGroupId} scheduled transition {previous} → {position.State} (next at {position.NextChange:yyyy-MM-dd HH:mm})");

        SendSwitchZoneState();
    }

    public void SetTimerTask()
    {
        if (NextStateTime > DateTime.MinValue)
        {
            var lpConflictStartTask = new ZoneStateChangeTask(this);
            var delay = NextStateTime - DateTime.UtcNow;
            Logger.Debug($"ZoneGroup {ZoneGroupId}: scheduling next state check in {delay.TotalMinutes:F1} min (NextStateTime={NextStateTime:HH:mm:ss})");
            TaskManager.Instance.Schedule(lpConflictStartTask, delay);
        }
        else
        {
            Logger.Debug($"ZoneGroup {ZoneGroupId}: no NextStateTime set — timer chain stopped.");
        }
    }

    public void SendSwitchZoneState()
    {
        // Schedule the next timer FIRST, before broadcasting to clients.
        // This guarantees the timer chain is preserved even if BroadcastPacketToServer
        // throws (e.g. transient connection issue, packet encode error).
        SetTimerTask();

        try
        {
            WorldManager.Instance.BroadcastPacketToServer(new SCConflictZoneStatePacket(ZoneGroupId, CurrentZoneState, NextStateTime));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"SendSwitchZoneState: Failed to broadcast zone state for ZoneGroup {ZoneGroupId}, State={CurrentZoneState}");
        }

        // Under ZoneAuthority the Zone hosts own NPC spawning, so they need the state to arm the
        // conflict_zone_npc_spawners rows for peace/war. Null in the monolithic server.
        try
        {
            WorldIntegration.RelayConflictZoneStateToZone?.Invoke(ZoneGroupId, (byte)CurrentZoneState);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"SendSwitchZoneState: Failed to relay zone state to zone hosts for ZoneGroup {ZoneGroupId}, State={CurrentZoneState}");
        }
    }

    public void CheckTimer()
    {
        // Scheduled zones derive every state from the wall clock, so the fired timer only means
        // "re-read the schedule"; WarMin/PeaceMin do not apply to them.
        if (IsScheduleDriven)
        {
            Logger.Debug($"ZoneGroup {ZoneGroupId}: scheduled timer elapsed, re-resolving schedule...");
            ApplyScheduledState(DateTime.Now);
            return;
        }

        if (NextStateTime > DateTime.MinValue && DateTime.UtcNow >= NextStateTime)
        {
            Logger.Debug($"ZoneGroup {ZoneGroupId}: timer elapsed, current state={CurrentZoneState}, advancing...");
            ForceNextState();
        }
    }

    public void SetState(ZoneConflictType ct)
    {
        if (ct == CurrentZoneState)
            return;

        var previousState = CurrentZoneState;

        switch (ct)
        {
            case ZoneConflictType.Conflict:
                KillCount = 0;
                NpcKillCount = 0;
                QuestCompletionCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
                break;
            case ZoneConflictType.War:
                KillCount = 0;
                NpcKillCount = 0;
                QuestCompletionCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(WarMin);
                break;
            case ZoneConflictType.Peace:
                KillCount = 0;
                NpcKillCount = 0;
                QuestCompletionCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(PeaceMin);
                break;
            default:
                NextStateTime = DateTime.MinValue;
                break;
        }
        CurrentZoneState = ct;
        Logger.Info($"ZoneGroup {ZoneGroupId} changed from {previousState} → {ct} (next state at {NextStateTime:HH:mm:ss})");
        SendSwitchZoneState();
    }

    public void ForceNextState()
    {
        if (CurrentZoneState < ZoneConflictType.Peace)
        {
            if (CurrentZoneState == ZoneConflictType.War && PeaceMin <= 0)
            {
                SetState(ZoneConflictType.Conflict);
            }
            else
            {
                SetState(CurrentZoneState + 1);
            }
        }
        else
        if (CurrentZoneState >= ZoneConflictType.Peace)
        {
            // If it doesn't have a killcounter, go directly back to conflict (ocean areas)
            if (AllZero(NumKills))
                SetState(ZoneConflictType.Conflict);
            else
                SetState(ZoneConflictType.Tension);
        }
    }
}
