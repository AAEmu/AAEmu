using AAEmu.Game.Core.Managers;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Drives <c>game_schedules</c> periods into the dedicate.
/// </summary>
/// <remarks>
/// The dedicate owns the spawners but not the calendar: it withholds every placement named in
/// <c>game_schedule_spawners</c> until World declares that schedule open, and it never consults
/// <c>game_schedules</c> itself even though its own <c>game_decrypted.sqlite3</c> carries the table.
/// Measured on zone 183 before this existed — 92 of 92 schedule-linked placements withheld,
/// including spawner 846, whose schedule 272 runs 2013-01-20 → 2099-01-20 on any day at any hour.
///
/// So every event NPC, world boss and timed trader in the game depends on this relay. The live
/// set is small: of 922 schedules, 765 are dated live events that already ended, leaving 52
/// schedules that still carry spawners — the Eanad Library bosses on a four-hour cycle, the
/// Tuesday/Thursday Abyssal Assault, the afternoon and night gold traders, the Saturday fishing
/// contest, and the Wednesday siege and territory-transport windows.
/// </remarks>
public static class GameScheduleRelay
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object Sync = new();

    /// <summary>Schedule ids World has declared open. Drives the Start/End edges.</summary>
    private static HashSet<int> _running = [];
    private static bool _primed;

    public static int RunningCount => _running.Count;

    /// <summary>
    /// Recomputes which periods are open and sends the edges. Called from the schedule gate's
    /// tick so the gate's closed set and the zone's schedule state are derived from one clock
    /// reading — otherwise a period could open here and still be gated shut there for 30s.
    /// </summary>
    public static void Tick()
    {
        lock (Sync)
        {
            var next = GameScheduleManager.Instance.GetRunningGameScheduleIds();
            var previous = _running;
            _running = next;

            var started = new HashSet<int>(next);
            started.ExceptWith(previous);

            var ended = new HashSet<int>(previous);
            ended.ExceptWith(next);

            if (!_primed)
            {
                // First pass: nothing has been told to any zone yet, so every open period is an
                // opening edge rather than a no-op.
                _primed = true;
                Logger.Info("GameScheduleRelay priming — {0} schedule periods are open", next.Count);
                foreach (var zone in LoadedZones())
                    SendOpenSet(zone, next, continuation: true);
                return;
            }

            foreach (var id in started)
                Broadcast(new WZGameScheduleStartPacket(id), id, "start");

            foreach (var id in ended)
                Broadcast(new WZGameScheduleEndPacket(id), id, "end");

            // Opening the period only clears the spawner to run; it is the activate sphere that
            // walks placements and spawns them. Without this the NPCs of a schedule that opens
            // mid-session wait for the next player to enter the zone.
            if (started.Count > 0)
            {
                foreach (var zone in LoadedZones())
                    ZoneProtocolHandler.ReactivateNpcSpawners(zone, $"{started.Count} schedule periods opened");
            }
        }
    }

    /// <summary>
    /// Brings a freshly loaded zone up to date. A dedicate that connects mid-period never saw the
    /// Start edge, which is what Continue exists for.
    /// </summary>
    public static void OnZoneLoaded(ZoneConnection zone)
    {
        lock (Sync)
        {
            if (!_primed)
            {
                // The tick has not run yet; it will prime this zone along with the rest.
                return;
            }

            SendOpenSet(zone, _running, continuation: true);
        }
    }

    private static void SendOpenSet(ZoneConnection zone, HashSet<int> open, bool continuation)
    {
        var sent = 0;
        foreach (var id in open)
        {
            zone.SendPacket(continuation
                ? new WZGameScheduleContinuePacket(id)
                : new WZGameScheduleStartPacket(id));
            sent++;
        }

        if (sent > 0)
        {
            Logger.Info("WZGameScheduleContinue ×{0} → zoneId={1} (periods already open at connect)",
                sent, zone.ZoneId);
        }
    }

    private static void Broadcast(ZonePacket packet, int gameScheduleId, string edge)
    {
        var zones = 0;
        foreach (var zone in LoadedZones())
        {
            zone.SendPacket(packet);
            zones++;
        }

        var spawners = GameScheduleManager.Instance.GetSpawnerIdsForSchedule(gameScheduleId);
        Logger.Info("WZGameSchedule{0} → {1} zones: scheduleId={2} spawners={3}",
            edge == "start" ? "Start" : "End", zones, gameScheduleId, spawners.Count);
    }

    private static IEnumerable<ZoneConnection> LoadedZones()
    {
        foreach (var zone in ZoneSession.Instance.All)
        {
            if (zone.State >= ZoneConnectionState.ZoneLoaded)
                yield return zone;
        }
    }
}
