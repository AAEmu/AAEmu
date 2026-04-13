#nullable enable

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Physics.Util;

using System.Numerics;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Resolves ship hull vs steep <b>emerged</b> rock (terrain at/above local waterline only). Separation uses the cliff face normal
/// (horizontal gradient). Velocity changes are partial and small-step — avoids ping-pong elastic bounces from full depenetration + zeroed normal speed.
/// </summary>
public sealed class ShipCliffInteraction
{
    public static class CliffObstacleDefaults
    {
        /// <summary>Half-spacing for central-difference gradient (m). Larger = less seam/bathymetry noise, softer cliff onset.</summary>
        public const float GradientSampleMeters = 3.25f;
        /// <summary>Minimum |∇h| (m rise per m horizontal). Higher than shore probe threshold — avoids deep underwater slopes.</summary>
        public const float MinGradientMagnitude = 1.05f;
        /// <summary>Terrain must be at least this much above local water (m) at a probe — <c>hf &gt;= wf + this</c> (underwater rock ignored).</summary>
        public const float MinRockHeightAboveWaterMeters = 0.08f;
        /// <summary>Max distance (m) uphill to probe for above-water rock; beyond this, gradient alone is ignored.</summary>
        public const float EmergedRockProbeMaxDistanceMeters = 14f;
        /// <summary>Ignore |∇h| above this (single-tick spikes / bad cells); treated as non-cliff.</summary>
        public const float MaxGradientMagnitudeSanity = 6f;
        /// <summary>Half-thickness (m) of the virtual wall on the cliff face normal (long axis is tangent ⊥ normal — no extra radius on that axis).</summary>
        public const float WallHalfThicknessMeters = 0.45f;
        /// <summary>Along ascent past first emerged-rock probe: wall anchor sits this far past that point (m).</summary>
        public const float WallLeadBeyondEmergedProbeMeters = 1.15f;
        /// <summary>Minimum distance (m) along ascent for wall anchor (avoids wall through the hull when rock is very close).</summary>
        public const float WallAlongAscentMinMeters = 3.5f;
        /// <summary>Vertical half-height (m) of the obstacle column for overlap tests (Jitter Y).</summary>
        public const float VerticalHalfHeightMeters = 22f;
        /// <summary>Max depenetration iterations per ship per pass — keep low to reduce elastic ping-pong.</summary>
        public const int MaxPairIterations = 3;
        /// <summary>Ignore overlaps with penetration below this (m) to reduce jitter from marginal contacts.</summary>
        public const float MinPenetrationToAct = 0.11f;
        /// <summary>Minimum vertical (Jitter Y) AABB overlap (m) between hull and virtual wall column before face-normal resolve.</summary>
        public const float MinVerticalOverlap = 0.12f;
        /// <summary>Max horizontal depenetration step (m) per iteration — cap keeps contact from “popping”.</summary>
        public const float MaxSeparationStepMetersPerIteration = 0.095f;
        /// <summary>Multiplier on penetration before <see cref="OverlapResolveFractionPerIteration"/> (≤1 slightly under-corrects = softer).</summary>
        public const float SeparationPushMultiplier = 0.99f;
        /// <summary>Fraction of the computed push applied per iteration; &lt;1 spreads exit across ticks (less spring).</summary>
        public const float OverlapResolveFractionPerIteration = 0.48f;
        /// <summary>Extra separation (m); keep near zero to avoid overshoot oscillation.</summary>
        public const float SeparationSlackMeters = 0.002f;
        /// <summary>Fraction of <b>into-rock</b> horizontal speed removed per iteration — low = heavy hull, little bounce.</summary>
        public const float ClosingSpeedDamp = 0.14f;
        /// <summary>Fraction of tangential slip removed per iteration; very high = almost free slide along rock.</summary>
        public const float TangentialSlipDamp = 0.97f;
        /// <summary>Skip cliff resolve when the hull is clearly above the local water surface (m).</summary>
        public const float AboveWaterlineSkipMeters = 0.18f;
    }

    /// <summary>
    /// Run after <see cref="ShipDoodadInteraction.ResolveAll"/> so static props keep priority; re-syncs bow speed like doodads.
    /// </summary>
    public void ResolveAll(WorldInstance world, IReadOnlyList<Slave> ships, TimeSpan deltaTime)
    {
        if (ships.Count == 0)
            return;

        foreach (var ship in ships)
        {
            if (ship.RigidBody is null || ship.ShipController?.ShipModel is null || ship.Region is null)
                continue;
            if (ship.ParentWorld?.Id != world.Id)
                continue;

            TryResolveShipVsSteepTerrain(world, ship);
        }

        foreach (var ship in ships)
            ShipShipInteraction.SyncSlaveSpeedFromBowVelocity(ship);
    }

    /// <summary>
    /// Walks uphill along ∇h and finds the first point where terrain is <b>above local waterline</b>
    /// (<c>hf &gt;= wf + <see cref="CliffObstacleDefaults.MinRockHeightAboveWaterMeters"/></c>); underwater relief is ignored.
    /// </summary>
    private static bool TryGetFirstAboveWaterRockUphillDistance(
        WorldInstance world,
        float cx,
        float cz,
        float ax,
        float az,
        float bodyY,
        out float distanceAlongAscent)
    {
        ReadOnlySpan<float> steps = [2f, 3.5f, 5.5f, 7.5f, 10f, 12.5f, 14f];
        var minAbove = CliffObstacleDefaults.MinRockHeightAboveWaterMeters;
        var maxS = CliffObstacleDefaults.EmergedRockProbeMaxDistanceMeters;
        foreach (var s in steps)
        {
            if (s > maxS)
                break;
            var sx = cx + ax * s;
            var sz = cz + az * s;
            var hf = world.GetHeight(sx, sz);
            var wf = world.Water.GetWaterSurface(new Vector3(sx, sz, bodyY), out _);
            if (hf >= wf + minAbove)
            {
                distanceAlongAscent = s;
                return true;
            }
        }

        distanceAlongAscent = 0f;
        return false;
    }

