#nullable enable

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Debug;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using System.Numerics;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Ship interaction with terrain / shoreline: friction on dry ground, probes, latch, penetration resolve, and periodic beached hull damage.
/// </summary>
public sealed class ShipShoreInteraction
{
    /// <summary>Production constants for ship↔shore (single source of truth).</summary>
    public static class ShorePhysicsDefaults
    {
        /// <summary>Offset of the “bottom” point used for penetration, as a fraction of hull height (0 = rigidbody center).</summary>
        public const float BoatBottomOffsetFracOfSizeZ = 0f;
        /// <summary>Linear damping strength on dry ground (higher = faster slowdown on land).</summary>
        public const float GroundFriction = 0.1f;
        /// <summary>Extra velocity/ang-vel damping multiplier when fully on land (1 = no extra damping).</summary>
        public const float DryGroundCollisionDamping = 1f;
        /// <summary>Roll dead-zone (rad) when “settled” on land before applying corrective torque.</summary>
        public const float DryGroundRollDeadZoneRad = 0.1f;
        /// <summary>Strength of the corrective roll torque on land (scaled by mass).</summary>
        public const float DryGroundRollTorqueMul = 0.1f;
        /// <summary>Bow probe distance multiplier (legacy; current probe is clamped to OBB face).</summary>
        public const float BowProbeMul = 1.5f;
        /// <summary>Stern probe distance multiplier (legacy; current probe is clamped to OBB face).</summary>
        public const float SternProbeMul = 1.5f;
        /// <summary>Cliff/wall probe multiplier (0 disables the cliff probe logic).</summary>
        public const float CliffProbeMul = 0f;
        /// <summary>Look-ahead for cliff probe, as a multiple of half-length (0 disables).</summary>
        public const float CliffProbeLookAheadMulOfHalfLength = 0f;
        /// <summary>Minimum cliff probe look-ahead (m) when enabled.</summary>
        public const float CliffProbeMinLookAheadMeters = 0.25f;
        /// <summary>Threshold for treating terrain as “cliff” based on slope fraction.</summary>
        public const float CliffSlopeFracThreshold = 0.70f;
        /// <summary>Required margin above water (m) to consider a cliff valid (helps ignore wave noise).</summary>
        public const float CliffAboveWaterMargin = 0.30f;
        /// <summary>Hysteresis (m): how far below water the probe floor must be to latch “aground”.</summary>
        public const float ShoreEnterHyst = 0.5f;
        /// <summary>Hysteresis (m): how far below water the probe floor must be to release back to water.</summary>
        public const float ShoreExitHyst = 0.35f;
        /// <summary>Low-pass response rate for floor height smoothing (higher = less lag, more noise).</summary>
        public const float FloorSmoothResponse = 16.0f;
        /// <summary>Band (m) near shore where pre-latch damping is applied to soften the approach.</summary>
        public const float PreShoreBand = 0.5f;
        /// <summary>Penetration depth epsilon (m) below which we don't push the hull up (noise guard).</summary>
        public const float PenetrationEpsilon = 0.04f;
        /// <summary>Response rate for penetration resolution (higher = faster “push up” out of terrain).</summary>
        public const float PenetrationResponse = 4.5f;
        /// <summary>Max upward correction step (m) per tick shortly after latching (prevents snapping).</summary>
        public const float MaxUpStepEarly = 0.04f;
        /// <summary>Max upward correction step (m) per tick after the early window (allows stronger escape).</summary>
        public const float MaxUpStepLate = 0.07f;
        /// <summary>Max visual pitch (deg) applied while grounded (client feedback only).</summary>
        public const float VisualGroundPitchMaxDeg = 8.0f;
        /// <summary>Probe distance (m) used to compute visual ground pitch (client feedback only).</summary>
        public const float VisualGroundPitchProbeDistance = 6.0f;
        /// <summary>Response rate for visual ground pitch smoothing (higher = snappier).</summary>
        public const float VisualGroundPitchResponse = 2.0f;
        /// <summary>Secondary smoothing response for the visual pitch floor sample (reduces jitter).</summary>
        public const float VisualPitchFloorSmoothResponse = 8.0f;
    }

