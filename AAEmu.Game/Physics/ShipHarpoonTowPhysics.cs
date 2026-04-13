#nullable enable

using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Tow force when a ship harpoon is hooked to dry land: if the paid rope length is not slack
/// (<see cref="SlackMarginMeters"/>), accelerate the parent hull toward the hook in the horizontal plane.
/// Optional bow yaw toward average tow pull (<see cref="TowYawAssistRadPerSec"/>); ship–pair basis hull yaw is scaled by mass (see <see cref="ShipPairBowYawMassScaleReference"/>). Applied after helm/throttle velocity so <see cref="Slave.Speed"/> is resynced from the clamped along-bow component.
/// Tunables are <see cref="TowAccelPerMeterStretch"/> and related <c>const</c> fields below.
/// </summary>
public static class ShipHarpoonTowPhysics
{
    /// <summary>m/s² per meter of (cannon–hook distance − paid rope length) in the taut regime.</summary>
    public const float TowAccelPerMeterStretch = 1f;

    /// <summary>Cap on tow acceleration toward the hook (m/s²).</summary>
    public const float TowMaxAccel = 2f;

    /// <summary>If paid rope exceeds distance by more than this, treat as slack (no tow).</summary>
    public const float SlackMarginMeters = 0.5f;

    /// <summary>Ignore hooks this close to hull center (avoids spikes).</summary>
    public const float MinHookHorizontalDistance = 0.2f;

    /// <summary>
    /// Hack: added to <c>RopeLength</c> (client / initial stored value) only when judging slack/stretch in tow — avoids double-count at launch.
    /// Tune if fixed payout / <c>len</c> disagrees with server chord.
    /// </summary>
    public const float ServerRopePaidLengthAdditiveMeters = 12.5f;

    /// <summary>Yaw rate assist (rad/s per unit cross) so bow follows tow pull; use <c>-cross</c> vs body angular velocity sign.</summary>
    public const float TowYawAssistRadPerSec = 0.3f;

    /// <summary>Weights mass×(1 + k·|v|) dominance so a heavier/faster hull pulls the other more along a taut ship-to-ship harpoon.</summary>
    public const float ShipPairDominanceSpeedCoeff = 0.04f;

    /// <summary>Extra impulse scale when the towing hull wins dominance (≥1).</summary>
    public const float ShipPairDominantTowHullMul = 1.12f;

    /// <summary>Extra impulse scale when the hooked hull wins dominance (≥1).</summary>
    public const float ShipPairDominantBasisHullMul = 1.12f;

    /// <summary>Added to <see cref="SlackMarginMeters"/> only for ship–ship taut checks (client <c>RopeLength</c> often stays looser than chord).</summary>
    public const float ShipPairExtraSlackMarginMeters = 22f;

    /// <summary>Floor on stretch (m) when computing ship–pair tow so a tiny positive impulse still applies near taut.</summary>
    public const float ShipPairMinStretchMeters = 0.12f;

    /// <summary>When <see cref="ShipHarpoonRopeState.HookBasisObjId"/> is 0, pick nearest other boat hull within this distance of hook (world).</summary>
    public const float ShipPairMaxGuessBasisDistanceMeters = 45f;

    /// <summary>
    /// If cannon–hook chord exceeds paid rope + this (m), treat as in-flight / not bearing load — no ship–pair tow or yaw.
    /// Stops basis hull moving before the hook can physically span the rope.
    /// </summary>
    public const float ShipPairMaxChordOverPaidMeters = 12f;

    /// <summary>Reference in <c>scale = clamp(ref/(ref+μ), min, 1)</c>; μ = <c>total·towShare·basisShare</c> from <see cref="TryBuildShipPairTowDelta"/>.</summary>
    public const float ShipPairBowYawMassScaleReference = 2000f;

    /// <summary>Lower clamp on that ship–pair bow-yaw mass scale.</summary>
    public const float ShipPairBowYawMassScaleMin = 0.2f;

