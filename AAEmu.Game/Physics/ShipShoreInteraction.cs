#nullable enable

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Ship interaction with terrain / shoreline: friction on dry ground, probes, latch, penetration resolve, and periodic beached hull damage.
/// </summary>
public sealed class ShipShoreInteraction
{
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
        var isOnLand = !isOnWater && submergedDepth <= 0;

        if (!isOnLand)
            return;

        var groundFriction = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.GroundFriction
            : 0.4f;
        var frictionForce = new JVector(-slave.RigidBody.Velocity.X * groundFriction, 0, -slave.RigidBody.Velocity.Z * groundFriction);
        slave.RigidBody.AddForce(frictionForce);

        var dryDamp = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.DryGroundCollisionDamping
            : 0.5f;
        slave.RigidBody.Velocity *= dryDamp;
        slave.RigidBody.AngularVelocity *= dryDamp;

        if (slave.RigidBody.Velocity.Length() < 0.01f)
        {
            slave.RigidBody.Velocity = JVector.Zero;
            slave.RigidBody.AngularVelocity = JVector.Zero;

            var rollAngle = PhysicsUtil.GetYawPitchRollFromJMatrix(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation)).Item2;
            var rollDeadZone = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
                ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.DryGroundRollDeadZoneRad
                : 0.1f;
            if (Math.Abs(rollAngle) < rollDeadZone)
            {
                var rollTorqueMul = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
                    ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.DryGroundRollTorqueMul
                    : 0.1f;
                var correctionTorque = new JVector(0, 0, -rollAngle * slave.RigidBody.Mass * rollTorqueMul);
                slave.RigidBody.AddForce(correctionTorque);
            }

            slave.ThrottleRequest = 0;
            slave.SteeringRequest = 0;
            slave.Throttle = 0;
            slave.Steering = 0;
            slave.ThrottleSmoothed = 0f;
            slave.SteeringSmoothed = 0f;
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

        var groundPitchMaxDeg = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.VisualGroundPitchMaxDeg
            : 8.0f;
        var groundPitchProbeDistance = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.VisualGroundPitchProbeDistance
            : 6.0f;
        var groundPitchResponse = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.VisualGroundPitchResponse
            : 2.0f;
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

            var pitchFloorSmoothResponse = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
                ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.VisualPitchFloorSmoothResponse
                : 8.0f;
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

