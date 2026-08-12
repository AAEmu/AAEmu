using System.Numerics;

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
public static class TowerDefWaveForce
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Max distance (m) from seed portal to a step placement of the arm type.
    /// Override with World <c>TowerDef.WaveSpotRadiusMetres</c> or env <c>AAEMU_TOWER_WAVE_SPOT_RADIUS</c>.
    /// </summary>
    private static float SpotRadiusMetres
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("AAEMU_TOWER_WAVE_SPOT_RADIUS");
            if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var metres) &&
                metres > 0f)
                return metres;

            var configured = WorldRuntime.Config?.TowerDef?.WaveSpotRadiusMetres ?? 0f;
            return configured > 0f ? configured : 80f;
        }
    }

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

            var placements = ZoneSpawnerPlacementCatalog.GetAll(zoneId);
            var fired = 0;
            foreach (var p in placements)
            {
                if (!wanted.Contains(p.SpawnerType))
                    continue;

                if (!SendTowerRespawn(zone, creator, p.PlacementId))
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

            var placements = ZoneSpawnerPlacementCatalog.GetAll(zoneId);
            var r2 = SpotRadiusMetres * SpotRadiusMetres;
            var firedIds = new List<uint>();

            foreach (var sType in wantedTypes.OrderBy(x => x))
            {
                ZoneSpawnerPlacementCatalog.SpawnerPlacement? best = null;
                var bestD2 = float.MaxValue;
                foreach (var p in placements)
                {
                    if (p.SpawnerType != sType)
                        continue;
                    var world = ZoneManager.Instance.ConvertToWorldCoordinates(
                        zoneId, new Vector3(p.X, p.Y, p.Z));
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

                if (!SendTowerRespawn(zone, creator, best.Value.PlacementId))
                    continue;
                firedIds.Add(best.Value.PlacementId);
                total++;
                Logger.Info(
                    "TowerDefOnEventForce zoneId={0} placement={1} sType={2} d={3:F1}m ({4})",
                    zoneId, best.Value.PlacementId, sType, MathF.Sqrt(bestD2), reason);
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
            foreach (var p in ZoneSpawnerPlacementCatalog.GetByType(zoneId, towerDef.TargetNpcSpawnId))
            {
                list.Add(ZoneManager.Instance.ConvertToWorldCoordinates(
                    zoneId, new Vector3(p.X, p.Y, p.Z)));
            }
        }

        return list;
    }

    public static void InvalidatePlacementCache(uint zoneId = 0)
    {
        ZoneSpawnerPlacementCatalog.Invalidate(zoneId);
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

    private static float DistanceSq(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
