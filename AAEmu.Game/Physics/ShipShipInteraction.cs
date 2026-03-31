#nullable enable

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Ship–hull overlap resolution. Server motion overwrites <see cref="RigidBody.Velocity"/> each tick from <see cref="Slave.Speed"/>,
/// so Jitter contacts do not prevent mesh-deep penetration — we depenetrate in XZ and damp closing speed.
/// SAT overlap uses a tight mass box (near physics hull) so reaction does not start far before visuals meet.
/// After overlap is detected, separation is scaled up to approximate client mesh extent without enlarging the hit test.
/// Centers use the same local mass-center offset as <see cref="ShipController.Build"/> (TransformedShape).
/// </summary>
public sealed class ShipShipInteraction
{
    private static class Tune
    {
        public static float HullDetectInflateLength => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDetectInflateLength
            : HullDetectInflateLengthDefault;

        public static float HullDetectInflateBeam => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDetectInflateBeam
            : HullDetectInflateBeamDefault;

        public static float BeamDetectTightenMul => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.BeamDetectTightenMul
            : BeamDetectTightenMulDefault;

        public static float MinPenetrationToAct => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MinPenetrationToAct
            : MinPenetrationToActDefault;

        public static float MinPenetrationToDamage => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MinPenetrationToDamage
            : MinPenetrationToDamageDefault;

        public static float TangentialRampDepthMeters => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.TangentialRampDepthMeters
            : TangentialRampDepthMetersDefault;

        public static float SeparationPushMultiplier => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.SeparationPushMultiplier
            : SeparationPushMultiplierDefault;

        public static float SeparationSlackMeters => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.SeparationSlackMeters
            : SeparationSlackMetersDefault;

        public static float ClosingSpeedDamp => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.ClosingSpeedDamp
            : ClosingSpeedDampDefault;

        public static float TangentialSlipDamp => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.TangentialSlipDamp
            : TangentialSlipDampDefault;

        public static float MinVerticalOverlap => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MinVerticalOverlap
            : MinVerticalOverlapDefault;

        public static int ResolvePasses => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.ResolvePasses
            : ResolvePassesDefault;

        public static int MaxPairIterations => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MaxPairIterations
            : MaxPairIterationsDefault;

        public static float DeepPenetrationStart => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.DeepPenetrationStart
            : DeepPenetrationStartDefault;

        public static float DeepPenetrationBoost => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.DeepPenetrationBoost
            : DeepPenetrationBoostDefault;

        public static float MinHalfSeparationMeters => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MinHalfSeparationMeters
            : MinHalfSeparationMetersDefault;

        public static float MinLinearSeparationToApplyMeters => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.MinLinearSeparationToApplyMeters
            : MinLinearSeparationToApplyMetersDefault;

        public static float NoseContactCosThreshold => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.NoseContactCosThreshold
            : NoseContactCosThresholdDefault;

        public static float HullCollisionDamageCooldownSec => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullCollisionDamageCooldownSec
            : HullCollisionDamageCooldownSecDefault;

        public static float HullDamageLowSpeedThresholdMps => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDamageLowSpeedThresholdMps
            : HullDamageLowSpeedThresholdMpsDefault;

        public static float HullDamageSpeedInterpMaxMps => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDamageInterpMaxMps
            : HullDamageSpeedInterpMaxMpsDefault;

        public static int HullDamageSpeedScaledMinPercent => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDamageSpeedScaledMinPercent
            : HullDamageSpeedScaledMinPercentDefault;

        public static int HullDamageSpeedScaledMaxPercent => AAEmu.Game.Physics.Debug.ShipTuningDebug.Enabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShipShipTuning.HullDamageSpeedScaledMaxPercent
            : HullDamageSpeedScaledMaxPercentDefault;
    }

    /// <summary>Length half-axis multiplier for overlap test only — keep close to 1.</summary>
    private const float HullDetectInflateLengthDefault = 1.025f;

    /// <summary>Beam half-axis multiplier for overlap test only.</summary>
    private const float HullDetectInflateBeamDefault = 1.015f;

    /// <summary>
    /// Extra tightening of beam in SAT only: <c>MassBoxSizeY</c> is often full deck/rail width, so side-by-side
    /// contact triggered long before hulls meet; length stays full for nose-to-nose.
    /// </summary>
    private const float BeamDetectTightenMulDefault = 0.78f;

    /// <summary>Ignore overlap response below this depth (m) — kills ghost drag from marginal SAT positives.</summary>
    private const float MinPenetrationToActDefault = 0.055f;

