using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Runs the timed world events in <c>tower_defs</c> — the world-boss layer.
/// </summary>
/// <remarks>
/// This is a separate system from <c>game_schedules</c> (see <see cref="GameScheduleRelay"/>), and
/// the two do not overlap: no Kraken, Leviathan or dragon spawner appears in
/// <c>game_schedule_spawners</c>. A <c>tower_defs</c> row names a target spawner, a duration, a
/// broadcast message and seven <c>start_hourN</c>/<c>start_minuteN</c> slots, one per weekday.
///
/// 24 live rows carry a slot: 크라켄의 출현 (Kraken, row 152), 레비아탄의 출현 (Leviathan, 77),
/// 붉은 용의 출현 (Red Dragon, 103), 검은 용의 침공 (Black Dragon, 108/151), 칼리디스의 출현 (144),
/// 뒤틀린 자 안탈론 raid (150), 델피나드 유령선 (44) and 풍랑의 전조 (22). Almost all sit between
/// 21:00 and 21:45 local server time.
///
/// Nothing read those columns before, so every one of them was dead. World drives the zone with
/// <c>WZTowerDefStart</c> / <c>WaveStart</c> / <c>End</c> (0x067-0x069); the dedicate owns the
/// spawner activation and the wave progression from there.
/// </remarks>
public static class TowerDefScheduler
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object Sync = new();

    /// <summary>
    /// Events currently up. <c>Deadline</c> is the hard stop from <c>force_end_time</c>;
    /// <c>Manual</c> marks a run started by hand, which the schedule check must not reap — it is
    /// by definition outside its weekday slot, so without this flag the next tick ends it.
    /// </summary>
    private readonly record struct RunState(DateTime Deadline, bool Manual);

    private static readonly Dictionary<uint, RunState> Running = [];
    private static bool _primed;

    public static int RunningCount { get { lock (Sync) return Running.Count; } }

    /// <summary>
    /// The <c>spotIdx</c> field of the WZTowerDef packets is an index into the event's own spot
    /// list, not a zone key. Passing the zone key made the dedicate reject the packet with
    /// <c>TowerDef(152,36): Invalid idx(210)</c>; every shipped row defines a single spot, so 0 is
    /// the only valid value until a multi-spot event turns up.
    /// </summary>
    private const uint PrimarySpotIdx = 0;

    /// <summary>
    /// Evaluates the weekday slots and drives the start/end edges. Shares the schedule gate's tick
    /// so all timed content is decided from one clock reading.
    /// </summary>
    public static void Tick()
    {
        lock (Sync)
        {
            var now = DateTime.Now;

            if (!_primed)
            {
                _primed = true;
                var scheduled = TowerDefGameData.Instance.GetScheduledTowerDefs().Count();
                Logger.Info("TowerDefScheduler armed — {0} timed world events carry a weekday slot", scheduled);
            }

            foreach (var towerDef in TowerDefGameData.Instance.GetScheduledTowerDefs())
            {
                var shouldRun = towerDef.IsWithinWindow(now);
                if (!Running.TryGetValue(towerDef.Id, out var state))
                {
                    if (shouldRun)
                        Start(towerDef, now, manual: false, "schedule");
                    continue;
                }

                // A hand-started run sits outside its weekday slot on purpose; only its deadline
                // or an explicit end may stop it.
                if (!shouldRun && !state.Manual)
                    End(towerDef, "window closed");
            }

            // force_end_time is a hard stop independent of the window, and the only thing that
            // retires a manual run on its own.
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

    /// <summary>Fires an event immediately, ignoring its schedule. Used by the GM trigger.</summary>
    public static bool ForceStart(uint towerDefId)
    {
        lock (Sync)
        {
            var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (towerDef == null)
                return false;
            if (Running.ContainsKey(towerDefId))
                return true;

            Start(towerDef, DateTime.Now, manual: true, "manual trigger");
            return true;
        }
    }

    /// <summary>Ends a running event immediately. Used by the GM trigger.</summary>
    public static bool ForceEnd(uint towerDefId)
    {
        lock (Sync)
        {
            var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (towerDef == null || !Running.ContainsKey(towerDefId))
                return false;

            End(towerDef, "manual trigger");
            return true;
        }
    }

    /// <summary>Advances a running event to the next wave. Used by the GM trigger.</summary>
    public static bool ForceWave(uint towerDefId, uint step)
    {
        var towerDef = TowerDefGameData.Instance.GetTowerDef(towerDefId);
        if (towerDef == null)
            return false;

        foreach (var zone in LoadedZones())
        {
            zone.SendPacket(new WZTowerDefWaveStartPacket(
                (int)towerDef.Id, (short)ZoneGroupOf(zone), PrimarySpotIdx, step));
        }

        Logger.Info("WZTowerDefWaveStart → towerDef={0} step={1} ({2})", towerDef.Id, step, towerDef.Name);
        return true;
    }

    private static void Start(TowerDef towerDef, DateTime now, bool manual, string reason)
    {
        Running[towerDef.Id] = new RunState(now + towerDef.Duration, manual);

        var zones = 0;
        foreach (var zone in LoadedZones())
        {
            zone.SendPacket(new WZTowerDefStartPacket(
                (int)towerDef.Id, (short)ZoneGroupOf(zone), PrimarySpotIdx));
            zones++;
        }

        Logger.Info(
            "WZTowerDefStart → {0} zones: towerDef={1} spawner={2} for {3} ({4}) — {5}",
            zones, towerDef.Id, towerDef.TargetNpcSpawnId, towerDef.Duration, reason, towerDef.Name);

        // The spawner the event names is armed by the dedicate, but placements still need the
        // activate sphere to actually run — same rule as an opening game_schedule period.
        foreach (var zone in LoadedZones())
            ZoneProtocolHandler.ReactivateNpcSpawners(zone, $"towerDef {towerDef.Id} started");
    }

    private static void End(TowerDef towerDef, string reason)
    {
        Running.Remove(towerDef.Id);

        var zones = 0;
        foreach (var zone in LoadedZones())
        {
            zone.SendPacket(new WZTowerDefEndPacket(
                (int)towerDef.Id, (short)ZoneGroupOf(zone), PrimarySpotIdx));
            zones++;
        }

        Logger.Info("WZTowerDefEnd → {0} zones: towerDef={1} ({2}) — {3}",
            zones, towerDef.Id, reason, towerDef.Name);
    }

    /// <summary>
    /// Zone group for a connection. <c>tower_def_map_events</c> keys events by ZoneGroup and the
    /// dedicate echoes this back as the second half of its <c>TowerDef(id,group)</c> identity, so
    /// it has to be the real group id.
    /// </summary>
    /// <remarks>
    /// Resolve through the zone, not <c>ZoneManager.GetZoneGroupById</c> — that method is named for
    /// its return type, but it indexes the group table by <em>group</em> id, so handing it a zone
    /// key silently yields null and the packet goes out claiming group 0.
    /// </remarks>
    private static uint ZoneGroupOf(ZoneConnection zone)
    {
        return ZoneManager.Instance.GetZoneByKey(zone.ZoneId)?.GroupId ?? 0;
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
        lock (Sync)
            running = [.. Running.Keys];

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
                $"{towerDef.Id,4} [{state,7}] spawner={towerDef.TargetNpcSpawnId,6} " +
                $"{towerDef.Duration:hh\\:mm} {string.Join(", ", slots)}  {towerDef.Name}");
        }

        return lines;
    }
}