        var boatBottom = slave.RigidBody.Position.Y;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation));
        var heading = rpy.Item1 + 1.57f;
        var dirX = MathF.Cos(heading);
        var dirZ = MathF.Sin(heading);

        var vX = slave.RigidBody.Velocity.X;
        var vZ = slave.RigidBody.Velocity.Z;
        var along = vX * dirX + vZ * dirZ;
        var movingBackward = along < -0.05f || (MathF.Abs(along) <= 0.05f && slave.ThrottleRequest < 0);

        var bowProbeMul = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.BowProbeMul
            : 1.5f;
        var sternProbeMul = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.SternProbeMul
            : 1.5f;
        var useSternProbe = slave.GroundContactLatched ? slave.GroundedByStern : movingBackward;
        var probeMul = useSternProbe ? sternProbeMul : bowProbeMul;
        // MassBoxSizeY is treated as hull length (forward/back).
        var probeDist = MathF.Max(1.0f, slave.ShipController.ShipModel.MassBoxSizeY * probeMul * slave.Scale);
        var probeSign = useSternProbe ? -1f : 1f;
        var probeX = slave.RigidBody.Position.X + dirX * probeDist * probeSign;
        var probeY = slave.RigidBody.Position.Z + dirZ * probeDist * probeSign;
        var contactFloor = slave.ParentWorld.GetHeight(probeX, probeY);

        var cliffProbeMul = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.CliffProbeMul
            : 1.45f;
        var cliffSlopeFracThreshold = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.CliffSlopeFracThreshold
            : 0.57f;
        // MassBoxSizeY is treated as hull length (forward/back).
        var cliffDist = MathF.Max(1.0f, slave.ShipController.ShipModel.MassBoxSizeY * cliffProbeMul * slave.Scale);
        var cliffX = slave.RigidBody.Position.X + dirX * cliffDist * probeSign;
        var cliffY = slave.RigidBody.Position.Z + dirZ * cliffDist * probeSign;
        var cliffFloor = slave.ParentWorld.GetHeight(cliffX, cliffY);
        var cliffAboveWaterMargin = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.CliffAboveWaterMargin
            : 0.20f;
        if (cliffFloor > slave.CachedWaterSurface + cliffAboveWaterMargin)
        {
            var dh = cliffFloor - slave.CachedFloorLevel;
            var slopeFrac = dh / MathF.Max(0.01f, cliffDist);
            if (slopeFrac > cliffSlopeFracThreshold)
            {
                var v = slave.RigidBody.Velocity;
                var vAlong = v.X * dirX + v.Z * dirZ;
                var pushingIntoBarrier = useSternProbe ? (vAlong < 0f) : (vAlong > 0f);
                if (pushingIntoBarrier)
                {
                    slave.Speed = 0f;
                    var newVX = v.X - vAlong * dirX;
                    var newVZ = v.Z - vAlong * dirZ;
                    slave.RigidBody.Velocity = new JVector(newVX * 0.85f, 0f, newVZ * 0.85f);
                    slave.RigidBody.AngularVelocity *= 0.85f;
                }

                var pushDirSign = useSternProbe ? 1f : -1f;
                var pushStep = MathF.Min(0.50f, MathF.Max(0.08f, MathF.Abs(vAlong) * dt * 1.10f));
                slave.RigidBody.Position += new JVector(dirX * pushStep * pushDirSign, 0f, dirZ * pushStep * pushDirSign);

                return;
            }
        }

        var shoreEnterHyst = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.ShoreEnterHyst
            : 0.35f;
        var shoreExitHyst = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.ShoreExitHyst
            : 0.10f;

        var floorSmoothResponse = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.FloorSmoothResponse
            : 10.0f;
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

        var preShoreBand = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.PreShoreBand
            : 0.25f;
        var enterDelta = (slave.CachedWaterSurface + shoreEnterHyst) - floorSmoothed;
        if (!slave.GroundContactLatched && enterDelta >= 0f && enterDelta <= preShoreBand)
        {
            var v = slave.RigidBody.Velocity;
            var t = 1f - (enterDelta / preShoreBand);
            var damp = 1f - 0.85f * t;
            slave.RigidBody.Velocity = new JVector(v.X, v.Y * damp, v.Z);
        }

        if (!slave.GroundContactLatched)
        {
            if (slave.CachedWaterSurface + shoreEnterHyst >= floorSmoothed)
                return;
            slave.GroundContactLatched = true;
            slave.GroundContactLatchedTime = 0f;
            AAEmu.Game.Physics.Debug.ShipTuningDebug.OnShoreLatchChanged(slave, latched: true);
        }
        else
        {
            if (slave.CachedWaterSurface + shoreExitHyst >= floorSmoothed)
            {
                slave.GroundContactLatched = false;
                slave.GroundContactLatchedTime = 0f;
                AAEmu.Game.Physics.Debug.ShipTuningDebug.OnShoreLatchChanged(slave, latched: false);
                return;
            }
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

        var penetrationEpsilon = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.PenetrationEpsilon
            : 0.02f;
        var penetrationResponse = AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled
            ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.PenetrationResponse
            : 4.5f;
        var maxUpStepPerTick = slave.GroundContactLatchedTime < 0.30f
            ? (AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.MaxUpStepEarly : 0.04f)
            : (AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreEnabled ? AAEmu.Game.Physics.Debug.ShipTuningDebug.ShoreTuning.MaxUpStepLate : 0.07f);
        if (penetration > penetrationEpsilon)
        {
            var a = 1f - MathF.Exp(-penetrationResponse * dt);
            var step = MathF.Min(penetration * a, maxUpStepPerTick);
            slave.RigidBody.Position += new JVector(0, step, 0);

            var v = slave.RigidBody.Velocity;
            if (MathF.Abs(v.Y) > 0.01f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        var collisionForce = physWorld.Gravity * -1f;
        slave.RigidBody.AddForce(collisionForce);

        var escapeThrottleSign = slave.GroundedByStern ? 1 : -1;
        var isEscapeThrottle = slave.ThrottleRequest != 0 && Math.Sign(slave.ThrottleRequest) == escapeThrottleSign;
        var deepContact = penetration > 0.25f;
        var collisionDamping = isEscapeThrottle ? 0.99f : (deepContact ? 0.88f : 0.95f);
        slave.RigidBody.Velocity *= collisionDamping;
        slave.RigidBody.AngularVelocity *= collisionDamping;

        AAEmu.Game.Physics.Debug.ShipTuningDebug.OnShoreProbe(
            slave,
            probeX, probeY, floorSmoothed,
            cliffX, cliffY, cliffFloor,
            boatCenterX: slave.RigidBody.Position.X,
            boatCenterY: slave.RigidBody.Position.Z,
            boatBottomZ: boatBottom,
            waterSurfaceZ: slave.CachedWaterSurface,
            penetrationMeters: penetration);
    }
}
