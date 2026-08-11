using System.Collections.Concurrent;
using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Optional re-arm of <c>WZNpcSpawnerEvent</c> (TowerDefense / RespawnAllOnce) for tower_def wave
/// spawn targets. ChangeStep can report success while type-1 emit is silent (validation or
/// maxPop caps), so stage portals may never send ZW without a second arm.
/// </summary>
/// <remarks>
/// Off by default. Wire body must use reason Default (no extra reason payload); wrong packing
/// previously size-mismatched the zone deserializer and crashed the process. Enable with
/// <c>AAEMU_TOWER_WAVE_FORCE=1</c>. Portal re-arm is separate: <c>AAEMU_TOWER_PORTAL_FORCE=1</c>.
/// When on, only the placement of each wanted sType nearest the live seed portal is fired.
/// </remarks>
public static partial class TowerDefWaveForce
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private sealed record Placement(uint Id, uint Type, float LocalX, float LocalY, float LocalZ);

    private static readonly ConcurrentDictionary<uint, IReadOnlyList<Placement>> PlacementCache = new();

    private static readonly Regex SpawnerIdRegex = SpawnerIdPattern();
    private static readonly Regex SpawnerTypeRegex = SpawnerTypePattern();
    private static readonly Regex PosRegex = PosPattern();
    private static readonly Regex SplitBlocksRegex = SplitSpawnerBlocks();

    /// <summary>Max distance (m) from seed portal to a step placement of the arm type.</summary>
    private const float SpotRadiusMetres = 80f;

    private static bool WaveForceEnabled
    {
        get
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_TOWER_WAVE_FORCE") == "1")
                return false;
            // Opt-in only — mis-sized WZNpcSpawnerEvent bodies have crashed dedicated zones.
            return Environment.GetEnvironmentVariable("AAEMU_TOWER_WAVE_FORCE") == "1";
        }
    }

    private static bool PortalForceEnabled =>
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_TOWER_PORTAL_FORCE") != "1"
        && Environment.GetEnvironmentVariable("AAEMU_TOWER_PORTAL_FORCE") == "1";

    /// <summary>
    /// After WaveStart when force is enabled: arm nearest g-file placement per prog spawn sType
    /// next to the live seed portal on each host zone.
    /// </summary>
    public static void ArmProgSpawners(TowerDef towerDef, int step, IReadOnlyList<uint> hostZoneIds)
    {
        if (!WaveForceEnabled || towerDef?.Progs == null || hostZoneIds == null || hostZoneIds.Count == 0)
            return;
        if (step < 0 || step >= towerDef.Progs.Count)
            return;

        var prog = towerDef.Progs[step];
        if (prog.SpawnTargets is not { Count: > 0 })
            return;

        var wantedTypes = new HashSet<uint>();
        foreach (var target in prog.SpawnTargets)
        {
            if (target.SpawnTargetId == 0)
                continue;
            if (!string.Equals(target.SpawnTargetType, "NpcSpawner", StringComparison.Ordinal))
                continue;
            wantedTypes.Add(target.SpawnTargetId);
        }

        if (wantedTypes.Count == 0)
            return;

        ArmNearActivePortal(
            towerDef,
            hostZoneIds,
            wantedTypes,
            $"wave tower={towerDef.Id} step={step}");
    }

    /// <summary>
    /// After WZ Start only when <c>AAEMU_TOWER_PORTAL_FORCE=1</c>.
    /// </summary>
    public static void ArmPortalTargets(TowerDef towerDef, IReadOnlyList<uint> hostZoneIds)
    {
        if (!PortalForceEnabled || towerDef == null || towerDef.TargetNpcSpawnId == 0)
            return;
        if (hostZoneIds == null || hostZoneIds.Count == 0)
            return;

        ArmSpawnerTypes(
            hostZoneIds,
            [towerDef.TargetNpcSpawnId],
            $"portal tower={towerDef.Id} sType={towerDef.TargetNpcSpawnId}");
    }

    /// <summary>
    /// Fire RespawnAllOnce / TowerDefense on every g placement of the given types (all spots).
    /// Prefer <see cref="ArmNearActivePortal"/> for wave steps.
    /// </summary>
    public static void ArmSpawnerTypes(
        IReadOnlyList<uint> hostZoneIds,
        IReadOnlyCollection<uint> spawnerTypes,
        string reason)
    {
        if (hostZoneIds == null || hostZoneIds.Count == 0 || spawnerTypes == null || spawnerTypes.Count == 0)
            return;

        var wanted = spawnerTypes is HashSet<uint> hs ? hs : spawnerTypes.ToHashSet();
        var total = 0;
        foreach (var zoneId in hostZoneIds)
        {
            var zone = ZoneSession.Instance.GetByZoneId(zoneId);
            if (zone == null || zone.State < ZoneConnectionState.ZoneLoaded)
                continue;

            var creator = ResolveCreator(zoneId, towerDef: null);
            if (creator == null)
            {
                Logger.Warn(
                    "TowerDefOnEventForce skip zoneId={0} ({1}) — no character/NPC creator in zone",
                    zoneId, reason);
                continue;
            }

            var placements = PlacementCache.GetOrAdd(zoneId, LoadPlacements);
            var fired = 0;
            foreach (var p in placements)
            {
                if (!wanted.Contains(p.Type))
                    continue;

                if (!SendTowerRespawn(zone, creator, p.Id))
                    continue;
                fired++;
                total++;
            }

            if (fired > 0)
            {
                Logger.Info(
                    "TowerDefOnEventForce zoneId={0} placements={1} types=[{2}] ({3})",
                    zoneId, fired, string.Join(',', wanted.OrderBy(x => x)), reason);
            }
        }

        if (total == 0)
            Logger.Warn(
                "TowerDefOnEventForce armed 0 placements across {0} host zone(s) types=[{1}] ({2})",
                hostZoneIds.Count, string.Join(',', wanted.OrderBy(x => x)), reason);
    }

    /// <summary>
    /// One placement per wanted sType: nearest to a live seed portal of this tower in that zone.
    /// Infantry (e.g. 9844/9852) often have zero g placements — stage summoner 9848→8830 owns that.
    /// </summary>
    private static void ArmNearActivePortal(
        TowerDef towerDef,
        IReadOnlyList<uint> hostZoneIds,
        HashSet<uint> wantedTypes,
        string reason)
    {
        var total = 0;
        foreach (var zoneId in hostZoneIds)
        {
            var zone = ZoneSession.Instance.GetByZoneId(zoneId);
            if (zone == null || zone.State < ZoneConnectionState.ZoneLoaded)
                continue;

            var creator = ResolveCreator(zoneId, towerDef);
            if (creator == null)
            {
                Logger.Warn(
                    "TowerDefOnEventForce skip zoneId={0} ({1}) — no character/NPC creator in zone",
                    zoneId, reason);
                continue;
            }

            var anchors = CollectPortalWorldAnchors(towerDef, zoneId);
            if (anchors.Count == 0)
            {
                Logger.Warn(
                    "TowerDefOnEventForce zoneId={0} ({1}) — no seed portal anchor (tpl for sType {2})",
                    zoneId, reason, towerDef.TargetNpcSpawnId);
                continue;
            }

            var placements = PlacementCache.GetOrAdd(zoneId, LoadPlacements);
            var r2 = SpotRadiusMetres * SpotRadiusMetres;
            var firedIds = new List<uint>();

            foreach (var sType in wantedTypes.OrderBy(x => x))
            {
                Placement best = null;
                var bestD2 = float.MaxValue;
                foreach (var p in placements)
                {
                    if (p.Type != sType)
                        continue;
                    var world = ZoneManager.Instance.ConvertToWorldCoordinates(
                        zoneId, new Vector3(p.LocalX, p.LocalY, p.LocalZ));
                    foreach (var a in anchors)
                    {
                        var d2 = DistanceSq(world, a);
                        if (d2 > r2 || d2 >= bestD2)
                            continue;
                        bestD2 = d2;
                        best = p;
                    }
                }

                if (best == null)
                {
                    Logger.Warn(
                        "TowerDefOnEventForce zoneId={0} no placement sType={1} within {2}m of seed ({3})",
                        zoneId, sType, SpotRadiusMetres, reason);
                    continue;
                }

                if (!SendTowerRespawn(zone, creator, best.Id))
                    continue;
                firedIds.Add(best.Id);
                total++;
                Logger.Info(
                    "TowerDefOnEventForce zoneId={0} placement={1} sType={2} d={3:F1}m ({4})",
                    zoneId, best.Id, sType, MathF.Sqrt(bestD2), reason);
            }
        }

        if (total == 0)
            Logger.Warn(
                "TowerDefOnEventForce armed 0 near-portal placements types=[{0}] ({1})",
                string.Join(',', wantedTypes.OrderBy(x => x)), reason);
    }

    private static List<Vector3> CollectPortalWorldAnchors(TowerDef towerDef, uint zoneId)
    {
        var list = new List<Vector3>();
        var seedNpcs = TowerDefGameData.Instance.GetSpawnerMemberNpcIds(towerDef.TargetNpcSpawnId);
        if (seedNpcs is { Count: > 0 })
        {
            foreach (var world in WorldManager.Instance.GetWorlds() ?? [])
            {
                foreach (var npc in world.GetAllNpcs())
                {
                    if (npc is not { IsZoneMirror: true } || npc.Transform?.ZoneId != zoneId)
                        continue;
                    if (!seedNpcs.Contains(npc.TemplateId))
                        continue;
                    list.Add(npc.Transform.World.Position);
                }
            }
        }

        // Cold / not yet mirrored: use g-file portal points for this zone's sType.
        if (list.Count == 0 && towerDef.TargetNpcSpawnId != 0)
        {
            var placements = PlacementCache.GetOrAdd(zoneId, LoadPlacements);
            foreach (var p in placements)
            {
                if (p.Type != towerDef.TargetNpcSpawnId)
                    continue;
                list.Add(ZoneManager.Instance.ConvertToWorldCoordinates(
                    zoneId, new Vector3(p.LocalX, p.LocalY, p.LocalZ)));
            }
        }

        return list;
    }

    public static void InvalidatePlacementCache(uint zoneId = 0)
    {
        if (zoneId == 0)
            PlacementCache.Clear();
        else
            PlacementCache.TryRemove(zoneId, out _);
    }

    private static bool SendTowerRespawn(ZoneConnection zone, BaseUnit creator, uint placementId)
    {
        try
        {
            var creatorType = creator switch
            {
                Character => BaseUnitType.Character,
                Npc => BaseUnitType.Npc,
                _ => BaseUnitType.Invalid
            };
            if (creatorType == BaseUnitType.Invalid)
                return false;

            var characterId = creator is Character ch ? ch.Id : 0UL;
            var ownerId = creator is Npc npc ? npc.OwnerId : 0UL;
            var flag = creator is Npc n ? n.UnitStateFlag : (byte)0;

            var request = new WorldNpcSpawnerEventRequest(
                creator.ObjId,
                creatorType,
                characterId,
                0L,
                creator.TemplateId,
                ownerId,
                flag,
                placementId,
                NpcSpawnerEvent.RespawnAllOnce,
                NpcSpawnerEventType.TowerDefense,
                0f,
                false,
                false);

            zone.SendPacket(new WZNpcSpawnerEventPacket(request));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "TowerDefWaveForce event failed placement={0}", placementId);
            return false;
        }
    }

    private static BaseUnit ResolveCreator(uint zoneId, TowerDef towerDef)
    {
        // Prefer the seed portal itself when it already exists as a mirror.
        if (towerDef != null)
        {
            var seedNpcs = TowerDefGameData.Instance.GetSpawnerMemberNpcIds(towerDef.TargetNpcSpawnId);
            if (seedNpcs is { Count: > 0 })
            {
                foreach (var world in WorldManager.Instance.GetWorlds() ?? [])
                {
                    foreach (var npc in world.GetAllNpcs())
                    {
                        if (npc is not { IsZoneMirror: true } || npc.Transform?.ZoneId != zoneId)
                            continue;
                        if (seedNpcs.Contains(npc.TemplateId))
                            return npc;
                    }
                }
            }
        }

        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            if (character?.Transform == null)
                continue;
            if (character.Transform.ZoneId == zoneId)
                return character;
        }

        foreach (var world in WorldManager.Instance.GetWorlds() ?? [])
        {
            foreach (var npc in world.GetAllNpcs())
            {
                if (npc is not { IsZoneMirror: true } || npc.Transform?.ZoneId != zoneId)
                    continue;
                return npc;
            }
        }

        return null;
    }

    private static IReadOnlyList<Placement> LoadPlacements(uint zoneId)
    {
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world == null)
            return [];

        var path = ResolveSpawnerFile(zoneId, world.Name);
        if (path == null)
        {
            Logger.Warn("TowerDefWaveForce: no npc_spawners.g for zoneId={0}", zoneId);
            return [];
        }

        try
        {
            var text = File.ReadAllText(path);
            var blocks = SplitBlocksRegex.Split(text);
            var list = new List<Placement>(blocks.Length);
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block))
                    continue;
                var idMatch = SpawnerIdRegex.Match(block);
                var typeMatch = SpawnerTypeRegex.Match(block);
                if (!idMatch.Success || !typeMatch.Success)
                    continue;
                if (!uint.TryParse(idMatch.Groups[1].Value, out var id))
                    continue;
                if (!uint.TryParse(typeMatch.Groups[1].Value, out var type))
                    continue;
                var posMatch = PosRegex.Match(block);
                var x = 0f;
                var y = 0f;
                var z = 0f;
                if (posMatch.Success)
                {
                    float.TryParse(posMatch.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out x);
                    float.TryParse(posMatch.Groups[2].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out y);
                    float.TryParse(posMatch.Groups[3].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out z);
                }

                list.Add(new Placement(id, type, x, y, z));
            }

            Logger.Info(
                "TowerDefWaveForce catalog zoneId={0} placements={1} from {2}",
                zoneId, list.Count, path);
            return list;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "TowerDefWaveForce failed to parse {0}", path);
            return [];
        }
    }

    private static string ResolveSpawnerFile(uint zoneId, string worldName)
    {
        foreach (var root in EnumerateGameRoots())
        {
            var path = Path.Combine(
                root,
                "worlds",
                worldName,
                "level_design",
                "zone",
                zoneId.ToString(),
                "zone_server",
                "npc_spawners.g");
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateGameRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            var full = Path.GetFullPath(candidate.Trim());
            if (Directory.Exists(full))
                seen.Add(full);
        }

        Offer(WorldRuntime.Config.ZoneGameDataRoot);
        Offer(Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT"));
        Offer(@"G:\AAchina\Server\game");
        return seen;
    }

    private static float DistanceSq(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    [GeneratedRegex(@"spawnerId\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerIdPattern();

    [GeneratedRegex(@"spawnerType\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerTypePattern();

    [GeneratedRegex(
        @"pos\s*\(\s*x\s*([-\d.]+),\s*y\s*([-\d.]+),\s*z\s*([-\d.]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PosPattern();

    [GeneratedRegex(@"(?m)^spawner\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SplitSpawnerBlocks();
}
