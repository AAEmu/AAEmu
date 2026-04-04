#nullable enable

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Debug;
using AAEmu.Game.Physics.Util;
using static AAEmu.Game.Physics.ShipShipInteraction.ShipHullPairDefaults;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Ship–hull overlap resolution. Server motion overwrites <see cref="RigidBody.Velocity"/> each tick from <see cref="Slave.Speed"/>,
/// so Jitter contacts do not prevent mesh-deep penetration — we depenetrate in XZ and damp closing speed.
/// SAT overlap uses a tight mass box (near physics hull) so reaction does not start far before visuals meet.
/// After overlap is detected, separation is scaled up to approximate client mesh extent without enlarging the hit test.
/// Separation and velocity damping are mass-weighted; see <see cref="ShipShipInteraction.ShipHullPairDefaults.MassPushStrength"/>.
/// Centers use the same local mass-center offset as <see cref="ShipController.Build"/> (TransformedShape).
/// </summary>
public sealed class ShipShipInteraction
{
    /// <summary>Ship↔ship SAT/response constants (single source of truth).</summary>
    public static class ShipHullPairDefaults
    {
        /// <summary>Length inflation for the SAT hit-test OBB (starts detection slightly earlier along bow/stern).</summary>
        public const float HullDetectInflateLength = 1.025f;
        /// <summary>Beam inflation for the SAT hit-test OBB (starts detection slightly earlier side-to-side).</summary>
        public const float HullDetectInflateBeam = 1.015f;
        /// <summary>Tighten the beam used for detection (helps keep “air gap” drag low when ships pass nearby).</summary>
        public const float BeamDetectTightenMul = 0.78f;
        /// <summary>Ignore overlaps smaller than this (m) to avoid jitter from tiny penetrations.</summary>
        public const float MinPenetrationToAct = 0.055f;
        /// <summary>Minimum overlap depth (m) required to apply periodic hull collision damage.</summary>
        public const float MinPenetrationToDamage = MinPenetrationToAct * 1.15f;
        /// <summary>Penetration depth (m) over which tangential slip damping ramps from 0 to full.</summary>
        public const float TangentialRampDepthMeters = 0.22f;
        /// <summary>Multiplier for the computed separation distance (stronger push-out when overlapped).</summary>
        public const float SeparationPushMultiplier = 1.22f;
        /// <summary>Extra separation slack (m) added to prevent immediate re-penetration after depenetration.</summary>
        public const float SeparationSlackMeters = 0.020f;
        /// <summary>Damping factor for relative closing speed into the contact normal (1 = remove fully).</summary>
        public const float ClosingSpeedDamp = 1f;
        /// <summary>Fraction of tangential relative motion removed at full ramp (lower = more slip).</summary>
        public const float TangentialSlipDamp = 0.86f;
        /// <summary>Minimum vertical (Y) AABB overlap (m) required before attempting XZ SAT (cheap reject).</summary>
        public const float MinVerticalOverlap = 0.12f;
        /// <summary>Number of passes over all ship pairs per tick (extra passes help resolve chain overlaps).</summary>
        public const int ResolvePasses = 2;
        /// <summary>Max iterations per pair while still overlapping (prevents pathological loops).</summary>
        public const int MaxPairIterations = 12;
        /// <summary>Penetration depth (m) above which separation begins to get an extra “deep overlap” boost.</summary>
        public const float DeepPenetrationStart = 0.12f;
        /// <summary>Scale of the “deep overlap” separation boost.</summary>
        public const float DeepPenetrationBoost = 0.72f;
        /// <summary>Minimum per-side separation (m) to apply even when computed overlap is tiny.</summary>
        public const float MinHalfSeparationMeters = 0.02f;
        /// <summary>Skip applying separation if computed linear separation is below this (m) (noise guard).</summary>
        public const float MinLinearSeparationToApplyMeters = 0.018f;
        /// <summary>Cosine threshold for considering a contact “nose/bow hit” (affects damage logic).</summary>
        public const float NoseContactCosThreshold = 0.65f;
        /// <summary>Cooldown (seconds) per other ship for applying %HP hull collision damage.</summary>
        public const float HullCollisionDamageCooldownSec = 1.5f;
        /// <summary>Below this relative impact speed (m/s), hull damage uses the minimum percent.</summary>
        public const float HullDamageLowSpeedThresholdMps = 2f;
        /// <summary>At or above this relative impact speed (m/s), hull damage uses the maximum percent.</summary>
        public const float HullDamageSpeedInterpMaxMps = 10f;
        /// <summary>Minimum periodic hull damage (percent of max HP) for eligible collisions.</summary>
        public const int HullDamageSpeedScaledMinPercent = 1;
        /// <summary>Maximum periodic hull damage (percent of max HP) for eligible collisions.</summary>
        public const int HullDamageSpeedScaledMaxPercent = 10;
        /// <summary>How strongly mass affects who gets pushed more (1 = pure inverse-mass split; &gt;1 exaggerates).</summary>
        public const float MassPushStrength = 2f;
    }

