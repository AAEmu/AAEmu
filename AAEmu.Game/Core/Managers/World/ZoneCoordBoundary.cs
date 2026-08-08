using System.Numerics;

using AAEmu.Game.Models.Game.Units.Movements;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Converts unit positions at the World/Zone boundary when zone-local placement is enabled.
/// The conversion is opt-in because world-space placement remains the default.
/// </summary>
public static class ZoneCoordBoundary
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// When true: WZ placement + WZUnitMovement are zone-local; ZWUnitMovements convert local→world
    /// for SC. Default <c>false</c> (world-on-wire). Set <c>AAEMU_WZ_UNIT_POS_LOCAL=1</c> to experiment.
    /// </summary>
    public static bool UseLocalOnZoneWire { get; } = ResolveUseLocalOnZoneWire();

    static ZoneCoordBoundary()
    {
        Logger.Info(
            "ZoneCoordBoundary: UseLocalOnZoneWire={0} (opt-in LOCAL=1; kill WORLD=1 / LOCAL=0)",
            UseLocalOnZoneWire);
    }

    private static bool ResolveUseLocalOnZoneWire()
    {
        if (IsEnv("AAEMU_WZ_UNIT_POS_WORLD", "1"))
            return false;
        if (IsEnv("AAEMU_WZ_UNIT_POS_LOCAL", "0"))
            return false;
        // Keep the conversion opt-in; world-space placement is the default.
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