    /// <summary>
    /// When the hull center reads as above water (no submergence), apply ground friction and clear controls if settled.
    /// Call before rudder/throttle smoothing in the ship physics tick.
    /// </summary>
    public void ApplyOnLandPhysics(Slave slave, TimeSpan deltaTime)
    {
        if (slave.RigidBody == null)
            return;

        var submergedDepth = Math.Max(0, slave.CachedWaterSurface - slave.RigidBody.Position.Y);
        var isOnWater = submergedDepth > 0;
        // With Max(0, …), depth is never negative: !isOnWater implies depth == 0.
        var isOnLand = !isOnWater;

        if (!isOnLand)
            return;

        var groundFriction = ShorePhysicsDefaults.GroundFriction;
        var frictionForce = new JVector(-slave.RigidBody.Velocity.X * groundFriction, 0, -slave.RigidBody.Velocity.Z * groundFriction);
        slave.RigidBody.AddForce(frictionForce);

        var dryDamp = ShorePhysicsDefaults.DryGroundCollisionDamping;
        slave.RigidBody.Velocity *= dryDamp;
        slave.RigidBody.AngularVelocity *= dryDamp;

        if (slave.RigidBody.Velocity.Length() < 0.01f)
        {
            slave.RigidBody.Velocity = JVector.Zero;
            slave.RigidBody.AngularVelocity = JVector.Zero;

            var rollAngle = PhysicsUtil.GetYawPitchRollFromJMatrix(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation)).Item2;
            var rollDeadZone = ShorePhysicsDefaults.DryGroundRollDeadZoneRad;
            if (Math.Abs(rollAngle) < rollDeadZone)
            {
                var rollTorqueMul = ShorePhysicsDefaults.DryGroundRollTorqueMul;
                var correctionTorque = new JVector(0, 0, -rollAngle * slave.RigidBody.Mass * rollTorqueMul);
                slave.RigidBody.AddForce(correctionTorque);
            }

            // Only clear controls when the ship is settled AND player is not actively providing input.
            // Otherwise we can accidentally cancel "escape throttle" attempts while beached.
            if (slave.ThrottleRequest == 0 && slave.SteeringRequest == 0)
            {
                slave.Throttle = 0;
                slave.Steering = 0;
                slave.ThrottleSmoothed = 0f;
                slave.SteeringSmoothed = 0f;
            }
        }
    }

    /// <summary>
    /// Cliff / shore probes, ground latch, vertical resolve, damping. Then hull damage while latched on shore.
    /// </summary>
    public void ResolveTerrainContacts(Slave slave, TimeSpan deltaTime, Jitter2.World physWorld)
    {
        ResolveLandCollisions(slave, deltaTime, physWorld);
        slave.TickBeachedHullDamage(deltaTime);
    }

    /// <summary>
    /// Visual-only pitch on shoal/ground: aligns replicated nose/stern to local terrain slope (does not move rigidbody).
    /// </summary>
    public void UpdateVisualGroundPitch(Slave slave, RigidBody rigidBody, TimeSpan deltaTime)
    {
        var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));

        var groundPitchMaxDeg = ShorePhysicsDefaults.VisualGroundPitchMaxDeg;
        var groundPitchProbeDistance = ShorePhysicsDefaults.VisualGroundPitchProbeDistance;
        var groundPitchResponse = ShorePhysicsDefaults.VisualGroundPitchResponse;
        var targetGroundPitch = 0f;
        if (slave.ParentWorld != null && (slave.CachedFloorLevel > slave.CachedWaterSurface || slave.GroundContactLatched))
        {
            var yaw = rpy.Item1 + 1.57f;
            var cosYaw = MathF.Cos(yaw);
            var sinYaw = MathF.Sin(yaw);
            var cx = rigidBody.Position.X;
            var cy = rigidBody.Position.Z;
            var frontX = cx + cosYaw * groundPitchProbeDistance;
            var frontY = cy + sinYaw * groundPitchProbeDistance;
            var backX = cx - cosYaw * groundPitchProbeDistance;
            var backY = cy - sinYaw * groundPitchProbeDistance;
            var frontH = slave.ParentWorld.GetHeight(frontX, frontY);
            var backH = slave.ParentWorld.GetHeight(backX, backY);

            var pitchFloorSmoothResponse = ShorePhysicsDefaults.VisualPitchFloorSmoothResponse;
            var floorA = 1f - MathF.Exp(-pitchFloorSmoothResponse * dt);
            if (!slave.GroundPitchFloorSmoothingSeeded)
            {
                slave.GroundPitchFrontFloorSmoothed = frontH;
                slave.GroundPitchBackFloorSmoothed = backH;
                slave.GroundPitchFloorSmoothingSeeded = true;
            }
            else
            {
                slave.GroundPitchFrontFloorSmoothed += (frontH - slave.GroundPitchFrontFloorSmoothed) * floorA;
                slave.GroundPitchBackFloorSmoothed += (backH - slave.GroundPitchBackFloorSmoothed) * floorA;
            }

            var slopeRad = MathF.Atan2(slave.GroundPitchFrontFloorSmoothed - slave.GroundPitchBackFloorSmoothed, groundPitchProbeDistance * 2f);
            targetGroundPitch = Math.Clamp(slopeRad, -groundPitchMaxDeg.DegToRad(), groundPitchMaxDeg.DegToRad());

            if (slave.GroundedByStern)
                targetGroundPitch = -targetGroundPitch;
        }
        else
            slave.GroundPitchFloorSmoothingSeeded = false;

        var pitchA = 1f - MathF.Exp(-groundPitchResponse * dt);
        slave.GroundPitchAngle += (targetGroundPitch - slave.GroundPitchAngle) * pitchA;
    }

    private static void ResolveLandCollisions(Slave slave, TimeSpan deltaTime, Jitter2.World physWorld)
    {
        if (slave.ShipController?.ShipModel is null || slave.RigidBody == null || slave.ParentWorld == null)
            return;

        var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);

        // "Bottom" for penetration is not the rigidbody center. Use a tunable fraction of hull height.
        // This reduces the visible gap when the ship becomes grounded/beached.
        var boatBottomOffsetFrac = ShorePhysicsDefaults.BoatBottomOffsetFracOfSizeZ;
        var hullHeight = MathF.Max(0.01f, slave.ShipController.ShipModel.MassBoxSizeZ * slave.Scale);
        var boatBottom = slave.RigidBody.Position.Y - hullHeight * boatBottomOffsetFrac;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation));
        var heading = rpy.Item1 + 1.57f;
        var dirX = MathF.Cos(heading);
        var dirZ = MathF.Sin(heading);

        var vX = slave.RigidBody.Velocity.X;
        var vZ = slave.RigidBody.Velocity.Z;
        var along = vX * dirX + vZ * dirZ;
        var movingBackward = along < -0.05f || (MathF.Abs(along) <= 0.05f && slave.ThrottleRequest < 0);

        var useSternProbe = slave.GroundContactLatched ? slave.GroundedByStern : movingBackward;
        var probeSign = useSternProbe ? -1f : 1f;
        // Shore probe: ray from rigidbody center to OBB face (bow/stern) only.
        // Do NOT add Bow/SternProbeMul * halfLength here — that pushed the sample far past the box face (~+0.75 hull length
        // with default 1.5 mul) and caused early shore latch / "invisible wall" meters before the hull reached terrain.
        var halfLength = 0.5f * slave.ShipController.ShipModel.MassBoxSizeY * slave.Scale;
        var centerOffsetAlongLength = slave.ShipController.ShipModel.MassCenterY * slave.Scale;
        var distFromCenterToFace = probeSign > 0f ? (halfLength + centerOffsetAlongLength) : (halfLength - centerOffsetAlongLength);
        distFromCenterToFace = MathF.Max(0.01f, distFromCenterToFace);

        var probeDist = distFromCenterToFace;
        var probeX = slave.RigidBody.Position.X + dirX * probeDist * probeSign;
        var probeY = slave.RigidBody.Position.Z + dirZ * probeDist * probeSign;
        var contactFloor = slave.ParentWorld.GetHeight(probeX, probeY);
        var rb = slave.RigidBody;
        var waterAtProbe = slave.ParentWorld.Water.GetWaterSurface(
            new Vector3(probeX, probeY, rb.Position.Y),
            out _);

        // WALL/CLIFF logic removed per request.
        // Keep debug probe output fields populated to avoid changing debug marker wiring.
        var cliffX = probeX;
        var cliffY = probeY;
        var cliffFloor = contactFloor;

        var shoreEnterHyst = ShorePhysicsDefaults.ShoreEnterHyst;
        var shoreExitHyst = ShorePhysicsDefaults.ShoreExitHyst;

        var floorSmoothResponse = ShorePhysicsDefaults.FloorSmoothResponse;
        {
            var a = 1f - MathF.Exp(-floorSmoothResponse * dt);
            if (!slave.GroundContactLatched && !slave.GroundContactFloorSmoothingSeeded)
            {
                slave.GroundContactFloorSmoothed = contactFloor;
                slave.GroundContactFloorSmoothingSeeded = true;
            }
            else
                slave.GroundContactFloorSmoothed += (contactFloor - slave.GroundContactFloorSmoothed) * a;
        }
        var floorSmoothed = slave.GroundContactFloorSmoothed;

        var preShoreBand = ShorePhysicsDefaults.PreShoreBand;
        // Use water surface at the probe point, not at ship center; near shore these can differ.
        var enterDelta = (waterAtProbe + shoreEnterHyst) - floorSmoothed;
        var exitDelta = (waterAtProbe + shoreExitHyst) - floorSmoothed;
        if (!slave.GroundContactLatched && enterDelta >= 0f && enterDelta <= preShoreBand)
        {
            var v = slave.RigidBody.Velocity;
            var t = 1f - (enterDelta / preShoreBand);
            var damp = 1f - 0.85f * t;
            slave.RigidBody.Velocity = new JVector(v.X, v.Y * damp, v.Z);
        }

        if (!slave.GroundContactLatched)
        {
            if (enterDelta >= 0f)
                return;
            SetGroundContactLatch(slave, latched: true);
        }
        else if (exitDelta >= 0f)
        {
            SetGroundContactLatch(slave, latched: false);
            return;
        }

        {
            var v = slave.RigidBody.Velocity;
            if (MathF.Abs(v.Y) > 0.01f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        slave.GroundContactLatchedTime += Math.Max(0f, (float)deltaTime.TotalSeconds);

        var penetration = floorSmoothed - boatBottom;
        if (penetration <= 0.0f)
            return;

        // Mass-box center (rigid body origin) above local waterline: stop vertical "pop" from penetration resolve.
        var waterAtCenter = slave.ParentWorld.Water.GetWaterSurface(
            new Vector3(rb.Position.X, rb.Position.Z, rb.Position.Y),
            out _);
        var centerAboveWaterline = slave.RigidBody.Position.Y > waterAtCenter + 0.001f;

        var penetrationEpsilon = ShorePhysicsDefaults.PenetrationEpsilon;
        var penetrationResponse = ShorePhysicsDefaults.PenetrationResponse;
        var maxUpStepPerTick = slave.GroundContactLatchedTime < 0.30f
            ? ShorePhysicsDefaults.MaxUpStepEarly
            : ShorePhysicsDefaults.MaxUpStepLate;
        if (penetration > penetrationEpsilon && !centerAboveWaterline)
        {
            var a = 1f - MathF.Exp(-penetrationResponse * dt);
            var step = MathF.Min(penetration * a, maxUpStepPerTick);
            slave.RigidBody.Position += new JVector(0, step, 0);

            var v = slave.RigidBody.Velocity;
            if (MathF.Abs(v.Y) > 0.01f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        else if (centerAboveWaterline)
        {
            var v = slave.RigidBody.Velocity;
            if (v.Y > 0f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        var collisionForce = physWorld.Gravity * -1f;
        slave.RigidBody.AddForce(collisionForce);

        var escapeThrottleSign = slave.GroundedByStern ? 1 : -1;
        var isEscapeThrottle = slave.ThrottleRequest != 0 && Math.Sign(slave.ThrottleRequest) == escapeThrottleSign;
        var deepContact = penetration > 0.25f;
        // Escape-direction throttle: no extra damping (pairs with full reverse mul on ground in ShipController).
        var collisionDamping = isEscapeThrottle ? 1.0f : (deepContact ? 0.88f : 0.95f);
        slave.RigidBody.Velocity *= collisionDamping;
        slave.RigidBody.AngularVelocity *= collisionDamping;

        ShipTuningDebug.OnShoreProbe(
            slave,
            probeX, probeY, floorSmoothed,
            cliffX, cliffY, cliffFloor,
            boatCenterX: slave.RigidBody.Position.X,
            boatCenterY: slave.RigidBody.Position.Z,
            boatBottomZ: boatBottom,
            waterSurfaceZ: waterAtProbe,
            penetrationMeters: penetration);
    }

    private static void SetGroundContactLatch(Slave slave, bool latched)
    {
        slave.GroundContactLatched = latched;
        slave.GroundContactLatchedTime = 0f;
        ShipTuningDebug.OnShoreLatchChanged(slave, latched: latched);
    }
}
