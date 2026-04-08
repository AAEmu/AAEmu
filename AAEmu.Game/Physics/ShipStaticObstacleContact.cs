#nullable enable

using AAEmu.Game.Models.Game.Units;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Shared XZ separation + normal/tangential velocity damping for ship vs static obstacles (doodads, cliff proxy wall).
/// </summary>
internal static class ShipStaticObstacleContact
{
    /// <summary>Replication <see cref="ReplicationSmoothing.ContactHoldTicks"/> bump for static contacts.</summary>
    public const byte DefaultReplicationContactHoldTicks = 8;

    /// <summary>
    /// Hull vs static obstacle SAT penetration (m) that counts as contact for periodic env hull damage.
    /// Lower than depenetration thresholds so damage still ticks after a push clears overlap on the same frame boundary.
    /// </summary>
    public const float MinPenetrationMetersForEnvHullDamage = 0.015f;

    public static void ApplySeparationAndSurfaceDampXz(
        RigidBody body,
        Slave ship,
        float nx,
        float nz,
        float moveMeters,
        float closingSpeedDamp,
        float tangentialSlipDamp,
        byte replicationContactHoldTicks)
    {
        body.Position -= new JVector(nx * moveMeters, 0f, nz * moveMeters);

        var va = body.Velocity;
        var closing = va.X * nx + va.Z * nz;
        if (closing > 0f)
        {
            va = new JVector(va.X - nx * closing * closingSpeedDamp, va.Y,
                va.Z - nz * closing * closingSpeedDamp);
        }

        var tx = -nz;
        var tz = nx;
        var relT = va.X * tx + va.Z * tz;
        var slip = relT * (1f - tangentialSlipDamp);
        va = new JVector(va.X - tx * slip, va.Y, va.Z - tz * slip);
        body.Velocity = va;

        if (replicationContactHoldTicks > 0 && ship.ShipController != null)
            ship.ShipController.Replication.ContactHoldTicks =
                Math.Max(ship.ShipController.Replication.ContactHoldTicks, replicationContactHoldTicks);
    }
}
