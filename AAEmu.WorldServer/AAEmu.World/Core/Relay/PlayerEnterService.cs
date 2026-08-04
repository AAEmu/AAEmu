using System.Collections.Concurrent;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Zone;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Commercial enter/leave + multi-zone routing.
/// </summary>
public class PlayerEnterService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Zone keys already reported as unhosted; used as a set (value is unused).</summary>
    private static readonly ConcurrentDictionary<uint, byte> WarnedMissingZones = new();

    public bool EnterZone(uint bcId, byte[] unitStateBody)
    {
        var zone = ForCharacter(bcId);
        if (zone == null)
        {
            Logger.Warn(
                "EnterZone refused: no ZoneLoaded for character bcId={0} zoneId={1}",
                bcId, ResolveCharacterZoneId(bcId));
            return false;
        }

        if (unitStateBody == null || unitStateBody.Length == 0)
        {
            Logger.Warn("EnterZone refused: empty WZUnitState body (bcId={0})", bcId);
            return false;
        }

        zone.SendPacket(new WZUnitStatePacket(unitStateBody));
        zone.Units.RegisterWithId(bcId, unitStateBody);
        var character = FindActiveCharacter(bcId);
        ActivateNpcSpawnersNearPlayer(zone, character);
        SyncExpedition(zone, character);
        Logger.Info(
            "WZUnitState enter → zoneId={0} ip={1} bcHint={2} bodyLen={3}",
            zone.ZoneId, zone.Ip, bcId, unitStateBody.Length);
        return true;
    }

    public void LeaveZone(uint bcId)
    {
        var zone = ForCharacter(bcId) ?? FindZoneTrackingUnit(bcId);
        if (zone == null)
            return;

        zone.SendPacket(new WZUnitRemovedPacket(bcId));
        zone.Units.TryRemove(bcId);
        Logger.Info("WZUnitRemoved leave → zoneId={0} ip={1} bcId={2}", zone.ZoneId, zone.Ip, bcId);
    }

    /// <summary>
    /// Fail-closed if new zone is not ZoneLoaded.
    /// </summary>
    public static bool HandoffOnZoneChange(uint bcId, uint oldZoneId, uint newZoneId, byte[] unitStateBody)
    {
        var oldZone = ForZoneId(oldZoneId) ?? FindZoneTrackingUnit(bcId);
        if (oldZone != null)
        {
            oldZone.SendPacket(new WZUnitRemovedPacket(bcId));
            oldZone.Units.TryRemove(bcId);
            Logger.Info(
                "Zone handoff leave → oldZoneId={0} bcId={1}",
                oldZone.ZoneId, bcId);
        }
        else if (oldZoneId != 0)
        {
            Logger.Warn("Zone handoff: no old zone connection zoneId={0} bcId={1}", oldZoneId, bcId);
        }

        var newZone = ForZoneId(newZoneId);
        if (newZone == null)
        {
            Logger.Warn(
                "Zone handoff refused: no ZoneLoaded for newZoneId={0} bcId={1}",
                newZoneId, bcId);
            return false;
        }

        if (unitStateBody == null || unitStateBody.Length == 0)
        {
            Logger.Warn("Zone handoff refused: empty WZUnitState body bcId={0}", bcId);
            return false;
        }

        newZone.SendPacket(new WZUnitStatePacket(unitStateBody));
        newZone.Units.RegisterWithId(bcId, unitStateBody);
        var character = FindActiveCharacter(bcId);
        ActivateNpcSpawnersNearPlayer(newZone, character);
        SyncExpedition(newZone, character);
        Logger.Info(
            "Zone handoff enter → newZoneId={0} bcId={1} bodyLen={2}",
            newZone.ZoneId, bcId, unitStateBody.Length);
        return true;
    }

    private static void ActivateNpcSpawnersNearPlayer(ZoneConnection zone, Character? character)
    {
        var cfg = global::AAEmu.World.WorldRuntime.Config.NpcSpawnerActivate;
        if (!cfg.Enabled || character?.Transform == null)
            return;

        // Gate the flood this triggers; the first player can enter before the refresh timer ticks.
        NpcScheduleGate.EnsureLoaded();

        var radius = float.IsFinite(cfg.Radius) && cfg.Radius > 0f ? cfg.Radius : 1024f;
        var position = character.Transform.World.Position;
        // spawn points, which are the raw zone-local values from npc_spawners.g. Sending world
        // coordinates put the circle a whole zone-origin away, so it never armed a spawner.
        var local = ZoneManager.Instance.ConvertToLocalCoordinates(zone.ZoneId, position);
        zone.SendPacket(new WZActivateNpcSpawnersInAreaPacket(
            local.X, local.Y, local.Z, radius, activate: true));
        Logger.Info(
            "WZActivateNpcSpawnersInArea player-scoped -> zoneId={0} bcId={1} local=({2:F1},{3:F1},{4:F1}) world=({5:F1},{6:F1},{7:F1}) r={8:F0}",
            zone.ZoneId, character.ObjId, local.X, local.Y, local.Z,
            position.X, position.Y, position.Z, radius);
    }

    private static void SyncExpedition(ZoneConnection zone, Character? character)
    {
        if (character?.Expedition is not { } expedition)
            return;

        zone.SendPacket(new WZUnitExpeditionChangedPacket(character.ObjId, 0, (int)expedition.Id));
    }

    /// <summary>
    /// ZoneLoaded connection for a unit's Transform.ZoneId (player, NPC, or doodad).
    /// Optional AAEMU_ZONE_PRIMARY_FALLBACK=1 falls back to first loaded zone (single-zone debug).
    /// </summary>
    public static ZoneConnection? ForUnit(uint unitObjId)
    {
        var zoneId = ResolveUnitZoneId(unitObjId);
        if (zoneId != 0)
        {
            var byZone = ForZoneId(zoneId);
            if (byZone != null)
                return byZone;
            WarnZoneMissing(zoneId, "ForUnit", "unit", unitObjId);
        }

        return PrimaryFallback();
    }

    /// <summary>
    /// One warning per zone key, not per unit. Every doodad, NPC and mirror in an unhosted zone
    /// hits this path: the doodad spawn pass alone drove 16k lines of the same warning across 94
    /// zones, and NLog writes them synchronously. Cleared by <see cref="OnZoneLoaded"/> so a zone
    /// that drops out later reports again.
    /// </summary>
    private static void WarnZoneMissing(uint zoneId, string caller, string unitKind, uint objId)
    {
        if (!WarnedMissingZones.TryAdd(zoneId, 0))
            return;

        Logger.Warn(
            "{0}: no ZoneLoaded for zoneId={1} ({2}={3}); further units in this zone will not be logged until it loads",
            caller, zoneId, unitKind, objId);
    }

    /// <summary>Re-arms the missing-zone warning once that zone is hosted again.</summary>
    public static void OnZoneLoaded(uint zoneId) => WarnedMissingZones.TryRemove(zoneId, out _);

    /// <summary>
    /// Player enter/leave — must use the Character object, not MainWorld.GetBaseUnit.
    /// Mirror NPCs can steal the same ObjId; GetBaseUnit then returns the wrong unit (or null).
    /// </summary>
    public static ZoneConnection? ForCharacter(uint unitObjId)
    {
        var zoneId = ResolveCharacterZoneId(unitObjId);
        if (zoneId != 0)
        {
            var byZone = ForZoneId(zoneId);
            if (byZone != null)
                return byZone;
            WarnZoneMissing(zoneId, "ForCharacter", "charObjId", unitObjId);
        }

        return PrimaryFallback();
    }

    /// <summary>ZoneLoaded connection for a zone key.</summary>
    public static ZoneConnection? ForZoneId(uint zoneId) =>
        ZoneSession.Instance.GetByZoneId(zoneId);

    /// <summary>First zone that finished bring-online (legacy / doodad flush without zone context).</summary>
    public static ZoneConnection? PrimaryZone()
    {
        return ZoneSession.Instance.All
            .FirstOrDefault(z => z.State >= ZoneConnectionState.ZoneLoaded);
    }

    /// <summary>Joined but not yet loaded — still not enter-ready.</summary>
    public static ZoneConnection? AnyJoinedZone()
    {
        return ZoneSession.Instance.All
            .FirstOrDefault(z => z.State >= ZoneConnectionState.Joined);
    }

    /// <summary>All ZoneLoaded dedicades (siege / mole / gimmick-remove fan-out).</summary>
    public static IEnumerable<ZoneConnection> AllLoadedZones() =>
        ZoneSession.Instance.All.Where(z => z.State >= ZoneConnectionState.ZoneLoaded);

    private static ZoneConnection? PrimaryFallback()
    {
        if (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") != "1")
            return null;
        var zone = PrimaryZone();
        if (zone != null)
            Logger.Warn("Using PrimaryZone fallback zoneId={0} (AAEMU_ZONE_PRIMARY_FALLBACK=1)", zone.ZoneId);
        return zone;
    }

    private static Character? FindActiveCharacter(uint objId)
    {
        if (objId == 0)
            return null;

        foreach (var con in GameConnectionTable.Instance.GetConnections())
        {
            var ch = con?.ActiveChar;
            if (ch != null && ch.ObjId == objId)
                return ch;
        }

        return WorldManager.Instance.GetCharacterByObjId(objId);
    }

    /// <summary>Zone key for a player ObjId — prefers ActiveChar over colliding MainWorld entries.</summary>
    private static uint ResolveCharacterZoneId(uint unitObjId)
    {
        var ch = FindActiveCharacter(unitObjId);
        if (ch?.Transform == null)
            return 0;

        if (ch.Transform.ZoneId != 0)
            return ch.Transform.ZoneId;

        var world = ch.ParentWorld ?? WorldManager.Instance.MainWorld;
        if (world?.Template == null)
            return 0;

        var pos = ch.Transform.World.Position;
        var resolved = WorldManager.Instance.GetZoneId(world.Template, pos.X, pos.Y);
        if (resolved != 0)
        {
            ch.Transform.ZoneId = resolved;
            Logger.Info(
                "Resolved character {0} ({1}) ZoneId 0 → {2} from position ({3:F1},{4:F1})",
                unitObjId, ch.Name, resolved, pos.X, pos.Y);
        }

        return resolved;
    }

    private static uint ResolveUnitZoneId(uint unitObjId)
    {
        if (unitObjId == 0)
            return 0;

        // Characters first — ObjId may collide with a mirror NPC still in MainWorld._baseUnits.
        var charZone = ResolveCharacterZoneId(unitObjId);
        if (charZone != 0)
            return charZone;

        // Mirrors of a non-main world (arche_mall etc.) are not in MainWorld — search every instance.
        var unit = AAEmu.Game.WorldIntegration.FindUnitAcrossWorlds(unitObjId);
        if (unit is Character)
        {
            // Character in _baseUnits but not ActiveChar yet — still use their transform.
            if (unit.Transform?.ZoneId is > 0)
                return unit.Transform.ZoneId;
        }
        else if (unit?.Transform != null)
        {
            if (unit.Transform.ZoneId != 0)
                return unit.Transform.ZoneId;

            var template = (unit.ParentWorld ?? WorldManager.Instance.MainWorld)?.Template;
            var pos = unit.Transform.World.Position;
            var resolved = template == null ? 0 : WorldManager.Instance.GetZoneId(template, pos.X, pos.Y);
            if (resolved != 0)
            {
                unit.Transform.ZoneId = resolved;
                Logger.Info("Resolved unit {0} ZoneId 0 → {1} from position ({2:F1},{3:F1})",
                    unitObjId, resolved, pos.X, pos.Y);
                return resolved;
            }
        }

        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            var doodad = world?.GetDoodad(unitObjId);
            if (doodad?.Transform == null)
                continue;
            if (doodad.Transform.ZoneId != 0)
                return doodad.Transform.ZoneId;

            var dPos = doodad.Transform.World.Position;
            return WorldManager.Instance.GetZoneId(world.Template, dPos.X, dPos.Y);
        }

        return 0;
    }

    private static ZoneConnection? FindZoneTrackingUnit(uint bcId)
    {
        foreach (var z in ZoneSession.Instance.All)
        {
            if (z.State >= ZoneConnectionState.Joined && z.Units.Contains(bcId))
                return z;
        }

        return null;
    }
}