    /// <summary>Depth-first over <paramref name="root"/>’s attached slaves (harpoon may be nested under mounts).</summary>
    public static IEnumerable<Slave> EnumerateAttachedSlaveDescendants(Slave root)
    {
        foreach (var c in root.AttachedSlaves)
        {
            yield return c;
            foreach (var d in EnumerateAttachedSlaveDescendants(c))
                yield return d;
        }
    }

    /// <summary>
    /// True when an engaged ship-harpoon links these hulls (explicit <see cref="ShipHarpoonRopeState.HookBasisObjId"/> or world-hook guess).
    /// Used to skip ship–ship velocity damping that otherwise removes closing motion every tick while OBBs overlap.
    /// </summary>
    public static bool AreBoatHullsLinkedByEngagedShipHarpoon(Slave hullA, Slave hullB, IReadOnlyList<Slave>? shipsThisTick = null)
    {
        if (hullA.ObjId == hullB.ObjId)
            return false;
        if (AnyAttachedEngagedHarpoonPullsBasis(hullA, hullB.ObjId)
            || AnyAttachedEngagedHarpoonPullsBasis(hullB, hullA.ObjId))
            return true;
        if (shipsThisTick is not { Count: >= 2 })
            return false;
        return GuessedShipPairFromWorldHook(hullA, hullB, shipsThisTick)
               || GuessedShipPairFromWorldHook(hullB, hullA, shipsThisTick);
    }

    /// <summary>World-hook fallback only; explicit <see cref="ShipHarpoonRopeState.HookBasisObjId"/> is handled in <see cref="AnyAttachedEngagedHarpoonPullsBasis"/>.</summary>
    private static bool GuessedShipPairFromWorldHook(Slave towHull, Slave candidateBasis, IReadOnlyList<Slave> ships)
    {
        foreach (var node in EnumerateAttachedSlaveDescendants(towHull))
        {
            var st = node.HarpoonRope;
            if (!st.IsEngaged || st.HookBasisObjId != 0)
                continue;
            if (ResolveShipPairBasisSlave(towHull, node, st, ships) is { } resolved && resolved.ObjId == candidateBasis.ObjId)
                return true;
        }

        return false;
    }

    private static bool AnyAttachedEngagedHarpoonPullsBasis(Slave root, uint basisObjId)
    {
        foreach (var child in root.AttachedSlaves)
        {
            if (EngagedShipHarpoonPullsBasisInSubtree(child, basisObjId))
                return true;
        }

        return false;
    }

    private static bool EngagedShipHarpoonPullsBasisInSubtree(Slave node, uint basisObjId)
    {
        var st = node.HarpoonRope;
        if (st is { IsEngaged: true } && st.HookBasisObjId != 0 && st.HookBasisObjId == basisObjId)
            return true;

        foreach (var child in node.AttachedSlaves)
        {
            if (EngagedShipHarpoonPullsBasisInSubtree(child, basisObjId))
                return true;
        }

        return false;
    }

    /// <summary>Resolves the other boat hull for ship–ship tow: explicit basis id, or nearest boat when client sent world-only hook.</summary>
    private static Slave? ResolveShipPairBasisSlave(Slave towHull, Slave harpoonChild, in ShipHarpoonRopeState st, IReadOnlyList<Slave> shipsThisTick)
    {
        if (st.HookBasisObjId != 0)
        {
            var world = towHull.ParentWorld ?? WorldManager.Instance.GetWorld(towHull.Transform.InstanceId);
            if (world?.GetBaseUnit(st.HookBasisObjId) is Slave b
                && b.Template.IsABoat()
                && b.ObjId != towHull.ObjId)
                return b;
            return null;
        }

        if (st.HookAttachedToTerrain)
            return null;

        var hook = ShipHarpoonRopeController.GetHookWorldPosition(harpoonChild);
        Slave? best = null;
        var bestD2 = float.MaxValue;
        var maxD = ShipPairMaxGuessBasisDistanceMeters;
        var maxD2 = maxD * maxD;
        foreach (var s in shipsThisTick)
        {
            if (s.ObjId == towHull.ObjId || !s.Template.IsABoat())
                continue;
            var p = s.Transform.World.Position;
            var dx = hook.X - p.X;
            var dy = hook.Y - p.Y;
            var dz = hook.Z - p.Z;
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > maxD2 || d2 >= bestD2)
                continue;
            bestD2 = d2;
            best = s;
        }

