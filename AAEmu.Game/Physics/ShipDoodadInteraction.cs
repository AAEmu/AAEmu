#nullable enable

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Util;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Resolves ship hull vs static-structure doodads (piers, harbor props, etc.). Client meshes are not in the heightmap,
/// so <see cref="HeightMaps.HeightmapDetection"/> never sees them; retail uses <c>collide_ship</c> / <c>no_collision</c> on doodad templates.
/// Uses the same XZ OBB SAT convention as <see cref="ShipShipInteraction"/>; obstacle is treated as infinite mass (only the ship moves).
/// </summary>
public sealed class ShipDoodadInteraction
{
    public static class DoodadObstacleDefaults
    {
        /// <summary>Default horizontal half-size (m) when <see cref="DoodadObj.Templates.DoodadTemplate.SimRadius"/> is 0 or unusable.</summary>
        public const float DefaultFootprintHalfExtentMeters = 3.5f;
        /// <summary>Scale applied to <c>sim_radius</c> from DB when interpreted as centimeters.</summary>
        public const float SimRadiusCmToMeters = 0.01f;
        /// <summary>Vertical half-height (m) of the collision column around the doodad pivot.</summary>
        public const float VerticalHalfHeightMeters = 14f;
        /// <summary>Extra padding when querying regions for nearby doodads.</summary>
        public const float QueryRadiusPaddingMeters = 28f;
        /// <summary>Passes over all ship–doodad pairs (helps when multiple props overlap).</summary>
        public const int ResolvePasses = 2;
        /// <summary>Max SAT/depentration iterations per ship–doodad pair per pass (caps pathological overlap loops).</summary>
        public const int MaxPairIterations = 10;
        /// <summary>Ignore overlaps with penetration below this (m) to reduce jitter from marginal contacts.</summary>
        public const float MinPenetrationToAct = 0.05f;
        /// <summary>Minimum vertical (Jitter Y) AABB overlap (m) between hull and obstacle column before XZ SAT runs.</summary>
        public const float MinVerticalOverlap = 0.12f;
        /// <summary>Multiplier on computed penetration when pushing the ship out along the contact normal.</summary>
        public const float SeparationPushMultiplier = 1.18f;
        /// <summary>Extra separation (m) added after depenetration to reduce immediate re-penetration next tick.</summary>
        public const float SeparationSlackMeters = 0.025f;
        /// <summary>Fraction of velocity into the obstacle (along SAT normal) removed per iteration (1 = fully cancel closing speed).</summary>
        public const float ClosingSpeedDamp = 1f;
        /// <summary>Fraction of tangential slip removed (1 − value retained); lower = more slide along the wall.</summary>
        public const float TangentialSlipDamp = 0.82f;
    }

    /// <summary>
    /// Run after <see cref="ShipShipInteraction.ResolveAllPairs"/> so ship–ship separation is settled first.
    /// </summary>
    public void ResolveAll(WorldInstance world, IReadOnlyList<Slave> ships, TimeSpan deltaTime)
    {
        if (ships.Count == 0)
            return;

        for (var pass = 0; pass < DoodadObstacleDefaults.ResolvePasses; pass++)
        {
            foreach (var ship in ships)
            {
                if (ship.RigidBody is null || ship.ShipController?.ShipModel is null || ship.Region is null)
                    continue;

                var model = ship.ShipController.ShipModel;
                var halfLen = model.MassBoxSizeY * ship.Scale * 0.5f;
                var halfBeam = model.MassBoxSizeX * ship.Scale * 0.5f;
                var queryR = halfLen + halfBeam + DoodadObstacleDefaults.QueryRadiusPaddingMeters +
                             DoodadObstacleDefaults.DefaultFootprintHalfExtentMeters * 2f;

                var doodads = WorldManager.GetAround<Doodad>(ship, queryR);
                foreach (var doodad in doodads)
                {
                    if (doodad.ParentWorld?.Id != world.Id)
                        continue;
                    var tmpl = doodad.Template;
                    if (tmpl is null || !tmpl.CollideShip || tmpl.NoCollision)
                        continue;

                    TryResolveShipVsDoodad(ship, doodad);
                }
            }
        }

        foreach (var ship in ships)
            ShipShipInteraction.SyncSlaveSpeedFromBowVelocity(ship);
    }

    private static float GetObstacleFootprintHalfExtent(Doodad doodad)
    {
        var sr = doodad.Template.SimRadius;
        if (sr <= 0)
            return DoodadObstacleDefaults.DefaultFootprintHalfExtentMeters * doodad.Scale;
        // Client data typically stores sim_radius in centimeters for medium/large props.
        var meters = sr * DoodadObstacleDefaults.SimRadiusCmToMeters;
        return MathF.Max(meters, 1.2f) * doodad.Scale;
    }