    /// <summary>Ignore periodic hull-damage for marginal SAT overlaps (m) — prevents "rubbing damage" from false contact.</summary>
    private const float MinPenetrationToDamageDefault = MinPenetrationToActDefault * 1.15f;

    /// <summary>Ramp tangential slip damp from 0 to full over this depth past <see cref="MinPenetrationToAct"/>.</summary>
    private const float TangentialRampDepthMetersDefault = 0.22f;

    /// <summary>Multiplies positional separation once overlap exists (mesh wider than mass box).</summary>
    private const float SeparationPushMultiplierDefault = 1.22f;

    /// <summary>Extra push after computed overlap so hulls do not sit exactly tangent (reduces z-fight / next-frame re-entry).</summary>
    private const float SeparationSlackMetersDefault = 0.020f;

    /// <summary>When overlapping, relative speed along separation normal is removed this fraction (1 = full stop along normal).</summary>
    private const float ClosingSpeedDampDefault = 1f;

    /// <summary>While hulls overlap, damp relative tangential slip in XZ to reduce slow grind-through.</summary>
    private const float TangentialSlipDampDefault = 0.86f;

    /// <summary>Minimum vertical overlap (m) to count as colliding (ignore ships far above/below).</summary>
    private const float MinVerticalOverlapDefault = 0.12f;

    private const int ResolvePassesDefault = 2;

    /// <summary>Per pair, per outer pass: depenetrate until separated or cap (handles sustained rubbing in one tick).</summary>
    private const int MaxPairIterationsDefault = 12;

    /// <summary>Extra separation scale when penetration exceeds this depth (m).</summary>
    private const float DeepPenetrationStartDefault = 0.12f;

    private const float DeepPenetrationBoostDefault = 0.72f;

    /// <summary>Floor on half-separation distance (m) so tiny SAT depths still produce a visible push.</summary>
    private const float MinHalfSeparationMetersDefault = 0.02f;

    /// <summary>
    /// If <c>halfSep + SeparationSlack</c> is below this (m), stop iterating — avoids high-frequency micro-pushes at marginal overlap.
    /// </summary>
    private const float MinLinearSeparationToApplyMetersDefault = 0.018f;

    /// <summary>Cosine threshold: other hull in this forward cone counts as “nose” hit (1% hull); else 3%.</summary>
    private const float NoseContactCosThresholdDefault = 0.65f;

    /// <summary>Min interval between hull-collision %HP ticks per ship while contact persists.</summary>
    private const float HullCollisionDamageCooldownSecDefault = 1.5f;

    /// <summary>At or below this relative speed (m/s) along separation axis, non–nose-to-nose hits use min % damage.</summary>
    private const float HullDamageLowSpeedThresholdMpsDefault = 2f;

    /// <summary>Relative speed (m/s) at which non–nose damage reaches <see cref="HullDamageSpeedScaledMaxPercent"/> (linear between thresholds).</summary>
    private const float HullDamageSpeedInterpMaxMpsDefault = 10f;

    private const int HullDamageSpeedScaledMinPercentDefault = 1;
    private const int HullDamageSpeedScaledMaxPercentDefault = 10;

    /// <summary>
    /// Run after all ships have had <see cref="ShipController.ApplyForceAndTorque"/> for this frame.
    /// </summary>
    public void ResolveAllPairs(IReadOnlyList<Slave> ships, TimeSpan deltaTime)
    {
        var dt = (float)deltaTime.TotalSeconds;
        if (ships.Count < 2)
            return;

        if (dt > 0f)
        {
            foreach (var s in ships)
                TickHullCollisionCooldowns(s, dt);
        }

        var pairDamagedThisTick = new HashSet<ulong>();

        for (var pass = 0; pass < Tune.ResolvePasses; pass++)
        {
            for (var i = 0; i < ships.Count; i++)
            {
                var sa = ships[i];
                if (sa.RigidBody is null || sa.RigidBody.Shapes.Count == 0)
                    continue;

                for (var j = i + 1; j < ships.Count; j++)
                {
                    var sb = ships[j];
                    if (sb.RigidBody is null || sb.RigidBody.Shapes.Count == 0)
                        continue;

                    if (!TryResolvePair(sa, sb, out var impactSpeedMps, out var maxPenetration))
                        continue;

                    // If SAT only detects a marginal overlap (e.g. due to discrete steps / tight mass-box),
                    // keep the depenetration but don't apply periodic hull damage.
                    if (maxPenetration < Tune.MinPenetrationToDamage)
                        continue;

                    const byte holdTicks = 10;
                    if (sa.ShipController != null)
                        sa.ShipController.Replication.ContactHoldTicks =
                            Math.Max(sa.ShipController.Replication.ContactHoldTicks, holdTicks);
                    if (sb.ShipController != null)
                        sb.ShipController.Replication.ContactHoldTicks =
                            Math.Max(sb.ShipController.Replication.ContactHoldTicks, holdTicks);

                    var minId = sa.Id < sb.Id ? sa.Id : sb.Id;
                    var maxId = sa.Id < sb.Id ? sb.Id : sa.Id;
                    var pairKey = ((ulong)minId << 32) | maxId;
                    if (!pairDamagedThisTick.Add(pairKey))
                        continue;

                    ApplyPairHullDamage(sa, sb, impactSpeedMps);
                }
            }
        }

        foreach (var slave in ships)
            SyncSlaveSpeedFromBowVelocity(slave);
    }

