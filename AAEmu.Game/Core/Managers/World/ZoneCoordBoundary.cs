using System.Numerics;

using AAEmu.Game.Models.Game.Units.Movements;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Optional World-side unit-space remapping at the zone TCP boundary.
/// Spawner geometry packets (Activate circle, ZWSpawnNpc parse) always use zone-local convert;
/// this flag only affects WZ unit Create/move bodies — leave it off unless reproducing a local-space
/// unit wire layout.
/// </summary>
public static class ZoneCoordBoundary
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// When true, World rewrites WZ unit placement + moves into zone-local and ZW moves into world.
    /// Default <c>false</c>: World sends/receives world coords on WZNpcState/WZUnitState/movements.
    /// Force-local can double-subtract zone origin and place units off-mesh. Opt-in:
    /// <c>AAEMU_WZ_UNIT_POS_LOCAL=1</c>. Kill with <c>WORLD=1</c> / <c>LOCAL=0</c>. Never flip mid-session.
    /// </summary>
    public static bool UseLocalOnZoneWire { get; } = ResolveUseLocalOnZoneWire();

    static ZoneCoordBoundary()
    {
        Logger.Info(
            "ZoneCoordBoundary: UseLocalOnZoneWire={0} (default world-on-wire; LOCAL=1 only for regression repro)",
            UseLocalOnZoneWire);
    }

    private static bool ResolveUseLocalOnZoneWire()
    {
        if (IsEnv("AAEMU_WZ_UNIT_POS_WORLD", "1"))
            return false;
        if (IsEnv("AAEMU_WZ_UNIT_POS_LOCAL", "0"))
            return false;
        // Opt-in only — default is world coordinates on unit wire.
        return IsEnv("AAEMU_WZ_UNIT_POS_LOCAL", "1");
    }

    private static bool IsEnv(string name, string value) =>
        string.Equals(Environment.GetEnvironmentVariable(name), value, StringComparison.Ordinal);

    public static Vector3 ToZoneLocal(uint zoneId, Vector3 worldPos)
    {
        if (!UseLocalOnZoneWire || zoneId == 0)
            return worldPos;
        return ZoneManager.Instance.ConvertToLocalCoordinates(zoneId, worldPos);
    }

    public static Vector3 ToWorld(uint zoneId, Vector3 zoneLocalPos)
    {
        if (!UseLocalOnZoneWire || zoneId == 0)
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

    public static void ShiftLocalToWorld(uint zoneId, MoveType move)
    {
        if (!CarriesSpatialPosition(move) || !UseLocalOnZoneWire || zoneId == 0)
            return;

        var p = ToWorld(zoneId, new Vector3(move.X, move.Y, move.Z));
        move.X = p.X;
        move.Y = p.Y;
        move.Z = p.Z;

        if (move is UnitMoveType unit && (unit.ActorFlags & 0x20) != 0)
        {
            var p2 = ToWorld(zoneId, new Vector3(unit.X2, unit.Y2, unit.Z2));
            unit.X2 = p2.X;
            unit.Y2 = p2.Y;
            unit.Z2 = p2.Z;
        }
    }
}
