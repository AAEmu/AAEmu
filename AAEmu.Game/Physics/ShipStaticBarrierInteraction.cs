#nullable enable

using System.Collections.Generic;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics.Util;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Ship hull vs polyline barriers in <see cref="ShipStaticBarrierZones"/>.
/// Each segment is a thin static OBB in XZ; same SAT + separation pattern as <see cref="ShipDoodadInteraction"/>.
/// </summary>
public sealed class ShipStaticBarrierInteraction
{
    private readonly List<(ShipStaticBarrier barrier, int segmentIndex)> _candidateBuffer = [];
    private readonly HashSet<(ShipStaticBarrier barrier, int segmentIndex)> _candidateDedupe = [];

    /// <summary>Ship hull vs barrier segment resolution (XZ OBB SAT + separation).</summary>
    public static class BarrierDefaults
    {
        /// <summary>Extra margin (m) on barrier <c>ZMin/ZMax</c> when testing hull AABB vs the obstacle vertical slab.</summary>
        public const float VerticalPadMeters = 0.35f;

        /// <summary>Passes over barriers per tick; &gt;1 worsens ping-pong in corners (multiple walls).</summary>
        public const int ResolvePasses = 1;

        /// <summary>Max depenetration iterations per ship–segment pair per pass.</summary>
        public const int MaxPairIterations = 6;

        /// <summary>Minimum OBB penetration (m) before applying motion (reduces jitter on hairline overlaps).</summary>
        public const float MinPenetrationToAct = 0.05f;

        /// <summary>Minimum Jitter-Y overlap (m) between hull and barrier height range; XZ SAT is skipped below this.</summary>
        public const float MinVerticalOverlap = 0.12f;

        /// <summary>Multiplier on penetration when pushing along the contact normal; keep near 1 to avoid corner eject.</summary>
        public const float SeparationPushMultiplier = 1.04f;

        /// <summary>Extra separation (m) after a push; smaller reduces bounce between opposing segments.</summary>
        public const float SeparationSlackMeters = 0.01f;

        /// <summary>Cap on one ApplySeparation step (m); same idea as <see cref="ShipCliffInteraction.CliffObstacleDefaults.MaxSeparationStepMetersPerIteration"/>.</summary>
        public const float MaxSeparationStepMetersPerIteration = 0.055f;

        /// <summary>Max total XZ separation applied to one hull from all barrier segments this tick (m).</summary>
        public const float MaxTotalSeparationBudgetPerTick = 0.22f;

        /// <summary>Fraction of velocity into the obstacle along the normal removed per iteration; lower = softer in tight slips.</summary>
        public const float ClosingSpeedDamp = 0.38f;

        /// <summary>Fraction of tangential slip removed per iteration; higher = more slide along walls (easier to creep out of corners).</summary>
        public const float TangentialSlipDamp = 0.91f;

        /// <summary>Padding (m) around ship mass-box center for broadphase culling before SAT.</summary>
        public const float ShipBoundsPadMeters = 72f;

        /// <summary>
        /// Multiplier on the ship SAT half-length and half-beam vs barriers only (after <see cref="ShipShipInteraction.ShipHullPairDefaults"/> inflate).
        /// &lt;1 pulls first contact closer to the mesh; ship–ship / doodad unchanged.
        /// </summary>
        public const float SatShipObbTightenMul = 1.05f;
    }

    public void ResolveAll(WorldInstance world, IReadOnlyList<Slave> ships, TimeSpan deltaTime)
    {
        if (ships.Count == 0)
            return;

        ShipStaticBarrierSpatialGrid? grid;
        lock (world.ShipStaticBarriersMutationLock)
        {
            var barriers = world.ShipStaticBarriers;
            if (barriers is null || barriers.Barriers.Count == 0)
                return;
            if (barriers.SpatialGridBuiltForBarrierCount != barriers.Barriers.Count)
            {
                barriers.SpatialGrid ??= new ShipStaticBarrierSpatialGrid();
                barriers.SpatialGrid.Rebuild(barriers.Barriers);
                barriers.SpatialGridBuiltForBarrierCount = barriers.Barriers.Count;
            }

            grid = barriers.SpatialGrid;
        }

        foreach (var ship in ships)
        {
            if (ship.RigidBody is null || ship.ShipController?.ShipModel is null || ship.Region is null)
                continue;
            if (ship.ParentWorld?.Id != world.Id)
                continue;

            var model = ship.ShipController.ShipModel;
            var halfLen = model.MassBoxSizeY * ship.Scale * 0.5f;
            var halfBeam = model.MassBoxSizeX * ship.Scale * 0.5f;
            var shipPad = halfLen + halfBeam + BarrierDefaults.ShipBoundsPadMeters;

            ShipShipInteraction.GetMassBoxCenterXz(ship.RigidBody, model, ship.Scale, out var scx, out var scz);
            grid!.Query(scx, scz, shipPad, _candidateBuffer, _candidateDedupe);

            var sepBudget = BarrierDefaults.MaxTotalSeparationBudgetPerTick;
            for (var pass = 0; pass < BarrierDefaults.ResolvePasses && sepBudget > 0f; pass++)
            {
                for (var ci = 0; ci < _candidateBuffer.Count; ci++)
                {
                    if (sepBudget <= 0f)
                        break;
                    var (barrier, segIdx) = _candidateBuffer[ci];
                    if (!barrier.Enabled)
                        continue;
                    if (barrier.ZoneKey != 0u && ship.Transform.ZoneId != barrier.ZoneKey)
                        continue;

                    var seg = barrier.Segments[segIdx];
                    TryResolveShipVsSegment(ship, barrier, seg, ref sepBudget);
                }
            }
        }

        foreach (var ship in ships)
            ShipShipInteraction.SyncSlaveSpeedFromBowVelocity(ship);
    }

