using System.Collections.Concurrent;

using AAEmu.Game;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// World-authors <c>tower_def_prog_spawn_targets</c> rows with type <c>DoodadAlmighty</c>.
/// Zone wave start only arms <c>NpcSpawner</c> targets; doodad templates need explicit world
/// placements from <c>TowerDefs.ProgDoodadPlacementsByTowerDefId</c> (no invented offsets).
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
    /// this step's DoodadAlmighty templates at configured world placements.
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

        var placements = ResolvePlacements(towerDef.Id, doodadTargets);
        if (placements.Count == 0)
        {
            Logger.Warn(
                "TowerDefProgDoodads tower={0} step={1}: {2} DoodadAlmighty target(s) but no " +
                "TowerDefs.ProgDoodadPlacementsByTowerDefId entries — not spawning",
                towerDef.Id, step, doodadTargets.Count);
            return;
        }

        var live = ByTower.GetOrAdd(towerDef.Id, _ => []);
        var spawned = 0;
        var despawnByTemplate = doodadTargets
            .GroupBy(t => t.SpawnTargetId)
            .ToDictionary(g => g.Key, g => g.Any(t => t.DespawnOnNextStep));

        foreach (var zoneId in hostZoneIds)
        {
            var world = WorldIntegration.ResolveWorldForZone(zoneId);
            if (world == null)
            {
                Logger.Warn(
                    "TowerDefProgDoodads tower={0} step={1} zoneId={2}: no world instance",
                    towerDef.Id, step, zoneId);
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

                var pos = new WorldSpawnPosition
                {
                    X = place.X,
                    Y = place.Y,
                    Z = place.Z,
                    Yaw = place.Yaw
                };

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

                if (doodad.Transform != null && doodad.Transform.ZoneId == 0)
                    doodad.Transform.ZoneId = zoneId;

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
                "TowerDefProgDoodads tower={0} step={1} spawned={2} placements={3}",
                towerDef.Id, step, spawned, placements.Count);
        }
        else
        {
            Logger.Warn(
                "TowerDefProgDoodads tower={0} step={1} spawned 0 (placements={2})",
                towerDef.Id, step, placements.Count);
        }
    }

    /// <summary>
    /// Match configured placements to this step's doodad templates. Extra config rows for other
    /// templates are ignored; missing templates are reported by the caller when the list is empty.
    /// </summary>
    public static IReadOnlyList<TowerDefProgDoodadPlacement> ResolvePlacements(
        uint towerDefId,
        IReadOnlyList<TowerDefProgSpawnTarget> doodadTargets)
    {
        if (towerDefId == 0 || doodadTargets == null || doodadTargets.Count == 0)
            return [];

        var cfg = AppConfiguration.Instance.TowerDefs?.ProgDoodadPlacementsByTowerDefId;
        if (cfg == null || !cfg.TryGetValue(towerDefId, out var all) || all == null || all.Count == 0)
            return [];

        var wanted = new HashSet<uint>();
        foreach (var t in doodadTargets)
        {
            if (t.SpawnTargetId != 0)
                wanted.Add(t.SpawnTargetId);
        }

        return TowerDefProgDoodadPlacementMatcher.Match(all, wanted);
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