    private static bool TryResolveShipVsDoodad(Slave ship, Doodad doodad)
    {
        var body = ship.RigidBody!;
        var ma = ship.ShipController!.ShipModel;

        var bbA = body.Shapes[0].WorldBoundingBox;
        var halfH = DoodadObstacleDefaults.VerticalHalfHeightMeters * doodad.Scale;
        var w = doodad.Transform.World;
        var dPy = w.Position.Z;
        var dMinY = dPy - halfH;
        var dMaxY = dPy + halfH;
        var overlapY = MathF.Min(bbA.Max.Y, dMaxY) - MathF.Max(bbA.Min.Y, dMinY);
        if (overlapY < DoodadObstacleDefaults.MinVerticalOverlap)
            return false;

        var r = GetObstacleFootprintHalfExtent(doodad);
        var bowB = w.Rotation.Z + 1.57f;
        var bx = w.Position.X;
        var bz = w.Position.Y;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(body.Orientation));
        var bowA = rpyA.Item1 + 1.57f;

        var halfLenA = ma.MassBoxSizeY * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateLength;
        var satHalfWidA = ma.MassBoxSizeX * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateBeam *
                          ShipShipInteraction.ShipHullPairDefaults.BeamDetectTightenMul;

        var had = false;
        for (var iter = 0; iter < DoodadObstacleDefaults.MaxPairIterations; iter++)
        {
            bbA = body.Shapes[0].WorldBoundingBox;
            overlapY = MathF.Min(bbA.Max.Y, dMaxY) - MathF.Max(bbA.Min.Y, dMinY);
            if (overlapY < DoodadObstacleDefaults.MinVerticalOverlap)
                break;

            ShipShipInteraction.GetMassBoxCenterXz(body, ma, ship.Scale, out var ax, out var az);

            if (!TryObbXzMinPenetration(
                    ax, az, bowA, halfLenA, satHalfWidA,
                    bx, bz, bowB, r, r,
                    out var penetration,
                    out var nx,
                    out var nz))
                break;

            if (penetration < DoodadObstacleDefaults.MinPenetrationToAct)
                break;

            var dx = bx - ax;
            var dz = bz - az;
            if (nx * dx + nz * dz < 0f)
            {
                nx = -nx;
                nz = -nz;
            }

            var move = penetration * DoodadObstacleDefaults.SeparationPushMultiplier + DoodadObstacleDefaults.SeparationSlackMeters;
            body.Position -= new JVector(nx * move, 0f, nz * move);

            var va = body.Velocity;
            var closing = va.X * nx + va.Z * nz;
            if (closing > 0f)
            {
                va = new JVector(va.X - nx * closing * DoodadObstacleDefaults.ClosingSpeedDamp, va.Y,
                    va.Z - nz * closing * DoodadObstacleDefaults.ClosingSpeedDamp);
            }

            var tx = -nz;
            var tz = nx;
            var relT = va.X * tx + va.Z * tz;
            var slip = relT * (1f - DoodadObstacleDefaults.TangentialSlipDamp);
            va = new JVector(va.X - tx * slip, va.Y, va.Z - tz * slip);
            body.Velocity = va;

            const byte holdTicks = 8;
            if (ship.ShipController != null)
                ship.ShipController.Replication.ContactHoldTicks =
                    Math.Max(ship.ShipController.Replication.ContactHoldTicks, holdTicks);

            had = true;
        }

        return had;
    }

    /// <summary>2D SAT on XZ (same as ship–ship).</summary>
    private static bool TryObbXzMinPenetration(
        float ax, float az, float bowA, float halfLenA, float halfWidA,
        float bx, float bz, float bowB, float halfLenB, float halfWidB,
        out float minPenetration,
        out float bestNx,
        out float bestNz)
    {
        minPenetration = 0f;
        bestNx = 1f;
        bestNz = 0f;

        ReadOnlySpan<(float x, float z)> axes =
        [
            (MathF.Cos(bowA), MathF.Sin(bowA)),
            (-MathF.Sin(bowA), MathF.Cos(bowA)),
            (MathF.Cos(bowB), MathF.Sin(bowB)),
            (-MathF.Sin(bowB), MathF.Cos(bowB)),
        ];

        var found = false;
        var minO = float.MaxValue;

        foreach (var (ux, uz) in axes)
        {
            var len = MathF.Sqrt(ux * ux + uz * uz);
            if (len < 1e-6f)
                continue;
            var nx = ux / len;
            var nz = uz / len;

            var cA = ax * nx + az * nz;
            var cB = bx * nx + bz * nz;
            var rA = ProjectObbRadiusXz(halfLenA, halfWidA, bowA, nx, nz);
            var rB = ProjectObbRadiusXz(halfLenB, halfWidB, bowB, nx, nz);

            var overlap = MathF.Min(cA + rA, cB + rB) - MathF.Max(cA - rA, cB - rB);
            if (overlap <= 0f)
                return false;

            if (overlap < minO)
            {
                minO = overlap;
                bestNx = nx;
                bestNz = nz;
                found = true;
            }
        }

        if (!found)
            return false;

        minPenetration = minO;
        return true;
    }

    private static float ProjectObbRadiusXz(float halfLen, float halfWid, float bow, float nx, float nz)
    {
        var d1 = MathF.Cos(bow) * nx + MathF.Sin(bow) * nz;
        var d2 = -MathF.Sin(bow) * nx + MathF.Cos(bow) * nz;
        return halfLen * MathF.Abs(d1) + halfWid * MathF.Abs(d2);
    }
}
