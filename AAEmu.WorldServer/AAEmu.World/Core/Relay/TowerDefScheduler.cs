using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using System.Threading;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Schedule authority for <c>tower_defs</c> under ZoneAuthority.
/// </summary>
/// <remarks>
/// Zone holds spot playability and executes spawner arm/disarm on WZ. World owns the schedule,
/// progression clock (timed or kill-quota), and client banners (<c>SCTowerDef*</c>).
/// Only zones that report playable spots for a tower receive Start/Wave/End; SC fires only when
/// at least one such host exists (prevents twin-continent false announces).
/// Kill-switch: <c>AAEMU_DISABLE_GAME_TIME_TOWERS=1</c> skips Game-Time arms only.
/// </remarks>
public static class TowerDefScheduler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object Sync = new();

    /// <summary>
    /// Live run. Kill quota for the step just opened, if any, must be met before the next step.
    /// </summary>
    private sealed class RunState
    {
        public DateTime Deadline;
        public bool Manual;
        public bool FromGameTime;
        /// <summary>Next prog index to open with <c>WZTowerDefWaveStart</c>.</summary>
        public int NextStep;
        public DateTime NextWaveAt;
        public uint AnnounceZoneId;
        public ushort AnnounceZoneGroupId;
        /// <summary>Zone keys that accepted Start (playable spots &gt; 0).</summary>
        public List<uint> HostZoneIds = [];
        /// <summary>Spot index chosen per host zone id for this run.</summary>
        public Dictionary<uint, uint> SpotByZone = [];
        /// <summary>prog index currently waiting on kill targets (-1 = not waiting).</summary>
        public int KillWaitStep = -1;
        public Dictionary<uint, int> KillRemaining;
        /// <summary>Last opened wave index, or -1 before any WaveStart (matches client map curStep).</summary>
        public int CurrentStep = -1;
        /// <summary>When set, start <see cref="PendingFollowOnId"/> at this UTC time.</summary>
        public DateTime FollowOnAt = DateTime.MaxValue;
        public uint PendingFollowOnId;
        /// <summary>Incremented on each Start; captured when a follow-on delay is armed.</summary>
        public ulong Generation;
        public ulong FollowOnGeneration;
    }

    private static readonly Dictionary<uint, RunState> Running = [];
    /// <summary>Playable spot counts reported by zones: towerDefId → (zoneId → count).</summary>
    private static readonly Dictionary<uint, Dictionary<uint, uint>> Playability = [];
    private static bool _primed;
    private static ulong _nextRunGeneration = 1;

    private static ulong NextRunGeneration() => Interlocked.Increment(ref _nextRunGeneration);

    public static int RunningCount { get { lock (Sync) return Running.Count; } }

    private static bool GameTimeTowersDisabled =>
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_GAME_TIME_TOWERS") == "1";

    /// <summary>
    /// Wall-clock (UTC) windows. Called from the schedule gate tick.
    /// </summary>
    public static void Tick()
    {
        lock (Sync)
        {
            var now = DateTime.UtcNow;

            if (!_primed)
            {
                _primed = true;
                var wall = TowerDefGameData.Instance.GetScheduledTowerDefs().Count();
                var game = TowerDefGameData.Instance.GetGameTimeScheduledTowerDefs().Count();
                Logger.Info(
                    "TowerDefScheduler armed — {0} UTC wall-slot events, {1} Game-Time (tod) rifts (zone-auth WZ arm)",
                    wall, game);
            }

            foreach (var towerDef in TowerDefGameData.Instance.GetScheduledTowerDefs())
            {
                var shouldRun = towerDef.IsWithinWindow(now);
                if (!Running.TryGetValue(towerDef.Id, out var state))
                {
                    if (shouldRun)
                        Start(towerDef, now, manual: false, fromGameTime: false, "utc schedule");
                    continue;
                }

                if (!shouldRun && !state.Manual && !state.FromGameTime)
                    End(towerDef, "utc window closed");
            }

            AdvanceTimedWaves(now);
            AdvancePendingFollowOns(now);

            foreach (var (id, state) in Running.ToList())
            {
                if (now < state.Deadline)
                    continue;
                var towerDef = TowerDefGameData.Instance.GetTowerDef(id);
                if (towerDef != null)
                    End(towerDef, "force_end_time reached");
                else
                    Running.Remove(id);
            }
        }
    }

    /// <summary>
    /// Game-hour advanced. Arms Game-Time rifts when the hour crosses <c>tod</c>.
    /// </summary>
    public static void OnGameTimeAdvanced(float oldHour, float newHour)
    {
        if (GameTimeTowersDisabled)
            return;

        if (TimeManager.IsLargeGameHourJump(oldHour, newHour))
        {
            Logger.Warn(
                "OnGameTimeAdvanced skipped tower arms for large jump {0:F2}→{1:F2} — use /towerdef start",
                oldHour, newHour);
            return;
        }

        lock (Sync)
        {
            var now = DateTime.UtcNow;
            if (!_primed)
            {
                _primed = true;
                var wall = TowerDefGameData.Instance.GetScheduledTowerDefs().Count();
                var game = TowerDefGameData.Instance.GetGameTimeScheduledTowerDefs().Count();
                Logger.Info(
                    "TowerDefScheduler armed — {0} UTC wall-slot events, {1} Game-Time (tod) rifts (zone-auth WZ arm)",
                    wall, game);
            }

            // Earlier TimeOfDay first so a shared portal keeps the first owner.
            foreach (var towerDef in TowerDefGameData.Instance.GetGameTimeScheduledTowerDefs()
                         .OrderBy(t => t.TimeOfDay)
                         .ThenBy(t => t.Id))
            {
                if (!towerDef.CrossedGameStartHour(oldHour, newHour))
                    continue;
                if (Running.ContainsKey(towerDef.Id))
                    continue;

                Start(
                    towerDef,
                    now,
                    manual: false,
                    fromGameTime: true,
                    $"game ToD {oldHour:F2}→{newHour:F2} crossed tod={towerDef.TimeOfDay:F2}");
            }
        }
    }

    /// <summary>Drop playability cache rows for a disconnected zone.</summary>
    public static void OnZoneDisconnected(uint zoneId)
    {
        if (zoneId == 0)
            return;
        lock (Sync)
        {
            foreach (var byZone in Playability.Values)
                byZone.Remove(zoneId);
        }
    }

    /// <summary>
    /// After a dedicate reaches ZoneLoaded, refresh playability for scheduled tower defs.
    /// </summary>
    public static void OnZoneLoaded(ZoneConnection zone)
    {
        if (zone.State < ZoneConnectionState.ZoneLoaded)
            return;
        if (!TowerDefGameData.Instance.IsLoaded)
            return;

        QueryPlayabilityForZone(zone);
    }

    /// <summary>
    /// ZWTowerDefReportPlayability: store playableSpotCount for later host selection.
    /// </summary>
    public static void OnPlayabilityReport(uint zoneId, uint towerDefId, ushort zoneGroupType2, uint playableSpots)
    {
        lock (Sync)
        {
            if (!Playability.TryGetValue(towerDefId, out var byZone))
            {
                byZone = [];
                Playability[towerDefId] = byZone;
            }

            byZone[zoneId] = playableSpots;
            Logger.Info(
                "ZWTowerDefReportPlayability zoneId={0} towerDef={1} type2={2} playableSpots={3}",
                zoneId, towerDefId, zoneGroupType2, playableSpots);
        }
    }

    /// <summary>
    /// Zone (or World combat) killed an NPC — advance kill-gated tower steps.
    /// </summary>
    public static void OnNpcKilled(uint templateId)
    {
        if (templateId == 0)
            return;

        lock (Sync)
        {
            var now = DateTime.UtcNow;
            foreach (var (id, state) in Running.ToList())
            {
                if (state.KillWaitStep < 0 || state.KillRemaining == null || state.KillRemaining.Count == 0)
                    continue;
                if (!state.KillRemaining.TryGetValue(templateId, out var left) || left <= 0)
                    continue;

                left--;
                if (left <= 0)
                    state.KillRemaining.Remove(templateId);
                else
                    state.KillRemaining[templateId] = left;

                if (state.KillRemaining.Count > 0)
                {
                    Running[id] = state;
                    continue;
                }

                Logger.Info(
                    "TowerDef {0} step {1} kill quotas met — scheduling next wave",
                    id, state.KillWaitStep);
                state.KillWaitStep = -1;
                state.KillRemaining = null;
                state.NextWaveAt = now;
                Running[id] = state;
            }

            AdvanceTimedWaves(now);
        }
    }

    /// <summary>Fires an event immediately, ignoring its schedule. Used by the GM trigger.</summary>
    public static bool ForceStart(uint towerDefId)
    {
        lock (Sync)
        {
            var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (towerDef == null)
                return false;

            // Restart path: leftover Running / portal liveCount left users with banner-only Start.
            if (Running.ContainsKey(towerDefId))
            {
                Logger.Info("ForceStart tower={0}: ending live run first (manual restart)", towerDefId);
                End(towerDef, "manual restart");
            }

            // Manual takeover of a shared portal (e.g. end expand thrash, re-run base).
            if (towerDef.TargetNpcSpawnId != 0)
            {
                foreach (var (otherId, _) in Running.ToList())
                {
                    if (otherId == towerDefId)
                        continue;
                    var other = TowerDefGameData.Instance.GetTowerDef(otherId);
                    if (other == null || other.TargetNpcSpawnId != towerDef.TargetNpcSpawnId)
                        continue;
                    Logger.Info(
                        "ForceStart tower={0}: ending conflicting owner tower={1} on portal sType={2}",
                        towerDefId, otherId, towerDef.TargetNpcSpawnId);
                    End(other, "manual portal reclaim");
                }
            }

            Start(towerDef, DateTime.UtcNow, manual: true, fromGameTime: false, "manual trigger");
            return true;
        }
    }

    /// <summary>Ends a running event immediately. Used by the GM trigger.</summary>
    public static bool ForceEnd(uint towerDefId)
    {
        lock (Sync)
        {
            var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (towerDef == null)
                return false;

            // Allow cleanup even if schedule mark is gone (orphan army after thrash).
            if (!Running.ContainsKey(towerDefId))
            {
                var state = new RunState
                {
                    AnnounceZoneId = FirstLoadedZoneId(),
                    AnnounceZoneGroupId = FirstLoadedZoneGroup()
                };
                foreach (var (zone, spots, group) in EnumeratePlayableHosts(towerDefId, manualFallbackAll: true))
                {
                    state.HostZoneIds.Add(zone.ZoneId);
                    state.SpotByZone[zone.ZoneId] = 0;
                    state.AnnounceZoneId = zone.ZoneId;
                    state.AnnounceZoneGroupId = group;
                }

                Running[towerDefId] = state;
            }

            End(towerDef, "manual trigger");
            return true;
        }
    }

    /// <summary>Advances a running event to an explicit progression step. Used by the GM trigger.</summary>
    public static bool ForceWave(uint towerDefId, uint step)
    {
        lock (Sync)
        {
            var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (towerDef == null)
                return false;

            var now = DateTime.UtcNow;
            if (!Running.TryGetValue(towerDefId, out var state))
            {
                state = new RunState
                {
                    AnnounceZoneId = FirstLoadedZoneId(),
                    AnnounceZoneGroupId = FirstLoadedZoneGroup(),
                    // GM wave without a prior Start must not be culled by WallClock
                    // ("utc window closed" ~15s later) or an unset Deadline (instant force_end).
                    Manual = true,
                    Deadline = now + towerDef.Duration,
                    KillWaitStep = -1,
                    Generation = NextRunGeneration()
                };
                // Host all currently playable zones for an ad-hoc wave.
                foreach (var (zone, spots, group) in EnumeratePlayableHosts(towerDefId, manualFallbackAll: true))
                {
                    state.HostZoneIds.Add(zone.ZoneId);
                    state.SpotByZone[zone.ZoneId] = PickSpot(spots);
                    state.AnnounceZoneId = zone.ZoneId;
                    state.AnnounceZoneGroupId = group;
                }
            }
            else
            {
                // Keep a GM-advanced run alive outside its schedule window.
                state.Manual = true;
                if (state.Deadline < now)
                    state.Deadline = now + towerDef.Duration;
            }

            OpenStep(towerDef, state, (int)step, "manual wave");
            state.NextStep = (int)step + 1;
            ArmPostStep(towerDef, state, (int)step, now);
            if (Running.ContainsKey(towerDefId) || state.HostZoneIds.Count > 0)
                Running[towerDefId] = state;
            return true;
        }
    }

    private static void Start(TowerDef towerDef, DateTime now, bool manual, bool fromGameTime, string reason)
    {
        // Soft refresh: query any loaded zone that has not yet reported for this def.
        foreach (var zone in LoadedZones())
        {
            if (!HasPlayabilityReport(towerDef.Id, zone.ZoneId))
                QueryPlayability(zone, towerDef.Id);
        }

        // Auto-arm must not start a second event on a portal spawner another run already owns.
        // Identity is target_npc_spawner_id (loaded relationship). Manual ForceStart reclaims first.
        if (!manual &&
            towerDef.TargetNpcSpawnId != 0 &&
            TryFindRunningPortalOwner(towerDef.TargetNpcSpawnId, out var ownerId) &&
            ownerId != towerDef.Id)
        {
            Logger.Info(
                "TowerDef {0} skipped Start — portal sType={1} already owned by tower={2} ({3}) — {4}",
                towerDef.Id, towerDef.TargetNpcSpawnId, ownerId, reason, towerDef.Name);
            return;
        }

        var hosts = EnumeratePlayableHosts(towerDef.Id, manualFallbackAll: manual).ToList();
        if (hosts.Count == 0)
        {
            Logger.Warn(
                "TowerDef {0} skipped Start — no zone with playable spots (spawner={1}) — {2} ({3})",
                towerDef.Id, towerDef.TargetNpcSpawnId, reason, towerDef.Name);
            return;
        }

        var firstWaveDelay = towerDef.FirstWaveAfter > 0f
            ? TimeSpan.FromSeconds(towerDef.FirstWaveAfter)
            : TimeSpan.Zero;

        var announceZoneId = hosts[0].Zone.ZoneId;
        var announceGroup = hosts[0].Group;

        var state = new RunState
        {
            Deadline = now + towerDef.Duration,
            Manual = manual,
            FromGameTime = fromGameTime,
            NextStep = 0,
            NextWaveAt = now + firstWaveDelay,
            AnnounceZoneId = announceZoneId,
            AnnounceZoneGroupId = announceGroup,
            KillWaitStep = -1,
            Generation = NextRunGeneration()
        };

        foreach (var (zone, spots, group) in hosts)
        {
            var spotIdx = PickSpot(spots);
            state.HostZoneIds.Add(zone.ZoneId);
            state.SpotByZone[zone.ZoneId] = spotIdx;
            zone.SendPacket(new WZTowerDefStartPacket(
                (int)towerDef.Id, (short)group, spotIdx));
            Logger.Info(
                "WZTowerDefStart → zoneId={0} group={1} towerDef={2} spot={3}/{4}",
                zone.ZoneId, group, towerDef.Id, spotIdx, spots);
        }

        // Prefer the first host as SC eventKey (real host zone + group, not "any loaded").
        state.AnnounceZoneId = announceZoneId;
        state.AnnounceZoneGroupId = announceGroup;
        Running[towerDef.Id] = state;

        Logger.Info(
            "WZTowerDefStart hosts={0}: towerDef={1} spawner={2} for {3} ({4}) — {5}",
            hosts.Count, towerDef.Id, towerDef.TargetNpcSpawnId, towerDef.Duration, reason, towerDef.Name);

        BroadcastScStart(towerDef, state);

        // Retail: dedic Start OnEvent → ZW. Optional portal re-OnEvent helps silent re-arms
        // (maxPop / stuck template after thrash). Never World-mesh author.
        TowerDefWaveForce.ArmPortalTargets(towerDef, state.HostZoneIds);

        if (firstWaveDelay <= TimeSpan.Zero)
            AdvanceTimedWaves(now);
    }

    /// <summary>True if any running tower owns this portal spawner type.</summary>
    private static bool TryFindRunningPortalOwner(uint portalSpawnerType, out uint towerDefId)
    {
        foreach (var id in Running.Keys)
        {
            var def = TowerDefGameData.Instance.GetTowerDef(id);
            if (def == null || def.TargetNpcSpawnId != portalSpawnerType)
                continue;
            towerDefId = id;
            return true;
        }

        towerDefId = 0;
        return false;
    }

    private static void AdvanceTimedWaves(DateTime now)
    {
        const int MaxStepsPerTick = 4;

        foreach (var (id, state) in Running.ToList())
        {
            if (state.KillWaitStep >= 0)
                continue; // wait for OnNpcKilled

            var towerDef = TowerDefGameData.Instance.GetTowerDef(id);
            if (towerDef?.Progs == null || towerDef.Progs.Count == 0)
                continue;

            var stepsThisTick = 0;
            while (state.NextStep < towerDef.Progs.Count &&
                   now >= state.NextWaveAt &&
                   stepsThisTick < MaxStepsPerTick)
            {
                var step = state.NextStep;
                OpenStep(towerDef, state, step, "schedule");
                stepsThisTick++;
                state.NextStep = step + 1;
                ArmPostStep(towerDef, state, step, now);

                if (state.KillWaitStep >= 0 || now < state.NextWaveAt)
                    break;
            }

            Running[id] = state;
        }
    }

    /// <summary>
    /// After opening <paramref name="openedStep"/>, schedule the next open or arm kill tracking.
    /// </summary>
    private static void ArmPostStep(TowerDef towerDef, RunState state, int openedStep, DateTime now)
    {
        var progs = towerDef.Progs;
        if (openedStep < 0 || openedStep >= progs.Count)
        {
            state.NextWaveAt = DateTime.MaxValue;
            state.KillWaitStep = -1;
            state.KillRemaining = null;
            return;
        }

        if (state.NextStep >= progs.Count)
        {
            state.NextWaveAt = DateTime.MaxValue;
            state.KillWaitStep = -1;
            state.KillRemaining = null;
            return;
        }

        var prog = progs[openedStep];
        if (prog.KillTargets is { Count: > 0 })
        {
            state.KillWaitStep = openedStep;
            state.KillRemaining = [];
            foreach (var kt in prog.KillTargets)
            {
                if (kt.KillCount <= 0)
                    continue;
                state.KillRemaining[kt.KillTargetId] =
                    state.KillRemaining.GetValueOrDefault(kt.KillTargetId) + (int)kt.KillCount;
            }

            if (state.KillRemaining.Count == 0)
            {
                state.KillWaitStep = -1;
                state.KillRemaining = null;
            }
            else
            {
                state.NextWaveAt = DateTime.MaxValue;
                Logger.Info(
                    "TowerDef {0} step {1} waiting for kill quotas ({2} npc types)",
                    towerDef.Id, openedStep, state.KillRemaining.Count);
                return;
            }
        }

        var hold = prog.CondToNextTime > 0f ? prog.CondToNextTime : 0f;
        state.KillWaitStep = -1;
        state.KillRemaining = null;
        state.NextWaveAt = now + TimeSpan.FromSeconds(hold);
    }

    private static void OpenStep(TowerDef towerDef, RunState state, int step, string reason)
    {
        var zones = 0;
        foreach (var zoneId in state.HostZoneIds)
        {
            var zone = ZoneSession.Instance.GetByZoneId(zoneId);
            if (zone == null || zone.State < ZoneConnectionState.ZoneLoaded)
                continue;
            if (!state.SpotByZone.TryGetValue(zoneId, out var spotIdx))
                spotIdx = 0;
            var group = (ushort)ZoneGroupOf(zone);
            zone.SendPacket(new WZTowerDefWaveStartPacket(
                (int)towerDef.Id, (short)group, spotIdx, (uint)step));
            zones++;
        }

        if (zones > 0)
        {
            state.CurrentStep = step;
            BroadcastScWave(towerDef, state, (uint)step);
            BroadcastClientMapState();
        }

        // Retail ChangeStep can log success with zero emit; re-fire tower OnEvent on g placements.
        if (zones > 0)
            TowerDefWaveForce.ArmProgSpawners(towerDef, step, state.HostZoneIds);

        // Zone ChangeStep ignores DoodadAlmighty spawn targets — World authors them.
        if (zones > 0)
            TowerDefProgDoodads.ApplyStep(towerDef, step, state.HostZoneIds);

        Logger.Info(
            "WZTowerDefWaveStart → {0} host zones: towerDef={1} step={2} ({3}) — {4}",
            zones, towerDef.Id, step, reason, towerDef.Name);

        // Victory / loot phases (e.g. Abyssal 36 → 37). Hold for the final step's
        // cond_to_next_time when set (36 final = 10 s) so restore FX lands before crystals.
        if (zones > 0 &&
            towerDef.Progs != null &&
            step == towerDef.Progs.Count - 1 &&
            towerDef.FollowOnTowerDefId != 0)
        {
            ScheduleOrStartFollowOn(towerDef, state, step);
        }
    }

    private static void ScheduleOrStartFollowOn(TowerDef towerDef, RunState state, int finalStep)
    {
        var followId = towerDef.FollowOnTowerDefId;
        if (followId == 0)
            return;

        var delay = TowerDefFollowOnDelay.FromFinalProg(towerDef.Progs[finalStep]);
        if (delay <= TimeSpan.Zero)
        {
            TryStartFollowOn(towerDef, state);
            return;
        }

        state.PendingFollowOnId = followId;
        state.FollowOnAt = DateTime.UtcNow + delay;
        state.FollowOnGeneration = state.Generation;
        Logger.Info(
            "TowerDef {0} follow-on {1} scheduled in {2:0.#}s (final CondToNextTime, gen={3})",
            towerDef.Id, followId, delay.TotalSeconds, state.FollowOnGeneration);

        // Precise wake — schedule-gate Tick is only ~5s; do not wait on that alone.
        var predecessorId = towerDef.Id;
        var scheduledGeneration = state.FollowOnGeneration;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                lock (Sync)
                {
                    if (!Running.TryGetValue(predecessorId, out var live))
                        return;
                    if (!TowerDefFollowOnGate.ShouldFire(
                            live.PendingFollowOnId, followId, live.Generation, scheduledGeneration))
                        return;
                    live.PendingFollowOnId = 0;
                    live.FollowOnAt = DateTime.MaxValue;
                    live.FollowOnGeneration = 0;
                    Running[predecessorId] = live;
                    var td = TowerDefGameData.Instance.GetTowerDef(predecessorId);
                    if (td == null)
                        return;
                    Logger.Info(
                        "TowerDef {0} follow-on {1} delay elapsed — starting",
                        predecessorId, followId);
                    TryStartFollowOn(td, live);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "TowerDef {0} follow-on delay task failed", predecessorId);
            }
        });
    }

    private static void AdvancePendingFollowOns(DateTime now)
    {
        foreach (var (id, state) in Running.ToList())
        {
            if (state.PendingFollowOnId == 0 || now < state.FollowOnAt)
                continue;

            if (!TowerDefFollowOnGate.ShouldFire(
                    state.PendingFollowOnId, state.PendingFollowOnId, state.Generation, state.FollowOnGeneration))
            {
                state.PendingFollowOnId = 0;
                state.FollowOnAt = DateTime.MaxValue;
                state.FollowOnGeneration = 0;
                Running[id] = state;
                continue;
            }

            var towerDef = TowerDefGameData.Instance.GetTowerDef(id);
            var followId = state.PendingFollowOnId;
            state.PendingFollowOnId = 0;
            state.FollowOnAt = DateTime.MaxValue;
            state.FollowOnGeneration = 0;
            Running[id] = state;

            if (towerDef == null)
            {
                Logger.Warn("TowerDef {0} follow-on {1} due but predecessor missing", id, followId);
                continue;
            }

            Logger.Info(
                "TowerDef {0} follow-on {1} delay elapsed (tick) — starting",
                id, followId);
            TryStartFollowOn(towerDef, state);
        }
    }

    /// <summary>
    /// Starts a configured follow-on tower as Manual, preferring the predecessor's host zones
    /// when the follow-on has no playability reports yet.
    /// </summary>
    private static void TryStartFollowOn(TowerDef predecessor, RunState predecessorState)
    {
        var followId = predecessor.FollowOnTowerDefId;
        if (followId == 0 || Running.ContainsKey(followId))
            return;

        var follow = TowerDefGameData.Instance.GetTowerDef(followId);
        if (follow == null)
        {
            Logger.Warn(
                "TowerDef {0} follow-on {1} missing from loaded tower_defs",
                predecessor.Id, followId);
            return;
        }

        // Soft-refresh playability so manual seed fallback is not the only path.
        foreach (var zone in LoadedZones())
        {
            if (!HasPlayabilityReport(followId, zone.ZoneId))
                QueryPlayability(zone, followId);
        }

        var hosts = EnumeratePlayableHosts(followId, manualFallbackAll: true).ToList();
        if (hosts.Count == 0 && predecessorState.HostZoneIds.Count > 0)
        {
            // Same island / continent as the fight that just completed.
            Logger.Info(
                "TowerDef {0} follow-on {1}: no playability yet — hosting predecessor zones [{2}]",
                predecessor.Id,
                followId,
                string.Join(',', predecessorState.HostZoneIds));
            StartOnHostZones(
                follow,
                predecessorState.HostZoneIds,
                predecessorState.SpotByZone,
                DateTime.UtcNow,
                manual: true,
                fromGameTime: false,
                $"follow-on after tower={predecessor.Id}");
            return;
        }

        if (hosts.Count == 0)
        {
            Logger.Warn(
                "TowerDef {0} follow-on {1} skipped — no host zones ({2})",
                predecessor.Id, followId, follow.Name);
            return;
        }

        Start(
            follow,
            DateTime.UtcNow,
            manual: true,
            fromGameTime: false,
            $"follow-on after tower={predecessor.Id}");
    }

    /// <summary>
    /// Like <see cref="Start"/> but forces the given host zone list (follow-on inheritance).
    /// </summary>
    private static void StartOnHostZones(
        TowerDef towerDef,
        IReadOnlyList<uint> hostZoneIds,
        IReadOnlyDictionary<uint, uint> spotByZone,
        DateTime now,
        bool manual,
        bool fromGameTime,
        string reason)
    {
        if (hostZoneIds == null || hostZoneIds.Count == 0)
            return;

        var firstWaveDelay = towerDef.FirstWaveAfter > 0f
            ? TimeSpan.FromSeconds(towerDef.FirstWaveAfter)
            : TimeSpan.Zero;

        var announceZoneId = hostZoneIds[0];
        var announceGroup = (ushort)0;
        var state = new RunState
        {
            Deadline = now + towerDef.Duration,
            Manual = manual,
            FromGameTime = fromGameTime,
            NextStep = 0,
            NextWaveAt = now + firstWaveDelay,
            AnnounceZoneId = announceZoneId,
            AnnounceZoneGroupId = announceGroup,
            KillWaitStep = -1,
            Generation = NextRunGeneration()
        };

        var zones = 0;
        foreach (var zoneId in hostZoneIds)
        {
            var zone = ZoneSession.Instance.GetByZoneId(zoneId);
            if (zone == null || zone.State < ZoneConnectionState.ZoneLoaded)
                continue;
            var spotIdx = spotByZone != null && spotByZone.TryGetValue(zoneId, out var s) ? s : 0u;
            var group = (ushort)ZoneGroupOf(zone);
            state.HostZoneIds.Add(zoneId);
            state.SpotByZone[zoneId] = spotIdx;
            state.AnnounceZoneId = zoneId;
            state.AnnounceZoneGroupId = group;
            zone.SendPacket(new WZTowerDefStartPacket(
                (int)towerDef.Id, (short)group, spotIdx));
            zones++;
            Logger.Info(
                "WZTowerDefStart → zoneId={0} group={1} towerDef={2} spot={3} ({4})",
                zoneId, group, towerDef.Id, spotIdx, reason);
        }

        if (zones == 0)
            return;

        Running[towerDef.Id] = state;
        Logger.Info(
            "WZTowerDefStart hosts={0}: towerDef={1} spawner={2} for {3} ({4}) — {5}",
            zones, towerDef.Id, towerDef.TargetNpcSpawnId, towerDef.Duration, reason, towerDef.Name);
        BroadcastScStart(towerDef, state);
        TowerDefWaveForce.ArmPortalTargets(towerDef, state.HostZoneIds);
        if (firstWaveDelay <= TimeSpan.Zero)
            AdvanceTimedWaves(now);
    }

    private static void End(TowerDef towerDef, string reason)
    {
        if (!Running.TryGetValue(towerDef.Id, out var state))
        {
            state = new RunState
            {
                AnnounceZoneId = FirstLoadedZoneId(),
                AnnounceZoneGroupId = FirstLoadedZoneGroup()
            };
            foreach (var (zone, spots, group) in EnumeratePlayableHosts(towerDef.Id, manualFallbackAll: false))
            {
                state.HostZoneIds.Add(zone.ZoneId);
                state.SpotByZone[zone.ZoneId] = PickSpot(spots);
                state.AnnounceZoneId = zone.ZoneId;
                state.AnnounceZoneGroupId = group;
            }
        }

        Running.Remove(towerDef.Id);

        // Drop a pending follow-on so /towerdef end mid-hold does not still arm reward crystals.
        // (Follow-on already Running is left alone.)

        try
        {
            var doodads = TowerDefProgDoodads.DespawnAll(towerDef.Id);
            if (doodads > 0)
                Logger.Info("TowerDef {0} End despawned prog doodads={1}", towerDef.Id, doodads);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "TowerDef {0} End prog doodad cleanup failed", towerDef.Id);
        }

        var zones = 0;
        var zoneList = state.HostZoneIds.Count > 0
            ? state.HostZoneIds
            : LoadedZones().Select(z => z.ZoneId).ToList();

        foreach (var zoneId in zoneList)
        {
            var zone = ZoneSession.Instance.GetByZoneId(zoneId);
            if (zone == null || zone.State < ZoneConnectionState.ZoneLoaded)
                continue;
            if (!state.SpotByZone.TryGetValue(zoneId, out var spotIdx))
                spotIdx = 0;
            zone.SendPacket(new WZTowerDefEndPacket(
                (int)towerDef.Id, (short)ZoneGroupOf(zone), spotIdx));
            zones++;
        }

        if (state.HostZoneIds.Count > 0 || zones > 0)
            BroadcastScEnd(towerDef, state);

        // Map / list packets always carry the post-remove snapshot (empty or remaining events).
        BroadcastClientMapState();

        // WZ End removes dedic-created units eventually; World-authored plot army (8826/8834…) and
        // lagging stage mirrors need an explicit cleanup or they stick after /towerdef end.
        // Run off the scheduler lock so WZ/ZW do not re-enter under Sync.
        var hostZones = state.HostZoneIds.Count > 0
            ? (IReadOnlyList<uint>)state.HostZoneIds.ToList()
            : Array.Empty<uint>();
        var towerId = towerDef.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                var n = WorldIntegration.DespawnTowerDefEventUnits(towerId, hostZones);
                if (n > 0)
                    Logger.Info("TowerDef {0} End cleanup pass-1 despawned={1}", towerId, n);
                // Late plot tickets keep SpawnEffect after stage Interrupt — second sweep.
                await Task.Delay(2500).ConfigureAwait(false);
                var n2 = WorldIntegration.DespawnTowerDefEventUnits(towerId, hostZones);
                if (n2 > 0)
                    Logger.Info("TowerDef {0} End cleanup pass-2 despawned={1}", towerId, n2);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "TowerDef {0} End cleanup failed", towerId);
            }
        });

        Logger.Info("WZTowerDefEnd → {0} zones: towerDef={1} ({2}) — {3}",
            zones, towerDef.Id, reason, towerDef.Name);
    }

    private static void BroadcastScStart(TowerDef towerDef, RunState state)
    {
        var key = MakeKey(towerDef, state);
        var seamless = towerDef.BroadcastToWholeWorld;
        WorldIntegration.BroadcastPacket(new SCTowerDefStartPacket(
            key, state.AnnounceZoneId, isStartSeamlessWorld: seamless, isBroadCastSeamless: seamless));
        Logger.Info(
            "SCTowerDefStart towerDef={0} zoneGroup={1} eventZone={2} seamless={3} hosts={4}",
            towerDef.Id, key.ZoneGroupId, state.AnnounceZoneId, seamless, state.HostZoneIds.Count);
        BroadcastClientMapState();
    }

    private static void BroadcastScEnd(TowerDef towerDef, RunState state)
    {
        var key = MakeKey(towerDef, state);
        var seamless = towerDef.BroadcastToWholeWorld;
        WorldIntegration.BroadcastPacket(new SCTowerDefEndPacket(
            key, state.AnnounceZoneId, isStartSeamlessWorld: seamless, isBroadCastSeamless: seamless));
    }

    private static void BroadcastScWave(TowerDef towerDef, RunState state, uint step)
    {
        var key = MakeKey(towerDef, state);
        // isSyncStep updates the client map mark's curStep for seamless path; ActiveInfo still
        // covers non-broadcast events (Crimson).
        WorldIntegration.BroadcastPacket(new SCTowerDefWaveStartPacket(
            key, state.AnnounceZoneId, step, isSyncStep: true));
    }

    /// <summary>
    /// Push map marks + positioned list for every running tower. Required for zone-local
    /// events where <c>broadcast_event_to_whole_seamless_world</c> is false — Start alone does
    /// not place the world-map skull (client only does that when the seamless start bit is set).
    /// </summary>
    private static void BroadcastClientMapState()
    {
        List<TowerDefActiveInfo> active;
        List<TowerDefInfo> list;
        lock (Sync)
        {
            active = BuildActiveInfoList();
            list = BuildPositionedList();
        }

        WorldIntegration.BroadcastPacket(new SCTowerDefActiveInfoListPacket(active));
        WorldIntegration.BroadcastPacket(new SCTowerDefListPacket(list));
        Logger.Info(
            "SCTowerDef map sync active={0} list={1}",
            active.Count, list.Count);
    }

    /// <summary>Late-join / after load: send the current map snapshot to one player.</summary>
    public static void SyncToCharacter(AAEmu.Game.Models.Game.Char.Character character)
    {
        if (character?.Connection == null)
            return;

        List<TowerDefActiveInfo> active;
        List<TowerDefInfo> list;
        lock (Sync)
        {
            active = BuildActiveInfoList();
            list = BuildPositionedList();
        }

        try
        {
            character.SendPacket(new SCTowerDefActiveInfoListPacket(active));
            character.SendPacket(new SCTowerDefListPacket(list));
            Logger.Info(
                "SCTowerDef sync → {0}: active={1} list={2}",
                character.Name, active.Count, list.Count);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "SCTowerDef sync failed for {0}", character.Name);
        }
    }

    /// <summary>
    /// Portal seed mirrored after Start — refresh positioned list so the map pin follows the unit.
    /// </summary>
    public static void OnEventNpcMirrored(uint templateId)
    {
        if (templateId == 0)
            return;

        bool hits;
        lock (Sync)
        {
            if (Running.Count == 0)
                return;

            hits = false;
            foreach (var (id, _) in Running)
            {
                var def = TowerDefGameData.Instance.GetTowerDef(id);
                if (def == null)
                    continue;
                if (TowerDefGameData.Instance.IsPortalSeedNpc(def.TargetNpcSpawnId, templateId))
                {
                    hits = true;
                    break;
                }
            }
        }

        if (hits)
            BroadcastClientMapState();
    }

    private static List<TowerDefActiveInfo> BuildActiveInfoList()
    {
        var result = new List<TowerDefActiveInfo>();
        foreach (var (id, state) in Running)
        {
            var def = TowerDefGameData.Instance.GetTowerDef(id);
            if (def == null)
                continue;

            var step = state.CurrentStep < 0 ? 0u : (uint)state.CurrentStep;
            var hostZones = state.HostZoneIds.Count > 0
                ? state.HostZoneIds
                : [state.AnnounceZoneId];

            foreach (var zoneId in hostZones)
            {
                if (zoneId == 0)
                    continue;
                var group = state.AnnounceZoneGroupId;
                var zone = ZoneSession.Instance.GetByZoneId(zoneId);
                if (zone != null)
                    group = (ushort)ZoneGroupOf(zone);

                result.Add(new TowerDefActiveInfo
                {
                    ZoneId = zoneId,
                    CurrentStep = step,
                    TowerDefId = def.Id,
                    ZoneGroupId = group
                });
            }
        }

        return result;
    }

    private static List<TowerDefInfo> BuildPositionedList()
    {
        var result = new List<TowerDefInfo>();
        foreach (var (id, state) in Running)
        {
            var def = TowerDefGameData.Instance.GetTowerDef(id);
            if (def == null)
                continue;

            var step = state.CurrentStep < 0 ? 0u : (uint)state.CurrentStep;
            var hostZones = state.HostZoneIds.Count > 0
                ? state.HostZoneIds
                : [state.AnnounceZoneId];

            foreach (var zoneId in hostZones)
            {
                if (zoneId == 0)
                    continue;

                var group = state.AnnounceZoneGroupId;
                var zone = ZoneSession.Instance.GetByZoneId(zoneId);
                if (zone != null)
                    group = (ushort)ZoneGroupOf(zone);

                state.SpotByZone.TryGetValue(zoneId, out var spotIdx);
                TryFindPortal(def, zoneId, out var portalObjId, out var x, out var y, out var z);

                result.Add(new TowerDefInfo
                {
                    TowerDefKey = new TowerDefKey { TowerDefId = def.Id, ZoneGroupId = group },
                    ZoneId = zoneId,
                    SpotId = spotIdx,
                    TargetObjId = portalObjId,
                    Position = new AAEmu.Game.Models.Game.World.Point(x, y, z),
                    CurrentStep = step
                });
            }
        }

        return result;
    }

    private static void TryFindPortal(TowerDef def, uint zoneId, out uint objId, out float x, out float y, out float z)
    {
        objId = 0;
        x = y = z = 0f;
        if (def.TargetNpcSpawnId == 0)
            return;

        var members = TowerDefGameData.Instance.GetSpawnerMemberNpcIds(def.TargetNpcSpawnId);
        if (members.Count == 0)
            return;

        var world = WorldIntegration.ResolveWorldForZone(zoneId);
        if (world == null)
            return;

        foreach (var npc in world.GetAllNpcs())
        {
            if (npc is not { IsZoneMirror: true } || !members.Contains(npc.TemplateId))
                continue;

            if (npc.Transform == null)
                continue;
            if (npc.Transform.ZoneId != 0 && npc.Transform.ZoneId != zoneId)
                continue;

            var pos = npc.Transform.World.Position;
            objId = npc.ObjId;
            x = pos.X;
            y = pos.Y;
            z = pos.Z;
            return;
        }
    }

    private static TowerDefKey MakeKey(TowerDef towerDef, RunState state)
    {
        return new TowerDefKey
        {
            TowerDefId = towerDef.Id,
            ZoneGroupId = state.AnnounceZoneGroupId
        };
    }

    private static bool HasPlayabilityReport(uint towerDefId, uint zoneId)
    {
        return Playability.TryGetValue(towerDefId, out var byZone) && byZone.ContainsKey(zoneId);
    }

    private static IEnumerable<(ZoneConnection Zone, uint Spots, ushort Group)> EnumeratePlayableHosts(
        uint towerDefId, bool manualFallbackAll)
    {
        var anyReport = Playability.TryGetValue(towerDefId, out var byZone) && byZone.Count > 0;

        foreach (var zone in LoadedZones())
        {
            var group = (ushort)ZoneGroupOf(zone);
            if (byZone != null && byZone.TryGetValue(zone.ZoneId, out var spots))
            {
                if (spots > 0)
                    yield return (zone, spots, group);
                continue;
            }

            // Manual GM when no zone has answered yet: only host zones that actually place the
            // seed spawnerType in npc_spawners.g. Fan-out to every loaded dedicate wrongly armed
            // Cross Plains when ForceStarting Lilyut Oblivion before playability replies arrived.
            if (manualFallbackAll && !anyReport)
            {
                var seedType = TowerDefGameData.Instance.GetTowerDef(towerDefId)?.TargetNpcSpawnId ?? 0;
                if (seedType != 0 && ZoneSpawnerPlacementCatalog.GetByType(zone.ZoneId, seedType).Count > 0)
                    yield return (zone, 1, group);
            }
        }
    }

    private static uint PickSpot(uint playableSpots)
    {
        if (playableSpots <= 1)
            return 0;
        return (uint)Random.Shared.Next(0, (int)playableSpots);
    }

    private static void QueryPlayabilityForZone(ZoneConnection zone)
    {
        foreach (var towerDef in TowerDefGameData.Instance.GetGameTimeScheduledTowerDefs())
            QueryPlayability(zone, towerDef.Id);
        foreach (var towerDef in TowerDefGameData.Instance.GetScheduledTowerDefs())
            QueryPlayability(zone, towerDef.Id);

        // Follow-on reward phases are Manual and never appear in the schedule lists above.
        foreach (var towerDef in TowerDefGameData.Instance.GetAllTowerDefs())
        {
            if (towerDef.FollowOnTowerDefId == 0)
                continue;
            QueryPlayability(zone, towerDef.FollowOnTowerDefId);
        }
    }

    private static void QueryPlayability(ZoneConnection zone, uint towerDefId)
    {
        var group = (short)ZoneGroupOf(zone);
        zone.SendPacket(new WZTowerDefQueryPlayabilityPacket((int)towerDefId, group));
    }

    private static uint ZoneGroupOf(ZoneConnection zone)
    {
        return ZoneManager.Instance.GetZoneByKey(zone.ZoneId)?.GroupId ?? 0;
    }

    private static uint FirstLoadedZoneId()
    {
        foreach (var zone in LoadedZones())
            return zone.ZoneId;
        return 0;
    }

    private static ushort FirstLoadedZoneGroup()
    {
        foreach (var zone in LoadedZones())
            return (ushort)ZoneGroupOf(zone);
        return 0;
    }

    private static IEnumerable<ZoneConnection> LoadedZones()
    {
        foreach (var zone in ZoneSession.Instance.All)
        {
            if (zone.State >= ZoneConnectionState.ZoneLoaded)
                yield return zone;
        }
    }

    /// <summary>Schedule overview for the GM trigger's list action.</summary>
    public static IEnumerable<string> Describe()
    {
        var days = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        HashSet<uint> running;
        Dictionary<uint, Dictionary<uint, uint>> playabilitySnapshot;
        lock (Sync)
        {
            running = [.. Running.Keys];
            playabilitySnapshot = Playability.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToDictionary(z => z.Key, z => z.Value));
        }

        var lines = new List<string>();
        foreach (var towerDef in TowerDefGameData.Instance.GetScheduledTowerDefs().OrderBy(t => t.Id))
        {
            var slots = new List<string>();
            for (var day = 0; day < 7; day++)
            {
                if (towerDef.StartTimes[day] is { } slot)
                    slots.Add($"{days[day]} {slot:hh\\:mm}");
            }

            var state = running.Contains(towerDef.Id) ? "RUNNING" : "idle";
            lines.Add(
                $"{towerDef.Id,4} [{state,7}] UTC  spawner={towerDef.TargetNpcSpawnId,6} " +
                $"{towerDef.Duration:hh\\:mm} {string.Join(", ", slots)}  {towerDef.Name}");
        }

        foreach (var towerDef in TowerDefGameData.Instance.GetGameTimeScheduledTowerDefs().OrderBy(t => t.Id))
        {
            var state = running.Contains(towerDef.Id) ? "RUNNING" : "idle";
            var play = playabilitySnapshot.TryGetValue(towerDef.Id, out var byZ)
                ? string.Join(",", byZ.Select(z => $"{z.Key}:{z.Value}"))
                : "-";
            lines.Add(
                $"{towerDef.Id,4} [{state,7}] GAME tod={towerDef.TimeOfDay:F1} spawner={towerDef.TargetNpcSpawnId,6} " +
                $"{towerDef.Duration:hh\\:mm} progs={towerDef.Progs?.Count ?? 0} play=[{play}]  {towerDef.Name}");
        }

        return lines;
    }
}
