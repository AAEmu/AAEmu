using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Holds the set of <c>npc_spawners</c> placements that are outside their window, and enforces it
/// on the zone-authority spawn path.
/// </summary>
/// <remarks>
/// The dedicate arms spawners geometrically: <c>WZActivateNpcSpawnersInArea</c> (0x042) carries
/// only a centre and a radius, so every placement in that circle announces itself with
/// <c>ZWSpawnNpc</c> regardless of calendar or time of day. World is the only side that holds
/// <c>game_schedules</c>, so the schedule gate has to be applied here.
///
/// Enforcement rides the acknowledgement: <c>ZWSpawnNpc</c> alone does not create a zone-side NPC.
/// An allowed announcement receives <c>WZNpcState</c> (0x002); a closed-window announcement receives
/// whose handler releases the dedicate's pending spawn state.
/// </remarks>
public static class NpcScheduleGate
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object RefreshLock = new();

    /// <summary>Spawner template ids currently outside their window. Swapped whole, never mutated.</summary>
    private static volatile HashSet<uint> _closed = [];
    private static bool _loaded;
    private static Timer _timer;

    private static long _suppressed;
    private static long _retired;

    /// <summary>Last coverage line per zone, so the periodic report only speaks when it changes.</summary>
    private static readonly Dictionary<uint, string> LastCoverage = [];

    public static int ClosedCount => _closed.Count;
    public static long SuppressedCount => Interlocked.Read(ref _suppressed);

    private static NpcScheduleGateConfig Config => WorldRuntime.Config.NpcScheduleGate;

    /// <summary>
    /// True when this spawner template may not hold live NPCs right now. Called once per
    /// <c>ZWSpawnNpc</c>, so it stays a single lock-free hash lookup.
    /// </summary>
    public static bool IsClosed(uint spawnerTemplateId)
    {
        if (!Config.Enabled || spawnerTemplateId == 0)
            return false;

        // TowerDef portal / wave spawners often carry a day-night window (e.g. Grimghast 14133
        // 0.1–1.0h). Dedic arms them via WZTowerDef*, not ambient geometry + schedule. Applying the
        // ToD gate answers WZNpcSpawnFailed and tears the whole NpcGroup down mid-wave.
        if (AAEmu.Game.GameData.TowerDefGameData.Instance.IsTowerDefEventSpawner(spawnerTemplateId))
            return false;

        return _closed.Contains(spawnerTemplateId);
    }

    /// <summary>
    /// Computes the closed set once before the first activate, so the opening flood is already
    /// gated. Zone load can finish before the refresh timer's first tick.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded || !Config.Enabled)
            return;

        lock (RefreshLock)
        {
            if (_loaded)
                return;
            Refresh();
        }
    }

    /// <summary>Begins periodic re-evaluation. Safe to call more than once.</summary>
    public static void Start()
    {
        if (!Config.Enabled)
        {
            Logger.Warn("NpcScheduleGate disabled by configuration — event and timed NPCs will spawn unconditionally");
            return;
        }

        EnsureLoaded();

        lock (RefreshLock)
        {
            if (_timer != null)
                return;

            var period = TimeSpan.FromSeconds(Math.Max(5, Config.RefreshSeconds));
            _timer = new Timer(_ => SafeRefresh(), null, period, period);
            Logger.Info("NpcScheduleGate armed — re-evaluating every {0}s", period.TotalSeconds);
        }
    }

    public static void Stop()
    {
        lock (RefreshLock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private static void SafeRefresh()
    {
        try
        {
            lock (RefreshLock)
                Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "NpcScheduleGate refresh failed");
        }
    }

    /// <summary>
    /// Recomputes the closed set and acts on the transitions: retire what a closing window has
    /// left standing, re-arm the dedicate for what an opening window now allows.
    /// </summary>
    private static void Refresh()
    {
        var previous = _closed;
        var next = GameScheduleManager.Instance.GetClosedSpawnerTemplateIds();
        _closed = next;

        var justClosed = new HashSet<uint>(next);
        justClosed.ExceptWith(previous);

        var justOpened = new HashSet<uint>(previous);
        justOpened.ExceptWith(next);

        if (!_loaded)
        {
            _loaded = true;
            Logger.Info("NpcScheduleGate loaded — {0} spawner templates are outside their window", next.Count);
        }
        else if (justClosed.Count > 0 || justOpened.Count > 0)
        {
            Logger.Info("NpcScheduleGate transition — closed +{0} / opened +{1} (total closed {2})",
                justClosed.Count, justOpened.Count, next.Count);
        }

        // One clock reading drives both sides: the zone is told which periods are open, and this
        // gate holds back everything outside its window. Ticking them apart would let a period
        // open on the zone while the gate still suppressed its spawns for up to a full interval.
        GameScheduleRelay.Tick();
        TowerDefScheduler.Tick();

        if (justClosed.Count > 0)
            RetireClosedSpawns(justClosed);

        if (justOpened.Count > 0)
            ReArmZones(justOpened.Count);

        ReportCoverage(next);
    }

    /// <summary>
    /// Reports how much of what each zone has actually announced the gate is in a position to
    /// judge. Separates "this zone has no scheduled placements" from "the ids the zone sends do
    /// not line up with npc_spawners.id", which look identical from the suppression count alone.
    /// </summary>
    private static void ReportCoverage(HashSet<uint> closed)
    {
        var scheduled = GameScheduleManager.Instance.GetScheduledSpawnerTemplateIds();

        foreach (var zone in ZoneSession.Instance.All)
        {
            if (zone.State < ZoneConnectionState.ZoneLoaded)
                continue;

            var tracked = 0;
            var scheduledHits = 0;
            var closedHits = 0;
            var types = new HashSet<uint>();

            foreach (var (_, raw) in zone.Units.Snapshot())
            {
                var parsed = ZwSpawnNpcParser.TryParse(raw);
                if (parsed == null)
                    continue;

                tracked++;
                types.Add(parsed.SpawnerType);
                if (scheduled.Contains(parsed.SpawnerType))
                    scheduledHits++;
                if (closed.Contains(parsed.SpawnerType))
                    closedHits++;
            }

            var summary = $"{tracked}/{types.Count}/{scheduledHits}/{closedHits}";
            if (LastCoverage.TryGetValue(zone.ZoneId, out var previous) && previous == summary)
                continue; // steady state — the 30s tick would otherwise repeat this forever

            var first = !LastCoverage.ContainsKey(zone.ZoneId);
            LastCoverage[zone.ZoneId] = summary;

            // The id list is what tells a schedule mismatch apart from a zone that simply never
            // armed those placements; dumped once per zone so it stays out of the steady state.
            if (first)
            {
                Logger.Info("NpcScheduleGate announced spawnerTypes zoneId={0}: {1}",
                    zone.ZoneId, string.Join(",", types.Order()));
            }

            Logger.Info(
                "NpcScheduleGate coverage zoneId={0} trackedNpcs={1} distinctSpawnerTypes={2} scheduled={3} closed={4}",
                zone.ZoneId, tracked, types.Count, scheduledHits, closedHits);
        }
    }

    /// <summary>
    /// Sends NPCs whose window has just closed to GO_TO_DESPAWN and drops their World mirror, so
    /// clients stop seeing them immediately. The first pass after boot also catches anything
    /// mirrored before the gate came up.
    /// </summary>
    /// <remarks>
    /// The bcId stays registered and stays allocated. Zone answers WZNpcStartDespawn with its own
    /// <c>ZWRemoveNpc</c>, and that is what retires the id through the ordinary path — releasing it
    /// here would let <see cref="ObjectIdManager"/> hand the same id to a new unit before that
    /// confirmation lands, and the late ZWRemoveNpc would then delete the wrong one.
    /// </remarks>
    private static void RetireClosedSpawns(HashSet<uint> justClosed)
    {
        foreach (var zone in ZoneSession.Instance.All)
        {
            if (zone.State < ZoneConnectionState.ZoneLoaded)
                continue;

            var retired = 0;
            foreach (var (bcId, raw) in zone.Units.Snapshot())
            {
                var parsed = ZwSpawnNpcParser.TryParse(raw);
                if (parsed == null || !justClosed.Contains(parsed.SpawnerType))
                    continue;

                zone.SendPacket(new WZNpcStartDespawnPacket(bcId));
                // Idempotent: the mirror is already gone when ZWRemoveNpc repeats this.
                WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
                NpcSpawnRelay.ForgetNpcState(zone.ZoneId, zone.InstanceId, bcId);
                retired++;
            }

            if (retired > 0)
            {
                Interlocked.Add(ref _retired, retired);
                Logger.Info("NpcScheduleGate retired {0} NPCs on zoneId={1} — window closed (total retired {2})",
                    retired, zone.ZoneId, Interlocked.Read(ref _retired));
            }
        }
    }

    /// <summary>
    /// Re-announces the activate sphere so the dedicate re-runs the spawners it has already armed.
    /// Placements suppressed while their window was shut announce again and are accepted this time.
    /// </summary>
    private static void ReArmZones(int openedCount)
    {
        foreach (var zone in ZoneSession.Instance.All)
        {
            if (zone.State < ZoneConnectionState.ZoneLoaded)
                continue;

            ZoneProtocolHandler.ReactivateNpcSpawners(zone, $"{openedCount} spawner windows opened");
        }
    }

    /// <summary>Records a withheld acknowledgement; logs at a decreasing rate under a flood.</summary>
    internal static void CountSuppressed(uint zoneId, uint spawnerId, uint spawnerType, uint templateId)
    {
        var n = Interlocked.Increment(ref _suppressed);
        if (n <= 5 || n % 250 == 0)
        {
            Logger.Info(
                "NpcScheduleGate suppressed #{0} zoneId={1} sid={2} sType={3} tpl={4} — outside its schedule window",
                n, zoneId, spawnerId, spawnerType, templateId);
        }
    }
}