    private static void TryResolveShipVsSteepTerrain(WorldInstance world, Slave ship)
    {
        var body = ship.RigidBody!;
        var ma = ship.ShipController!.ShipModel;

        var waterAtCenter = world.Water.GetWaterSurface(new Vector3(body.Position.X, body.Position.Z, body.Position.Y), out _);
        if (body.Position.Y > waterAtCenter + CliffObstacleDefaults.AboveWaterlineSkipMeters)
            return;

        ShipShipInteraction.GetMassBoxCenterXz(body, ma, ship.Scale, out var cx, out var cz);

        var d = CliffObstacleDefaults.GradientSampleMeters;
        var hxp = world.GetHeight(cx + d, cz);
        var hxm = world.GetHeight(cx - d, cz);
        var hzp = world.GetHeight(cx, cz + d);
        var hzm = world.GetHeight(cx, cz - d);
        var gx = (hxp - hxm) / (2f * d);
        var gz = (hzp - hzm) / (2f * d);
        var gLenSq = gx * gx + gz * gz;
        if (gLenSq < CliffObstacleDefaults.MinGradientMagnitude * CliffObstacleDefaults.MinGradientMagnitude)
            return;

        var gLen = MathF.Sqrt(gLenSq);
        if (gLen > CliffObstacleDefaults.MaxGradientMagnitudeSanity)
            return;

        var invLen = 1f / gLen;
        var ax = gx * invLen;
        var az = gz * invLen;

        if (!TryGetFirstAboveWaterRockUphillDistance(world, cx, cz, ax, az, body.Position.Y, out var emergedS))
            return;

        var wallAlong = Math.Clamp(
            emergedS + CliffObstacleDefaults.WallLeadBeyondEmergedProbeMeters,
            CliffObstacleDefaults.WallAlongAscentMinMeters,
            CliffObstacleDefaults.EmergedRockProbeMaxDistanceMeters);
        var bx = cx + ax * wallAlong;
        var bz = cz + az * wallAlong;
        var floorAtWall = world.GetHeight(bx, bz);
        var waterAtWallPoint = world.Water.GetWaterSurface(new Vector3(bx, bz, body.Position.Y), out _);
        if (floorAtWall < waterAtWallPoint + CliffObstacleDefaults.MinRockHeightAboveWaterMeters)
            return;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(body.Orientation));
        var bowA = rpyA.Item1 + MathUtil.HalfPi;

        var halfLenA = ma.MassBoxSizeY * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateLength;
        var satHalfWidA = ma.MassBoxSizeX * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateBeam *
                          ShipShipInteraction.ShipHullPairDefaults.BeamDetectTightenMul;

        var halfH = CliffObstacleDefaults.VerticalHalfHeightMeters;
        var dMinY = floorAtWall - halfH;
        var dMaxY = floorAtWall + halfH;
        var rB = CliffObstacleDefaults.WallHalfThicknessMeters;

        for (var iter = 0; iter < CliffObstacleDefaults.MaxPairIterations; iter++)
        {
            var bbA = body.Shapes[0].WorldBoundingBox;
            var overlapY = MathF.Min(bbA.Max.Y, dMaxY) - MathF.Max(bbA.Min.Y, dMinY);
            if (overlapY < CliffObstacleDefaults.MinVerticalOverlap)
                break;

            ShipShipInteraction.GetMassBoxCenterXz(body, ma, ship.Scale, out var acx, out var acz);

            // Face normal = uphill (ship → rock). Wall tangent ⊥ n ⇒ projected half-extent on n is only slab thickness.
            var nx = ax;
            var nz = az;
            var cA = acx * nx + acz * nz;
            var cB = bx * nx + bz * nz;
            var rA = ShipShipInteraction.ProjectObbRadiusXz(halfLenA, satHalfWidA, bowA, nx, nz);
            var penetration = MathF.Min(cA + rA, cB + rB) - MathF.Max(cA - rA, cB - rB);
            if (penetration >= ShipStaticObstacleContact.MinPenetrationMetersForEnvHullDamage)
                ship.StaticObstacleHullDamageContactActive = true;

            if (penetration < CliffObstacleDefaults.MinPenetrationToAct)
                break;

            var move = (penetration * CliffObstacleDefaults.SeparationPushMultiplier + CliffObstacleDefaults.SeparationSlackMeters)
                * CliffObstacleDefaults.OverlapResolveFractionPerIteration;
            move = MathF.Min(move, CliffObstacleDefaults.MaxSeparationStepMetersPerIteration);
            ShipStaticObstacleContact.ApplySeparationAndSurfaceDampXz(
                body,
                ship,
                nx,
                nz,
                move,
                CliffObstacleDefaults.ClosingSpeedDamp,
                CliffObstacleDefaults.TangentialSlipDamp,
                ShipStaticObstacleContact.DefaultReplicationContactHoldTicks);
        }
    }
}