    /// <summary>
    /// Run after all ships have had <see cref="ShipController.ApplyForceAndTorque"/> for this frame.
    /// Pair loop is O(N²); <see cref="TryResolvePair"/> cheaply rejects distant hulls via world AABB on XZ before SAT.
    /// For very large N, add a spatial hash / uniform grid on XZ so only nearby indices are paired.
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

        for (var pass = 0; pass < ResolvePasses; pass++)
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
                    if (maxPenetration < MinPenetrationToDamage)
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
        var dmgA = aHitsWithNose ? HullDamageSpeedScaledMinPercent : scaledDamage;
        var dmgB = bHitsWithNose ? HullDamageSpeedScaledMinPercent : scaledDamage;

        if (!sa.ShipHullCollisionDamageCooldownByOtherShipId.TryGetValue(sb.Id, out var cdA) || cdA <= 0f)
        {
            sa.ApplyShipHullCollisionDamage(sb, dmgA);
            sa.ShipHullCollisionDamageCooldownByOtherShipId[sb.Id] = HullCollisionDamageCooldownSec;
        }

        if (!sb.ShipHullCollisionDamageCooldownByOtherShipId.TryGetValue(sa.Id, out var cdB) || cdB <= 0f)
        {
            sb.ApplyShipHullCollisionDamage(sa, dmgB);
            sb.ShipHullCollisionDamageCooldownByOtherShipId[sa.Id] = HullCollisionDamageCooldownSec;
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
            return cosAng >= NoseContactCosThreshold;
    }

    private static int GetSpeedScaledHullDamagePercent(float relativeSpeedMps)
    {
        if (relativeSpeedMps <= HullDamageLowSpeedThresholdMps)
            return HullDamageSpeedScaledMinPercent;
        if (relativeSpeedMps >= HullDamageSpeedInterpMaxMps)
            return HullDamageSpeedScaledMaxPercent;

        var span = HullDamageSpeedInterpMaxMps - HullDamageLowSpeedThresholdMps;
        var t = (relativeSpeedMps - HullDamageLowSpeedThresholdMps) / span;
        var f = HullDamageSpeedScaledMinPercent +
                t * (HullDamageSpeedScaledMaxPercent - HullDamageSpeedScaledMinPercent);
        return (int)MathF.Round(f);
    }