    private static void TickHullCollisionCooldowns(Slave s, float dt)
    {
        var map = s.ShipHullCollisionDamageCooldownByOtherShipId;
        if (map.Count == 0)
            return;

        var keys = new uint[map.Count];
        var n = 0;
        foreach (var k in map.Keys)
            keys[n++] = k;

        for (var i = 0; i < n; i++)
        {
            var k = keys[i];
            if (!map.TryGetValue(k, out var v))
                continue;
            v -= dt;
            if (v <= 0f)
                map.Remove(k);
            else
                map[k] = v;
        }
    }

    /// <summary>
    /// Ship with the other hull in its nose cone (contact through the bow) takes flat 1% — strong stem.
    /// The other takes speed-interpolated % (see <see cref="GetSpeedScaledHullDamagePercent"/>). Both can be 1% in a true nose-to-nose if each qualifies.
    /// </summary>
    private static void ApplyPairHullDamage(Slave sa, Slave sb, float impactSpeedMps)
    {
        var bodyA = sa.RigidBody!;
        var bodyB = sb.RigidBody!;
        var ma = sa.ShipController?.ShipModel;
        var mb = sb.ShipController?.ShipModel;
        if (ma is null || mb is null)
            return;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyA.Orientation));
        var rpyB = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyB.Orientation));
        var bowA = rpyA.Item1 + 1.57f;
        var bowB = rpyB.Item1 + 1.57f;

        GetMassBoxCenterXz(bodyA, ma, sa.Scale, out var ax, out var az);
        GetMassBoxCenterXz(bodyB, mb, sb.Scale, out var bx, out var bz);

        var scaledDamage = GetSpeedScaledHullDamagePercent(impactSpeedMps);
        var aHitsWithNose = IsOtherShipInNoseCone(bx - ax, bz - az, bowA);
        var bHitsWithNose = IsOtherShipInNoseCone(ax - bx, az - bz, bowB);
        var dmgA = aHitsWithNose ? Tune.HullDamageSpeedScaledMinPercent : scaledDamage;
        var dmgB = bHitsWithNose ? Tune.HullDamageSpeedScaledMinPercent : scaledDamage;

        if (!sa.ShipHullCollisionDamageCooldownByOtherShipId.TryGetValue(sb.Id, out var cdA) || cdA <= 0f)
        {
            sa.ApplyShipHullCollisionDamage(sb, dmgA);
            sa.ShipHullCollisionDamageCooldownByOtherShipId[sb.Id] = Tune.HullCollisionDamageCooldownSec;
        }

        if (!sb.ShipHullCollisionDamageCooldownByOtherShipId.TryGetValue(sa.Id, out var cdB) || cdB <= 0f)
        {
            sb.ApplyShipHullCollisionDamage(sa, dmgB);
            sb.ShipHullCollisionDamageCooldownByOtherShipId[sa.Id] = Tune.HullCollisionDamageCooldownSec;
        }
    }

    private static bool IsOtherShipInNoseCone(float toOtherX, float toOtherZ, float bowRadians)
    {
        var lenSq = toOtherX * toOtherX + toOtherZ * toOtherZ;
        if (lenSq < 0.04f)
            return false;

        var invLen = 1f / MathF.Sqrt(lenSq);
        var fx = MathF.Cos(bowRadians);
        var fz = MathF.Sin(bowRadians);
        var cosAng = (toOtherX * fx + toOtherZ * fz) * invLen;
            return cosAng >= Tune.NoseContactCosThreshold;
    }

    private static int GetSpeedScaledHullDamagePercent(float relativeSpeedMps)
    {
        if (relativeSpeedMps <= Tune.HullDamageLowSpeedThresholdMps)
            return Tune.HullDamageSpeedScaledMinPercent;
        if (relativeSpeedMps >= Tune.HullDamageSpeedInterpMaxMps)
            return Tune.HullDamageSpeedScaledMaxPercent;

        var span = Tune.HullDamageSpeedInterpMaxMps - Tune.HullDamageLowSpeedThresholdMps;
        var t = (relativeSpeedMps - Tune.HullDamageLowSpeedThresholdMps) / span;
        var f = Tune.HullDamageSpeedScaledMinPercent +
                t * (Tune.HullDamageSpeedScaledMaxPercent - Tune.HullDamageSpeedScaledMinPercent);
        return (int)MathF.Round(f);
    }

    private static bool TryResolvePair(Slave sa, Slave sb, out float impactSpeedMps, out float maxPenetration)
    {
        impactSpeedMps = 0f;
        maxPenetration = 0f;
        var bodyA = sa.RigidBody!;
        var bodyB = sb.RigidBody!;
        var ma = sa.ShipController?.ShipModel;
        var mb = sb.ShipController?.ShipModel;
        if (ma is null || mb is null)
            return false;

        var hadResponse = false;
        var peakImpactSpeedMps = 0f;
        var bbA = bodyA.Shapes[0].WorldBoundingBox;
        var bbB = bodyB.Shapes[0].WorldBoundingBox;
        var overlapY = MathF.Min(bbA.Max.Y, bbB.Max.Y) - MathF.Max(bbA.Min.Y, bbB.Min.Y);
        if (overlapY < Tune.MinVerticalOverlap)
            return false;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyA.Orientation));
        var rpyB = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyB.Orientation));
        var bowA = rpyA.Item1 + 1.57f;
        var bowB = rpyB.Item1 + 1.57f;

        // Interpretation:
        // - MassBoxSizeY = length (forward/back)
        // - MassBoxSizeX = beam (right/left)
        var halfLenA = ma.MassBoxSizeY * sa.Scale * 0.5f * Tune.HullDetectInflateLength;
        var halfLenB = mb.MassBoxSizeY * sb.Scale * 0.5f * Tune.HullDetectInflateLength;
        var satHalfWidA = ma.MassBoxSizeX * sa.Scale * 0.5f * Tune.HullDetectInflateBeam * Tune.BeamDetectTightenMul;
        var satHalfWidB = mb.MassBoxSizeX * sb.Scale * 0.5f * Tune.HullDetectInflateBeam * Tune.BeamDetectTightenMul;

        float ax, az, bx, bz, penetration, nx, nz;
        for (var iter = 0; iter < Tune.MaxPairIterations; iter++)
        {
            bbA = bodyA.Shapes[0].WorldBoundingBox;
            bbB = bodyB.Shapes[0].WorldBoundingBox;
            overlapY = MathF.Min(bbA.Max.Y, bbB.Max.Y) - MathF.Max(bbA.Min.Y, bbB.Min.Y);
            if (overlapY < Tune.MinVerticalOverlap)
                break;

            GetMassBoxCenterXz(bodyA, ma, sa.Scale, out ax, out az);
            GetMassBoxCenterXz(bodyB, mb, sb.Scale, out bx, out bz);

            if (!TryObbXzMinPenetration(
                    ax, az, bowA, halfLenA, satHalfWidA,
                    bx, bz, bowB, halfLenB, satHalfWidB,
                    out penetration,
                    out nx,
                    out nz))
                break;

            if (penetration <= 1e-4f || penetration < Tune.MinPenetrationToAct)
                break;

            maxPenetration = MathF.Max(maxPenetration, penetration);

            var dx = bx - ax;
            var dz = bz - az;
            if (nx * dx + nz * dz < 0f)
            {
                nx = -nx;
                nz = -nz;
            }

            var halfSep = penetration * 0.5f;
            var deep = MathF.Max(0f, penetration - Tune.DeepPenetrationStart);
            halfSep *= 1f + Tune.DeepPenetrationBoost * MathF.Min(1.25f, deep);
            var linearSep = halfSep + Tune.SeparationSlackMeters;
            if (linearSep < Tune.MinLinearSeparationToApplyMeters)
                break;

            var move = MathF.Max(linearSep, Tune.MinHalfSeparationMeters);
            move *= Tune.SeparationPushMultiplier;

            var va = bodyA.Velocity;
            var vb = bodyB.Velocity;
            var relAlongN = (va.X - vb.X) * nx + (va.Z - vb.Z) * nz;
            peakImpactSpeedMps = Math.Max(peakImpactSpeedMps, MathF.Abs(relAlongN));

            bodyA.Position -= new JVector(nx * move, 0f, nz * move);
            bodyB.Position += new JVector(nx * move, 0f, nz * move);
            hadResponse = true;

            DampPairVelocities(bodyA, bodyB, nx, nz, penetration);

            AAEmu.Game.Physics.Debug.ShipTuningDebug.OnResolvedShipPair(sa, sb, penetration, nx, nz, peakImpactSpeedMps);
        }

        if (hadResponse)
            impactSpeedMps = peakImpactSpeedMps;

        return hadResponse;
    }

    /// <summary>Removes relative motion into the other hull along <paramref name="nx"/>,<paramref name="nz"/>; tangential damp scales with penetration so parallel “air gap” drag stays low.</summary>
    private static void DampPairVelocities(RigidBody bodyA, RigidBody bodyB, float nx, float nz, float penetrationDepth)
    {
        var va = bodyA.Velocity;
        var vb = bodyB.Velocity;

        var closing = (va.X - vb.X) * nx + (va.Z - vb.Z) * nz;
        if (closing > 0f)
        {
            var remove = closing * 0.5f * Tune.ClosingSpeedDamp;
            va = new JVector(va.X - nx * remove, va.Y, va.Z - nz * remove);
            vb = new JVector(vb.X + nx * remove, vb.Y, vb.Z + nz * remove);
        }

        var tangentialBlend = Math.Clamp((penetrationDepth - Tune.MinPenetrationToAct) / Tune.TangentialRampDepthMeters, 0f, 1f);
        var tx = -nz;
        var tz = nx;
        var relT = (va.X - vb.X) * tx + (va.Z - vb.Z) * tz;
        var slipRemove = relT * 0.5f * (1f - Tune.TangentialSlipDamp) * tangentialBlend;
        va = new JVector(va.X - tx * slipRemove, va.Y, va.Z - tz * slipRemove);
        vb = new JVector(vb.X + tx * slipRemove, vb.Y, vb.Z + tz * slipRemove);

        bodyA.Velocity = va;
        bodyB.Velocity = vb;
    }

    /// <summary>2D SAT on XZ for rectangles aligned with ship bow (same convention as <see cref="ShipController"/>).</summary>
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

    /// <summary>World XZ of mass-box center (matches <see cref="ShipController"/> TransformedShape offset).</summary>
    private static void GetMassBoxCenterXz(RigidBody body, ShipModelV1 model, float scale, out float cx, out float cz)
    {
        var local = new JVector(model.MassCenterX * scale, model.MassCenterZ * scale, model.MassCenterY * scale);
        var w = RotateVectorByQuaternion(local, body.Orientation);
        cx = body.Position.X + w.X;
        cz = body.Position.Z + w.Z;
    }

    private static JVector RotateVectorByQuaternion(JVector v, JQuaternion q)
    {
        var qx = q.X;
        var qy = q.Y;
        var qz = q.Z;
        var qw = q.W;
        var tx = 2f * (qy * v.Z - qz * v.Y);
        var ty = 2f * (qz * v.X - qx * v.Z);
        var tz = 2f * (qx * v.Y - qy * v.X);
        return new JVector(
            v.X + qw * tx + (qy * tz - qz * ty),
            v.Y + qw * ty + (qz * tx - qx * tz),
            v.Z + qw * tz + (qx * ty - qy * tx));
    }

    /// <summary>
    /// <see cref="ShipController.ApplyForceAndTorque"/> maps game <see cref="Slave.Speed"/> to horizontal velocity as
    /// <c>Speed * MoveSpeedMul / 4 * TurnSpeedVelocityMul</c> along the bow — not 1:1 with physics m/s.
    /// After we edit <see cref="RigidBody.Velocity"/>, game speed must be recovered with the same scale.
    /// </summary>
    private static void SyncSlaveSpeedFromBowVelocity(Slave slave)
    {
        var rb = slave.RigidBody;
        if (rb is null)
            return;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rb.Orientation));
        var bow = rpy.Item1 + 1.57f;
        var alongPhys = rb.Velocity.X * MathF.Cos(bow) + rb.Velocity.Z * MathF.Sin(bow);
        var denom = (slave.MoveSpeedMul / 4f) * slave.TurnSpeedVelocityMul;
        if (denom < 1e-5f)
            return;
        slave.Speed = alongPhys / denom;
    }
}