    private static bool TryResolveShipVsSegment(Slave ship, ShipStaticBarrier barrier, (float x0, float y0, float x1, float y1) seg, ref float separationBudgetMeters)
    {
        var body = ship.RigidBody!;
        var ma = ship.ShipController!.ShipModel;

        var dx = seg.x1 - seg.x0;
        var dz = seg.y1 - seg.y0;
        var segLen = MathF.Sqrt(dx * dx + dz * dz);
        if (segLen < 1e-3f)
            return false;

        var bx = (seg.x0 + seg.x1) * 0.5f;
        var bz = (seg.y0 + seg.y1) * 0.5f;
        var bowB = MathF.Atan2(dz, dx);
        var halfLenB = segLen * 0.5f;
        var halfWidB = barrier.HalfThicknessMeters;

        var bbA = body.Shapes[0].WorldBoundingBox;
        var dMinY = barrier.ZMin - BarrierDefaults.VerticalPadMeters;
        var dMaxY = barrier.ZMax + BarrierDefaults.VerticalPadMeters;
        var overlapY = MathF.Min(bbA.Max.Y, dMaxY) - MathF.Max(bbA.Min.Y, dMinY);
        if (overlapY < BarrierDefaults.MinVerticalOverlap)
            return false;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(body.Orientation));
        var bowA = rpyA.Item1 + 1.57f;

        var halfLenA = ma.MassBoxSizeY * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateLength *
                       BarrierDefaults.SatShipObbTightenMul;
        var satHalfWidA = ma.MassBoxSizeX * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateBeam *
                          ShipShipInteraction.ShipHullPairDefaults.BeamDetectTightenMul * BarrierDefaults.SatShipObbTightenMul;

        var had = false;
        for (var iter = 0; iter < BarrierDefaults.MaxPairIterations; iter++)
        {
            if (separationBudgetMeters <= 0f)
                break;

            bbA = body.Shapes[0].WorldBoundingBox;
            overlapY = MathF.Min(bbA.Max.Y, dMaxY) - MathF.Max(bbA.Min.Y, dMinY);
            if (overlapY < BarrierDefaults.MinVerticalOverlap)
                break;

            ShipShipInteraction.GetMassBoxCenterXz(body, ma, ship.Scale, out var ax, out var az);

            if (!ShipShipInteraction.TryObbXzMinPenetration(
                    ax, az, bowA, halfLenA, satHalfWidA,
                    bx, bz, bowB, halfLenB, halfWidB,
                    out var penetration,
                    out var nx,
                    out var nz))
                break;

            if (penetration >= ShipStaticObstacleContact.MinPenetrationMetersForEnvHullDamage)
                ship.StaticObstacleHullDamageContactActive = true;

            if (penetration < BarrierDefaults.MinPenetrationToAct)
                break;

            var rdx = bx - ax;
            var rdz = bz - az;
            if (nx * rdx + nz * rdz < 0f)
            {
                nx = -nx;
                nz = -nz;
            }

            var move = penetration * BarrierDefaults.SeparationPushMultiplier + BarrierDefaults.SeparationSlackMeters;
            move = MathF.Min(move, BarrierDefaults.MaxSeparationStepMetersPerIteration);
            move = MathF.Min(move, separationBudgetMeters);
            if (move < 1e-4f)
                break;

            separationBudgetMeters -= move;
            ShipStaticObstacleContact.ApplySeparationAndSurfaceDampXz(
                body,
                ship,
                nx,
                nz,
                move,
                BarrierDefaults.ClosingSpeedDamp,
                BarrierDefaults.TangentialSlipDamp,
                ShipStaticObstacleContact.DefaultReplicationContactHoldTicks);

            had = true;
        }

        return had;
    }
}