    /// <summary>True if world-space AABBs overlap on X and Z (conservative broadphase for hull SAT on XZ).</summary>
    private static bool HaveWorldAabbOverlapXz(in JBoundingBox bbA, in JBoundingBox bbB)
    {
        var ox = MathF.Min(bbA.Max.X, bbB.Max.X) - MathF.Max(bbA.Min.X, bbB.Min.X);
        var oz = MathF.Min(bbA.Max.Z, bbB.Max.Z) - MathF.Max(bbA.Min.Z, bbB.Min.Z);
        return ox > 0f && oz > 0f;
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
        if (!HaveWorldAabbOverlapXz(in bbA, in bbB))
            return false;

        var overlapY = MathF.Min(bbA.Max.Y, bbB.Max.Y) - MathF.Max(bbA.Min.Y, bbB.Min.Y);
        if (overlapY < MinVerticalOverlap)
            return false;

        var rpyA = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyA.Orientation));
        var rpyB = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(bodyB.Orientation));
        var bowA = rpyA.Item1 + 1.57f;
        var bowB = rpyB.Item1 + 1.57f;

        // Interpretation:
        // - MassBoxSizeY = length (forward/back)
        // - MassBoxSizeX = beam (right/left)
        var halfLenA = ma.MassBoxSizeY * sa.Scale * 0.5f * HullDetectInflateLength;
        var halfLenB = mb.MassBoxSizeY * sb.Scale * 0.5f * HullDetectInflateLength;
        var satHalfWidA = ma.MassBoxSizeX * sa.Scale * 0.5f * HullDetectInflateBeam * BeamDetectTightenMul;
        var satHalfWidB = mb.MassBoxSizeX * sb.Scale * 0.5f * HullDetectInflateBeam * BeamDetectTightenMul;

        float ax, az, bx, bz, penetration, nx, nz;
        for (var iter = 0; iter < MaxPairIterations; iter++)
        {
            bbA = bodyA.Shapes[0].WorldBoundingBox;
            bbB = bodyB.Shapes[0].WorldBoundingBox;
            if (!HaveWorldAabbOverlapXz(in bbA, in bbB))
                break;

            overlapY = MathF.Min(bbA.Max.Y, bbB.Max.Y) - MathF.Max(bbA.Min.Y, bbB.Min.Y);
            if (overlapY < MinVerticalOverlap)
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

            if (penetration <= 1e-4f || penetration < MinPenetrationToAct)
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
            var deep = MathF.Max(0f, penetration - DeepPenetrationStart);
            halfSep *= 1f + DeepPenetrationBoost * MathF.Min(1.25f, deep);
            var linearSep = halfSep + SeparationSlackMeters;
            if (linearSep < MinLinearSeparationToApplyMeters)
                break;

            var move = MathF.Max(linearSep, MinHalfSeparationMeters);
            move *= SeparationPushMultiplier;

            var va = bodyA.Velocity;
            var vb = bodyB.Velocity;
            var relAlongN = (va.X - vb.X) * nx + (va.Z - vb.Z) * nz;
            peakImpactSpeedMps = Math.Max(peakImpactSpeedMps, MathF.Abs(relAlongN));

            GetPairMassWeights(bodyA.Mass, bodyB.Mass, out var wA, out var wB);
            var moveA = 2f * move * wA;
            var moveB = 2f * move * wB;
            bodyA.Position -= new JVector(nx * moveA, 0f, nz * moveA);
            bodyB.Position += new JVector(nx * moveB, 0f, nz * moveB);
            hadResponse = true;

            DampPairVelocities(bodyA, bodyB, nx, nz, penetration, wA, wB);

            ShipTuningDebug.OnResolvedShipPair(sa, sb, penetration, nx, nz, peakImpactSpeedMps);
        }

        if (hadResponse)
            impactSpeedMps = peakImpactSpeedMps;

        return hadResponse;
    }

    /// <summary>Removes relative motion into the other hull along <paramref name="nx"/>,<paramref name="nz"/>; tangential damp scales with penetration so parallel “air gap” drag stays low.</summary>
    private static void DampPairVelocities(
        RigidBody bodyA,
        RigidBody bodyB,
        float nx,
        float nz,
        float penetrationDepth,
        float weightA,
        float weightB)
    {
        var va = bodyA.Velocity;
        var vb = bodyB.Velocity;

        var closing = (va.X - vb.X) * nx + (va.Z - vb.Z) * nz;
        if (closing > 0f)
        {
            var removeA = closing * weightA * ClosingSpeedDamp;
            var removeB = closing * weightB * ClosingSpeedDamp;
            va = new JVector(va.X - nx * removeA, va.Y, va.Z - nz * removeA);
            vb = new JVector(vb.X + nx * removeB, vb.Y, vb.Z + nz * removeB);
        }

        var tangentialBlend = Math.Clamp((penetrationDepth - MinPenetrationToAct) / TangentialRampDepthMeters, 0f, 1f);
        var tx = -nz;
        var tz = nx;
        var relT = (va.X - vb.X) * tx + (va.Z - vb.Z) * tz;
        var slipK = (1f - TangentialSlipDamp) * tangentialBlend;
        var slipA = relT * weightA * slipK;
        var slipB = relT * weightB * slipK;
        va = new JVector(va.X - tx * slipA, va.Y, va.Z - tz * slipA);
        vb = new JVector(vb.X + tx * slipB, vb.Y, vb.Z + tz * slipB);

        bodyA.Velocity = va;
        bodyB.Velocity = vb;
    }

    /// <summary>
    /// Mass-based split from inverse mass ratio, scaled by <see cref="ShipShipInteraction.ShipHullPairDefaults.MassPushStrength"/> (blend toward 50/50 or exaggerate).
    /// </summary>
    private static void GetPairMassWeights(float massA, float massB, out float weightA, out float weightB)
    {
        var sum = massA + massB;
        if (sum < 1e-6f || massA <= 0f || massB <= 0f)
        {
            weightA = weightB = 0.5f;
            return;
        }

        var inv = 1f / sum;
        var wPhysA = massB * inv;
        var wPhysB = massA * inv;
        var t = MassPushStrength;
        weightA = 0.5f + (wPhysA - 0.5f) * t;
        weightB = 0.5f + (wPhysB - 0.5f) * t;
        const float eps = 1e-4f;
        weightA = Math.Clamp(weightA, eps, 1f - eps);
        weightB = 1f - weightA;
    }

    /// <summary>2D SAT on XZ for rectangles aligned with ship bow (same convention as <see cref="ShipController"/>).</summary>
    internal static bool TryObbXzMinPenetration(
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

    internal static float ProjectObbRadiusXz(float halfLen, float halfWid, float bow, float nx, float nz)
    {
        var d1 = MathF.Cos(bow) * nx + MathF.Sin(bow) * nz;
        var d2 = -MathF.Sin(bow) * nx + MathF.Cos(bow) * nz;
        return halfLen * MathF.Abs(d1) + halfWid * MathF.Abs(d2);
    }

    /// <summary>World XZ of mass-box center (matches <see cref="ShipController"/> TransformedShape offset).</summary>
    internal static void GetMassBoxCenterXz(RigidBody body, ShipModelV1 model, float scale, out float cx, out float cz)
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
    internal static void SyncSlaveSpeedFromBowVelocity(Slave slave)
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
