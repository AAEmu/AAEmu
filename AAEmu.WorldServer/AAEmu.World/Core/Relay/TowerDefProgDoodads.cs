using AAEmu.Game;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

using System.Collections.Concurrent;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// World-authors <c>tower_def_prog_spawn_targets</c> rows with type <c>DoodadAlmighty</c>.
/// Zone wave start only arms <c>NpcSpawner</c> targets; doodad templates are placed from
/// <see cref="ZoneDoodadPlacementCatalog"/> (<c>cells/*/doodad.g</c> under ZoneGameDataRoot).
/// </summary>
public static class TowerDefProgDoodads
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private sealed class LiveDoodad
    {
        public uint ObjId;
        public uint TemplateId;
        public bool DespawnOnNextStep;
    }

    private static readonly ConcurrentDictionary<uint, List<LiveDoodad>> ByTower = new();

    private static bool Disabled =>
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_TOWER_PROG_DOODADS") == "1";

    /// <summary>
    /// After WaveStart: despawn prior-step doodads marked <c>despawn_on_next_step</c>, then spawn
    /// this step's DoodadAlmighty templates at level-pack world placements (once per world).
    /// </summary>
    public static void ApplyStep(TowerDef towerDef, int step, IReadOnlyList<uint> hostZoneIds)
    {
        if (Disabled || towerDef?.Progs == null || hostZoneIds == null || hostZoneIds.Count == 0)
            return;
        if (step < 0 || step >= towerDef.Progs.Count)
            return;

        DespawnMarkedForNextStep(towerDef.Id);

        var prog = towerDef.Progs[step];
        if (prog.SpawnTargets is not { Count: > 0 })
            return;

        var doodadTargets = new List<TowerDefProgSpawnTarget>();
        foreach (var target in prog.SpawnTargets)
        {
            if (target.SpawnTargetId == 0)
                continue;
            if (!string.Equals(target.SpawnTargetType, "DoodadAlmighty", StringComparison.Ordinal))
                continue;
            doodadTargets.Add(target);
        }

        if (doodadTargets.Count == 0)
            return;

        var worlds = DistinctWorldsForHosts(hostZoneIds);
        if (worlds.Count == 0)
        {
            Logger.Warn(
                "TowerDefProgDoodads tower={0} step={1}: no world instance for host zones",
                towerDef.Id, step);
            return;
        }

        var live = ByTower.GetOrAdd(towerDef.Id, _ => []);
        var spawned = 0;
        var despawnByTemplate = doodadTargets
            .GroupBy(t => t.SpawnTargetId)
            .ToDictionary(g => g.Key, g => g.Any(t => t.DespawnOnNextStep));

        foreach (var (world, fallbackZoneId) in worlds)
        {
            var worldName = world.Template?.Name;
            var placements = ResolvePlacements(worldName, doodadTargets);
            if (placements.Count == 0)
            {
                Logger.Warn(
                    "TowerDefProgDoodads tower={0} step={1} world={2}: {3} DoodadAlmighty target(s) " +
                    "but no cells/*/doodad.g placements under ZoneGameDataRoot — not spawning",
                    towerDef.Id, step, worldName ?? "?", doodadTargets.Count);
                continue;
            }

            foreach (var place in placements)
            {
                if (!DoodadManager.Instance.Exist(place.TemplateId))
                {
                    Logger.Warn(
                        "TowerDefProgDoodads unknown doodad template {0} (tower={1} step={2})",
                        place.TemplateId, towerDef.Id, step);
                    continue;
                }

                var owningZoneId = ResolvePlacementZoneId(world, place, fallbackZoneId);
                var pos = BuildSpawnPosition(place, owningZoneId);

                var spawner = new DoodadSpawner
                {
                    ParentWorld = world,
                    Id = 0,
                    UnitId = place.TemplateId,
                    Position = pos
                };

                Doodad doodad;
                try
                {
                    doodad = spawner.Spawn(0);
                }
                catch (Exception ex)
                {
                    Logger.Warn(
                        ex,
                        "TowerDefProgDoodads spawn failed tpl={0} tower={1}",
                        place.TemplateId, towerDef.Id);
                    continue;
                }

                if (doodad == null || doodad.ObjId == 0)
                    continue;

                WorldIntegration.RelayCreateDoodadToZone?.Invoke(doodad);
                var despawnNext = despawnByTemplate.GetValueOrDefault(place.TemplateId, true);
                lock (live)
                {
                    live.Add(new LiveDoodad
                    {
                        ObjId = doodad.ObjId,
                        TemplateId = place.TemplateId,
                        DespawnOnNextStep = despawnNext
                    });
                }

                spawned++;
            }
        }

        if (spawned > 0)
        {
            Logger.Info(
                "TowerDefProgDoodads tower={0} step={1} spawned={2} worlds={3}",
                towerDef.Id, step, spawned, worlds.Count);
        }
        else
        {
            Logger.Warn(
                "TowerDefProgDoodads tower={0} step={1} spawned 0",
                towerDef.Id, step);
        }
    }

    /// <summary>
    /// One entry per distinct <see cref="WorldInstance"/> among host zones (first zone is fallback).
    /// </summary>
    public static IReadOnlyList<(WorldInstance World, uint FallbackZoneId)> DistinctWorldsForHosts(
        IReadOnlyList<uint> hostZoneIds)
    {
        if (hostZoneIds == null || hostZoneIds.Count == 0)
            return [];

        var byWorldId = new Dictionary<uint, (WorldInstance World, uint FallbackZoneId)>();
        foreach (var zoneId in hostZoneIds)
        {
            if (zoneId == 0)
                continue;
            var world = WorldIntegration.ResolveWorldForZone(zoneId);
            if (world == null)
            {
                Logger.Warn("TowerDefProgDoodads zoneId={0}: no world instance", zoneId);
                continue;
            }

            byWorldId.TryAdd(world.Id, (world, zoneId));
        }

        return byWorldId.Values.ToList();
    }

    /// <summary>
    /// Placement zone ownership: optional <see cref="TowerDefProgDoodadPlacement.ZoneId"/>,
    /// else world template lookup from XYZ, else host fallback.
    /// </summary>
    public static uint ResolvePlacementZoneId(
        WorldInstance world,
        TowerDefProgDoodadPlacement place,
        uint fallbackZoneId)
    {
        if (place != null && place.ZoneId != 0)
            return place.ZoneId;
        if (world?.Template != null && place != null)
        {
            var fromPos = WorldManager.Instance.GetZoneId(world.Template, place.X, place.Y);
            if (fromPos != 0)
                return fromPos;
        }

        return fallbackZoneId;
    }

    /// <summary>
    /// Spawn position with ownership set before <see cref="DoodadSpawner.Spawn"/> so the first
    /// observable transform/region registration already carries the correct zone.
    /// </summary>
    public static WorldSpawnPosition BuildSpawnPosition(TowerDefProgDoodadPlacement place, uint owningZoneId)
    {
        ArgumentNullException.ThrowIfNull(place);
        return new WorldSpawnPosition
        {
            X = place.X,
            Y = place.Y,
            Z = place.Z,
            ZoneId = owningZoneId,
            // Permanent doodad_spawns.json stores degrees; SpawnManager converts on load.
            Yaw = place.Yaw.DegToRad()
        };
    }

    /// <summary>
    /// Collapse host zones to one entry per world key (first zone wins as fallback). Pure helper
    /// for the multi-zone contract; <see cref="DistinctWorldsForHosts"/> uses the live resolver.
    /// </summary>
    public static IReadOnlyList<(uint WorldId, uint FallbackZoneId)> DistinctWorldKeysForHosts(
        IReadOnlyList<uint> hostZoneIds,
        Func<uint, uint?> resolveWorldId)
    {
        if (hostZoneIds == null || hostZoneIds.Count == 0 || resolveWorldId == null)
            return [];

        var byWorldId = new Dictionary<uint, uint>();
        foreach (var zoneId in hostZoneIds)
        {
            if (zoneId == 0)
                continue;
            var worldId = resolveWorldId(zoneId);
            if (worldId is null or 0)
                continue;
            byWorldId.TryAdd(worldId.Value, zoneId);
        }

        return byWorldId.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Match level-pack placements to this step's doodad templates. Missing templates ⇒ empty list
    /// (caller warns); no invented coordinates.
    /// </summary>
    public static IReadOnlyList<TowerDefProgDoodadPlacement> ResolvePlacements(
        string worldName,
        IReadOnlyList<TowerDefProgSpawnTarget> doodadTargets)
    {
        if (string.IsNullOrWhiteSpace(worldName) || doodadTargets == null || doodadTargets.Count == 0)
            return [];

        var wanted = new HashSet<uint>();
        foreach (var t in doodadTargets)
        {
            if (t.SpawnTargetId != 0)
                wanted.Add(t.SpawnTargetId);
        }

        if (wanted.Count == 0)
            return [];

        var fromLevel = ZoneDoodadPlacementCatalog.GetByTemplates(worldName, wanted);
        if (fromLevel.Count == 0)
            return [];

        var found = new HashSet<uint>();
        var list = new List<TowerDefProgDoodadPlacement>(fromLevel.Count);
        foreach (var p in fromLevel)
        {
            found.Add(p.TemplateId);
            list.Add(new TowerDefProgDoodadPlacement
            {
                TemplateId = p.TemplateId,
                X = p.X,
                Y = p.Y,
                Z = p.Z,
                Yaw = p.YawDegrees
            });
        }

        foreach (var id in wanted)
        {
            if (!found.Contains(id))
            {
                Logger.Warn(
                    "TowerDefProgDoodads world={0}: DoodadAlmighty template {1} has no cells/*/doodad.g placement",
                    worldName, id);
            }
        }

        return list;
    }

    /// <summary>Remove every World-authored prog doodad for this tower (End / restart).</summary>
    public static int DespawnAll(uint towerDefId)
    {
        if (towerDefId == 0 || !ByTower.TryRemove(towerDefId, out var live) || live == null)
            return 0;

        List<LiveDoodad> snapshot;
        lock (live)
            snapshot = [.. live];

        return DeleteMany(snapshot);
    }

    private static void DespawnMarkedForNextStep(uint towerDefId)
    {
        if (!ByTower.TryGetValue(towerDefId, out var live) || live == null)
            return;

        List<LiveDoodad> doomed;
        lock (live)
        {
            doomed = live.Where(d => d.DespawnOnNextStep).ToList();
            live.RemoveAll(d => d.DespawnOnNextStep);
        }

        if (doomed.Count > 0)
            DeleteMany(doomed);
    }

    private static int DeleteMany(IReadOnlyList<LiveDoodad> entries)
    {
        var n = 0;
        foreach (var entry in entries)
        {
            try
            {
                var doodad = FindDoodad(entry.ObjId);
                if (doodad == null)
                    continue;
                doodad.Delete();
                n++;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "TowerDefProgDoodads delete failed obj={0} tpl={1}", entry.ObjId, entry.TemplateId);
            }
        }

        return n;
    }

    private static Doodad FindDoodad(uint objId)
    {
        if (objId == 0)
            return null;
        foreach (var world in WorldManager.Instance.GetWorlds() ?? [])
        {
            var d = world.GetDoodad(objId);
            if (d != null)
                return d;
        }

        return null;
    }
}
