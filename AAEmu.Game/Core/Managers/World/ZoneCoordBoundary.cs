using System.Numerics;

using AAEmu.Game.Models.Game.Units.Movements;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// World-side unit-space remapping at the zone TCP boundary.
/// See <see cref="WzCoordPolicy"/> for the retail rule (all zones, origin from world.xml).
/// Spawner geometry packets (Activate circle) always convert continent → zone-local.
/// Live WZ stays continent unless <see cref="UseLocalOnZoneWire"/> is opted in (debug only).
/// </summary>
public static class ZoneCoordBoundary
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// When true, World rewrites WZ unit/doodad/gimmick placement + moves into zone-local and ZW moves into world.
    /// Default <c>false</c> (continent on WZ). Opt in: <c>AAEMU_WZ_UNIT_POS_LOCAL=1</c>.
    /// World-authored event NPCs use continent WZ Create (same as the player).
    /// </summary>
    public static bool UseLocalOnZoneWire { get; } = ResolveUseLocalOnZoneWire();

    static ZoneCoordBoundary()
    {
        Logger.Info(
            "ZoneCoordBoundary: UseLocalOnZoneWire={0} (default continent on WZ; LOCAL=1 rewrites to zone-local)",
            UseLocalOnZoneWire);
    }

    public static bool ResolveUseLocalOnZoneWire() =>
        ResolveUseLocalOnZoneWire(
            Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_POS_WORLD"),
            Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_POS_LOCAL"));

    /// <summary>
    /// LOCAL=1 → zone-local on the zone wire. WORLD=1, LOCAL=0, or unset → continent.
    /// </summary>
    public static bool ResolveUseLocalOnZoneWire(string worldEnv, string localEnv)
    {
        if (IsValue(worldEnv, "1"))
            return false;
        if (IsValue(localEnv, "0"))
            return false;
        return IsValue(localEnv, "1");
    }

    private static bool IsValue(string env, string value) =>
        string.Equals(env, value, StringComparison.Ordinal);

    public static Vector3 ToZoneLocal(uint zoneId, Vector3 worldPos, bool force = false)
    {
        if (zoneId == 0)
            return worldPos;
        if (!force && !UseLocalOnZoneWire)
            return worldPos;
        return ZoneManager.Instance.ConvertToLocalCoordinates(zoneId, worldPos);
    }

    public static Vector3 ToWorld(uint zoneId, Vector3 zoneLocalPos, bool force = false)
    {
        if (zoneId == 0)
            return zoneLocalPos;
        if (!force && !UseLocalOnZoneWire)
            return zoneLocalPos;
        return ZoneManager.Instance.ConvertToWorldCoordinates(zoneId, zoneLocalPos);
    }

    /// <summary>Ship helm requests have no position field — leave them alone.</summary>
    public static bool CarriesSpatialPosition(MoveType move) =>
        move is not null and not ShipRequestMoveType;

    public static void ShiftWorldToLocal(uint zoneId, MoveType move)
    {
        if (!CarriesSpatialPosition(move) || !UseLocalOnZoneWire || zoneId == 0)
            return;

        var p = ToZoneLocal(zoneId, new Vector3(move.X, move.Y, move.Z));
        move.X = p.X;
        move.Y = p.Y;
        move.Z = p.Z;

        if (move is UnitMoveType unit && (unit.ActorFlags & 0x20) != 0)
        {
            var p2 = ToZoneLocal(zoneId, new Vector3(unit.X2, unit.Y2, unit.Z2));
            unit.X2 = p2.X;
            unit.Y2 = p2.Y;
            unit.Z2 = p2.Z;
        }
    }

    public static void ShiftLocalToWorld(uint zoneId, MoveType move, bool force = false)
    {
        if (!CarriesSpatialPosition(move) || zoneId == 0)
            return;
        if (!force && !UseLocalOnZoneWire)
            return;

        var p = ToWorld(zoneId, new Vector3(move.X, move.Y, move.Z), force);
        move.X = p.X;
        move.Y = p.Y;
        move.Z = p.Z;

        if (move is UnitMoveType unit && (unit.ActorFlags & 0x20) != 0)
        {
            var p2 = ToWorld(zoneId, new Vector3(unit.X2, unit.Y2, unit.Z2), force);
            unit.X2 = p2.X;
            unit.Y2 = p2.Y;
            unit.Z2 = p2.Z;
        }
    }
}
