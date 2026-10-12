using AAEmu.Game;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// npc_spawners ids the game keeps shut in every zone (exile brokers of closed routes). A shut id's spawn
/// announcements are refused and its live NPCs are hidden from players but left alive in their zone, because a
/// zone does not respawn a placement it was told to despawn. When an id opens again the hidden NPCs are mirrored
/// again and the zones are re-armed for placements that never spawned. The set is replaced as a whole by
/// <see cref="Apply"/>.
/// </summary>
public static class GameSpawnerGate
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static volatile HashSet<uint> _closed = [];

    public static bool IsClosed(uint spawnerId, uint spawnerType)
    {
        var closed = _closed;
        return closed.Count != 0 && (closed.Contains(spawnerId) || closed.Contains(spawnerType));
    }

    public static void Apply(IReadOnlyCollection<uint> closedIds)
    {
        var previous = _closed;
        var next = new HashSet<uint>(closedIds ?? []);
        _closed = next;

        var newlyClosed = next.Except(previous).ToHashSet();
        var reopened = previous.Except(next).ToHashSet();
        if (newlyClosed.Count == 0 && reopened.Count == 0)
            return;

        var relay = new NpcSpawnRelay();
        var shown = 0;
        foreach (var zone in PlayerEnterService.AllLoadedZones())
        {
            if (newlyClosed.Count > 0)
                Hide(zone, newlyClosed);
            if (reopened.Count > 0)
            {
                shown += relay.RemirrorSpawners(zone, reopened);
                ConflictZoneSpawnerRelay.ReactivateSpawners(zone, "game spawner reopened");
            }
        }

        Logger.Info("GameSpawnerGate: {0} spawner(s) shut, {1} newly, {2} reopened, {3} hidden NPC(s) shown again",
            next.Count, newlyClosed.Count, reopened.Count, shown);
    }

    private static void Hide(ZoneConnection zone, HashSet<uint> spawnerIds)
    {
        foreach (var (bcId, raw) in zone.Units.Snapshot())
        {
            var parsed = ZwSpawnNpcParser.TryParse(raw);
            if (parsed == null || (!spawnerIds.Contains(parsed.SpawnerId) && !spawnerIds.Contains(parsed.SpawnerType)))
                continue;

            WorldIntegration.OnZoneNpcRemove?.Invoke(bcId);
            NpcSpawnRelay.ForgetNpcState(zone.ZoneId, zone.InstanceId, bcId);
        }
    }
}
