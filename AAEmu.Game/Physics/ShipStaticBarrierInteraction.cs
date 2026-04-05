#nullable enable

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
    public static class BarrierDefaults
    {
        public const float VerticalPadMeters = 0.35f;
        public const int ResolvePasses = 2;
        public const int MaxPairIterations = 10;
        public const float MinPenetrationToAct = 0.05f;
        public const float MinVerticalOverlap = 0.12f;
        public const float SeparationPushMultiplier = 1.18f;
        public const float SeparationSlackMeters = 0.025f;
        public const float ClosingSpeedDamp = 1f;
        public const float TangentialSlipDamp = 0.82f;
        /// <summary>Extra AABB padding around ship for culling vs barrier bounds (m).</summary>
        public const float ShipBoundsPadMeters = 72f;
    }

    public void ResolveAll(WorldInstance world, IReadOnlyList<Slave> ships, TimeSpan deltaTime)
    {
        if (ships.Count == 0)
            return;

        List<ShipStaticBarrier> barrierSnapshot;
        lock (world.ShipStaticBarriersMutationLock)
        {
            var barriers = world.ShipStaticBarriers;
            if (barriers is null || barriers.Barriers.Count == 0)
                return;
            barrierSnapshot = [..barriers.Barriers];
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

            foreach (var barrier in barrierSnapshot)
            {
                if (!barrier.Enabled)
                    continue;
                if (barrier.ZoneKey != 0u && ship.Transform.ZoneId != barrier.ZoneKey)
                    continue;

                ShipShipInteraction.GetMassBoxCenterXz(ship.RigidBody, model, ship.Scale, out var scx, out var scz);
                if (scx < barrier.AabbMinX - shipPad || scx > barrier.AabbMaxX + shipPad ||
                    scz < barrier.AabbMinY - shipPad || scz > barrier.AabbMaxY + shipPad)
                    continue;

                for (var pass = 0; pass < BarrierDefaults.ResolvePasses; pass++)
                {
                    foreach (var seg in barrier.Segments)
                        TryResolveShipVsSegment(ship, barrier, seg);
                }
            }
        }

        foreach (var ship in ships)
            ShipShipInteraction.SyncSlaveSpeedFromBowVelocity(ship);
    }

    private static bool TryResolveShipVsSegment(Slave ship, ShipStaticBarrier barrier, (float x0, float y0, float x1, float y1) seg)
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

        var halfLenA = ma.MassBoxSizeY * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateLength;
        var satHalfWidA = ma.MassBoxSizeX * ship.Scale * 0.5f * ShipShipInteraction.ShipHullPairDefaults.HullDetectInflateBeam *
                          ShipShipInteraction.ShipHullPairDefaults.BeamDetectTightenMul;

        var had = false;
        for (var iter = 0; iter < BarrierDefaults.MaxPairIterations; iter++)
        {
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