        return best;
    }

    public static void ApplyTerrainHookTow(
        Slave hull,
        RigidBody rigidBody,
        float dtSec,
        float bowDirX,
        float bowDirZ,
        float speedToAlongVelScale,
        float maxForwardSpeed,
        float maxBackwardSpeed)
    {
        if (hull.AttachedSlaves.Count == 0 || dtSec <= 0f)
            return;

        var sum = JVector.Zero;
        foreach (var child in EnumerateAttachedSlaveDescendants(hull))
        {
            var st = child.HarpoonRope;
            if (!st.IsEngaged || !st.HookAttachedToTerrain)
                continue;

            var cannonPos = child.Transform.World.Position;
            var hook = ShipHarpoonRopeController.GetHookWorldPosition(child);
            var dist = Vector3.Distance(cannonPos, hook);
            var paid = st.RopeLength + ServerRopePaidLengthAdditiveMeters;
            if (paid > dist + SlackMarginMeters)
                continue;

            var stretch = MathF.Max(0f, dist - paid);
            var accel = MathF.Min(TowMaxAccel, TowAccelPerMeterStretch * stretch);
            if (accel <= 0f)
                continue;

            var hx = rigidBody.Position.X;
            var hz = rigidBody.Position.Z;
            var dx = hook.X - hx;
            var dz = hook.Y - hz;
            var len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < MinHookHorizontalDistance)
                continue;

            dx /= len;
            dz /= len;
            var imp = accel * dtSec;
            sum.X += dx * imp;
            sum.Z += dz * imp;
        }

        if (sum.LengthSquared() < 1e-10f)
            return;

        var vx = rigidBody.Velocity.X + sum.X;
        var vz = rigidBody.Velocity.Z + sum.Z;
        var alongVel = vx * bowDirX + vz * bowDirZ;
        var mul = speedToAlongVelScale;
        if (mul < 1e-4f)
            mul = 1e-4f;

        var maxAlong = maxForwardSpeed * mul;
        var minAlong = maxBackwardSpeed * mul;
        alongVel = Math.Clamp(alongVel, minAlong, maxAlong);

        var perpX = vx - alongVel * bowDirX;
        var perpZ = vz - alongVel * bowDirZ;

        rigidBody.Velocity = new JVector(
            alongVel * bowDirX + perpX,
            rigidBody.Velocity.Y,
            alongVel * bowDirZ + perpZ);

        hull.Speed = alongVel / mul;

        var sumLen = MathF.Sqrt(sum.X * sum.X + sum.Z * sum.Z);
        if (sumLen < 1e-6f)
            return;
        var sx = sum.X / sumLen;
        var sz = sum.Z / sumLen;
        var cross = bowDirX * sz - bowDirZ * sx;
        // ShipController sets angular velocity as -RotSpeed on rigid body Y — negate cross so bow turns toward pull.
        hull.RotSpeed += Math.Clamp(-cross * TowYawAssistRadPerSec * dtSec, -0.85f, 0.85f);
    }

    /// <summary>
    /// Late in the physics tick (after ship–ship overlap damping and cliff/doodad resolvers): harpoon engaged to another
    /// boat (explicit <see cref="ShipHarpoonRopeState.HookBasisObjId"/> or world-hook guess vs <paramref name="shipsThisTick"/>)
    /// applies a mass-weighted closing impulse on both rigid bodies when the rope is taut-ish, plus bow-yaw assist on tug and hooked hull toward the rope line. Descends <see cref="Slave.AttachedSlaves"/>
    /// so nested cannon slaves are included. Dominance uses mass × (1 + k·horizontal speed).
    /// </summary>
    public static void ApplyShipPairHarpoonTowImpulses(IReadOnlyList<Slave> shipsThisTick, float dtSec)
    {
        if (shipsThisTick.Count == 0 || dtSec <= 0f)
            return;

        foreach (var towHull in shipsThisTick)
        {
            var towRb = towHull.RigidBody;
            if (towRb is null || towHull.AttachedSlaves.Count == 0)
                continue;

            foreach (var child in EnumerateAttachedSlaveDescendants(towHull))
            {
                if (!TryBuildShipPairTowDelta(towHull, child, towRb, shipsThisTick, dtSec, out var basis, out var dvxTow, out var dvzTow, out var dvxBasis, out var dvzBasis, out var basisPullUx, out var basisPullUz, out var bowYawMassScale))
                    continue;

                towRb.Velocity += new JVector(dvxTow, 0f, dvzTow);
                ResyncSlaveSpeedFromRigidBodyAlongBow(towHull, towRb);
                // basisPull is (-dx,-dz) with (dx,dz) unit tow hull → hook; tug bow follows hook along rope.
                ApplyShipPairBowYawTowardPull(towHull, towRb, -basisPullUx, -basisPullUz, dtSec, bowYawMassScale);

                var bRb = basis.RigidBody;
                if (bRb is null)
                    continue;
                bRb.Velocity += new JVector(dvxBasis, 0f, dvzBasis);
                ResyncSlaveSpeedFromRigidBodyAlongBow(basis, bRb);
                ApplyShipPairBowYawTowardPull(basis, bRb, basisPullUx, basisPullUz, dtSec, bowYawMassScale);
            }
        }
    }

    private static bool TryBuildShipPairTowDelta(
        Slave towHull,
        Slave harpoonChild,
        RigidBody towHullRb,
        IReadOnlyList<Slave> shipsThisTick,
        float dtSec,
        out Slave basisShip,
        out float dvxTow,
        out float dvzTow,
        out float dvxBasis,
        out float dvzBasis,
        out float basisPullUnitX,
        out float basisPullUnitZ,
        out float bowYawMassScale)
    {
        basisShip = null!;
        dvxTow = dvzTow = dvxBasis = dvzBasis = 0f;
        basisPullUnitX = basisPullUnitZ = bowYawMassScale = 0f;

        var st = harpoonChild.HarpoonRope;
        if (!st.IsEngaged)
            return false;

        var basis = ResolveShipPairBasisSlave(towHull, harpoonChild, st, shipsThisTick);
        if (basis is null || basis.ObjId == towHull.ObjId)
            return false;

        var basisRb = basis.RigidBody;
        if (basisRb is null)
            return false;

        var hook = ShipHarpoonRopeController.GetHookWorldPosition(harpoonChild);
        var cannonPos = harpoonChild.Transform.World.Position;
        var dist = Vector3.Distance(cannonPos, hook);
        var paid = st.RopeLength + ServerRopePaidLengthAdditiveMeters;

        // In flight: chord >> paid — no tow (otherwise MinStretch invented force and yaw before hook lands).
        if (dist > paid + ShipPairMaxChordOverPaidMeters)
            return false;

        var slackCut = SlackMarginMeters + ShipPairExtraSlackMarginMeters;
        if (paid > dist + slackCut)
            return false;

        var rawStretch = dist - paid;
        // Chord shorter than paid = rope slack, not load-bearing — do not invent positive stretch.
        if (rawStretch <= 0f)
            return false;

        var stretch = MathF.Max(rawStretch, ShipPairMinStretchMeters);
        var accel = MathF.Min(TowMaxAccel, TowAccelPerMeterStretch * stretch);
        if (accel <= 0f)
            return false;

        var hx = towHullRb.Position.X;
        var hz = towHullRb.Position.Z;
        var dx = hook.X - hx;
        var dz = hook.Y - hz;
        var len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < MinHookHorizontalDistance)
            return false;

        dx /= len;
        dz /= len;

        var mTow = towHullRb.Mass;
        var mBasis = basisRb.Mass;
        var total = mTow + mBasis;
        if (total < 1e-3f)
            return false;

        var vTow = HorizontalSpeedXZ(towHullRb);
        var vBasis = HorizontalSpeedXZ(basisRb);
        var sTow = mTow * (1f + ShipPairDominanceSpeedCoeff * vTow);
        var sBasis = mBasis * (1f + ShipPairDominanceSpeedCoeff * vBasis);
        var impulseMul = sTow >= sBasis ? ShipPairDominantTowHullMul : ShipPairDominantBasisHullMul;

        var imp = accel * dtSec * impulseMul;
        var towShare = mBasis / total;
        var basisShare = mTow / total;

        dvxTow = dx * imp * towShare;
        dvzTow = dz * imp * towShare;
        dvxBasis = -dx * imp * basisShare;
        dvzBasis = -dz * imp * basisShare;

        basisPullUnitX = -dx;
        basisPullUnitZ = -dz;

        // μ = mTow·mBasis/total — same as total·towShare·basisShare (shares already used for impulses above).
        var reducedPairMass = total * towShare * basisShare;
        bowYawMassScale = Math.Clamp(
            ShipPairBowYawMassScaleReference / (ShipPairBowYawMassScaleReference + reducedPairMass),
            ShipPairBowYawMassScaleMin,
            1f);

        basisShip = basis;
        return true;
    }

    /// <summary>
    /// Nudge <see cref="Slave.RotSpeed"/> so the bow turns toward the horizontal pull unit (same 2D cross and clamp as terrain harpoon tow).
    /// Used for both tug and hooked hull during ship–pair harpoon tow; pull direction differs per hull.
    /// </summary>
    /// <param name="yawMassScale">From <see cref="TryBuildShipPairTowDelta"/> (same mass split as tow impulses).</param>
    private static void ApplyShipPairBowYawTowardPull(Slave slave, RigidBody rb, float pullUnitX, float pullUnitZ, float dtSec, float yawMassScale)
    {
        if (dtSec <= 0f)
            return;
        var pl = MathF.Sqrt(pullUnitX * pullUnitX + pullUnitZ * pullUnitZ);
        if (pl < 1e-5f)
            return;
        var px = pullUnitX / pl;
        var pz = pullUnitZ / pl;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rb.Orientation));
        var bowRad = rpy.Item1 + MathUtil.HalfPi;
        var fx = MathF.Cos(bowRad);
        var fz = MathF.Sin(bowRad);
        var cross = fx * pz - fz * px;
        // ShipController sets angular velocity from -RotSpeed on body Y — same sign as terrain tow pull assist.
        slave.RotSpeed += Math.Clamp(-cross * TowYawAssistRadPerSec * dtSec * yawMassScale, -0.85f, 0.85f);
    }

    private static float HorizontalSpeedXZ(RigidBody rb)
    {
        var vx = rb.Velocity.X;
        var vz = rb.Velocity.Z;
        return MathF.Sqrt(vx * vx + vz * vz);
    }

    /// <summary>Keeps <see cref="Slave.Speed"/> consistent with rigid-body XZ after an external velocity nudge (same bow convention as <see cref="ShipController"/>).</summary>
    private static void ResyncSlaveSpeedFromRigidBodyAlongBow(Slave slave, RigidBody rb)
    {
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rb.Orientation));
        var bowRad = rpy.Item1 + MathUtil.HalfPi;
        var fx = MathF.Cos(bowRad);
        var fz = MathF.Sin(bowRad);
        var mul = slave.MoveSpeedMul / 4f * MathF.Max(0.001f, slave.TurnSpeedVelocityMul);
        var along = rb.Velocity.X * fx + rb.Velocity.Z * fz;
        slave.Speed = along / mul;
    }
}
