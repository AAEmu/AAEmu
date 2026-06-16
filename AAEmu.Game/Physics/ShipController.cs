#nullable enable

using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

public class ShipController(World world, ShipModelV1 shipModel)
{
    private readonly World _world = world ?? throw new ArgumentNullException(nameof(world));

    public RigidBody Hull { get; private set; } = null!;
    public ShipModelV1 ShipModel { get; init; } = shipModel ?? throw new ArgumentNullException(nameof(shipModel));

    /// <summary>Per-ship replication smoothing state; see <see cref="ReplicationSmoothing"/>.</summary>
    public ReplicationSmoothing Replication { get; } = new();

    /// <summary>Mass-box Z center/size used by <see cref="Build"/>; change implementations if hull height needs server-side tuning.</summary>
    public static class ShipMassBoxDefaults
    {
        public static float GetCenterZ(float baseCenterZ, float baseSizeZ) => baseCenterZ;

        public static float GetSizeZ(float baseSizeZ) => MathF.Max(0.01f, baseSizeZ);
    }

    /// <summary>Production ship motion constants (single source of truth).</summary>
    public static class ShipMotionDefaults
    {
        /// <summary>Hard cap (|m/s|) for the assisted “escape” speed target while grounded/beached.</summary>
        public const float GroundEscapeMaxSpeedAbs = 1.5f;
        /// <summary>Reverse speed cap on ground, as a percent of the current water max speed cap.</summary>
        public const float GroundReverseSpeedCapPercentOfWater = 100f;
        /// <summary>Below this water depth (m), apply the ground-related speed caps/logic (shoals).</summary>
        public const float ShallowWaterDepthForGroundSpeedCaps = 0.35f;
        /// <summary>Extra acceleration multiplier when throttle opposes current motion (e.g. reverse while moving forward).</summary>
        public const float OpposingThrottleAccelMul = 1f;
        /// <summary>Additional braking multiplier for opposing throttle only (does not affect forward accel).</summary>
        public const float OpposingThrottleBrakeTuneMul = 2.4f;
        /// <summary>Max coast decay (1/s) when in water — keep below <see cref="ShoalCoastDragCapPerSec"/> so «отпустил газ» glides longer than active reverse brake.</summary>
        public const float OpenWaterCoastDragCapPerSec = 0.085f;
        /// <summary>Shoal / beached: allow stronger coast decay than open water (was single cap for all water).</summary>
        public const float ShoalCoastDragCapPerSec = 0.22f;
        /// <summary>Steering builds yaw rate faster without raising the max yaw cap.</summary>
        public const float SteeringResponsivenessMul = 1.45f;
        /// <summary>When rudder fights current yaw rate, decay faster (counter-steer responsiveness).</summary>
        public const float CounterSteerResponsivenessMul = 1.35f;
        /// <summary>At (near) zero speed, keep this fraction of turning ability (so you can still rotate in place).</summary>
        public const float MinTurnFactorAtZeroSpeed = 0.5f;
        /// <summary>Speed at which turning reaches full effectiveness (used to scale turn factor).</summary>
        public const float TurnFullFactorAtSpeed = 2.5f;
        /// <summary>Max fraction of forward/back speed removed at full yaw rate (speed loss while turning).</summary>
        public const float TurnSpeedSlowdownFrac = 0.1f;
        /// <summary>Response rate for smoothing <see cref="Slave.TurnSpeedVelocityMul"/> toward the target (higher = snappier).</summary>
        public const float TurnSpeedVelocityMulResponse = 5.5f;
        /// <summary>Exponential damping (1/s) for velocity perpendicular to bow in XZ on open water — keeps brief sideways drift after hull impulses without a separate lateral state on <see cref="Slave"/>.</summary>
        public const float LateralVelocityDampPerSec = 5f;
        /// <summary>Minimum hull submergence (m) before upright stabilization starts applying torque.</summary>
        public const float UprightStabilizeMinSubmergedMeters = 0.04f;
        /// <summary>Max angular correction speed (rad/s) toward upright per tick (prevents snapping).</summary>
        public const float UprightStabilizeMaxRadPerSec = 2.25f;
        /// <summary>Dead-zone angle (rad) under which upright stabilization does nothing.</summary>
        public const float UprightStabilizeAngleDeadZoneRad = 0.004f;
        /// <summary>Half-angle (deg) of the “with wind / against wind” cone used by the wind speed model.</summary>
        public const float WindConeHalfAngleDeg = 15f;
        /// <summary>Max speed multiplier when moving “with wind” (inside the cone).</summary>
        public const float WindWithMaxMul = 1.15f;
        /// <summary>Max speed multiplier when moving “against wind” (inside the opposite cone).</summary>
        public const float WindAgainstMaxMul = 0.85f;
        /// <summary>Vertical velocity damping (1/s) applied while submerged to prevent buoyancy oscillations.</summary>
        public const float WaterVerticalVelocityDampPerSec = 6.0f;
    }

    /// <summary>
    /// Fixed max yaw rate (degrees/s) by <see cref="SlaveKind"/>.
    /// NOTE: We intentionally do NOT use ship_models.steer_vel because values in DB are not in valid units.
    /// </summary>
    /// <remarks>
    /// When adding a <see cref="SlaveKind"/>, review this table and any other per-kind ship tuning.
    /// Visual turn bank in <see cref="PhysicsManager"/> uses <c>ship_models</c> mass box + mass instead.
    /// </remarks>
    private static float GetSteerMaxDegFixed(SlaveKind kind) => kind switch
    {
        SlaveKind.Boat => 4.35f,
        SlaveKind.SmallSailingShip => 2.6f,
        SlaveKind.BigSailingShip => 2.35f,
        SlaveKind.Fishboat => 3.35f,
        SlaveKind.Speedboat => 5.85f,
        SlaveKind.MerchantShip => 2.85f,
        _ => 3.1f
    };

    /// <summary>Horizontal wind when no river flow and clock wind is off/unavailable (game X,Y).</summary>
    private const float DefaultWindDirX = 0f;
    private const float DefaultWindDirY = 1f;

    /// <summary>Open-sea wind rotates with <see cref="TimeManager"/>; river <see cref="Slave.CachedWaterFlow"/> still wins.</summary>
    private const bool WindFollowsTimeOfDay = true;

    /// <summary>Shift wind phase in game hours (e.g. if «полночь» в клиенте не совпадает с 0:00).</summary>
    private const float WindTimePhaseOffsetHours = 0f;

    /// <summary>How wind affects max speed: none (rowing/motor), square (downwind best), lateen (beam reach best).</summary>
    private enum ShipWindProfile
    {
        None,
        SquareRig,
        LateenRig
    }

    /// <summary>Per-<see cref="SlaveTemplate.Id"/> wind overrides can branch here before the <see cref="SlaveKind"/> fallback.</summary>
    private static ShipWindProfile ResolveShipWindProfile(Slave slave)
    {
        return slave.Template.SlaveKind switch
        {
            SlaveKind.Boat or SlaveKind.Fishboat => ShipWindProfile.None,
            SlaveKind.BigSailingShip or SlaveKind.SmallSailingShip => ShipWindProfile.SquareRig,
            SlaveKind.MerchantShip or SlaveKind.Speedboat => ShipWindProfile.LateenRig,
            SlaveKind.Leviathan => ShipWindProfile.None,
            _ => ShipWindProfile.None
        };
    }

    /// <summary>
    /// Full cycle over 24h: h=0 → (0,+Y), h=12 → (0,-Y), h=6/18 → боковой ветер (плавно крутится каждый час).
    /// </summary>
    private static (float wx, float wy) GetOpenSeaWindFromGameClockRealistic()
    {
        if (!WindFollowsTimeOfDay || TimeManager.Instance is null)
            return NormalizeWind(DefaultWindDirX, DefaultWindDirY);

        var sec = TimeManager.Instance.Get() % 86400f;
        if (sec < 0f)
            sec += 86400f;
        var hours = sec / 3600f + WindTimePhaseOffsetHours;
        hours = (hours % 24f + 24f) % 24f;

        // π·h/12: два противоположных направления в сутках по оси Y (полночь/полдень), бок в 6 и 18:00
        var phase = MathF.PI * hours / 12f;
        return (MathF.Sin(phase), MathF.Cos(phase));
    }

    /// <summary>
    /// Retail-like wind: constant N↔S axis (does not change with time of day).
    /// </summary>
    private static (float wx, float wy) GetOpenSeaWindFromGameClockOfficial()
    {
        // Matches retail behavior described: boosts along the N↔S axis regardless of direction.
        return (0f, 1f);
    }

    private static (float wx, float wy) GetOpenSeaWind(Slave slave)
    {
        var model = AppConfiguration.Instance.World?.WindModel ?? WorldConfig.WindModelType.Official;
        return model == WorldConfig.WindModelType.Official
            ? GetOpenSeaWindFromGameClockOfficial()
            : GetOpenSeaWindFromGameClockRealistic();
    }

    private static (float wx, float wy) NormalizeWind(float x, float y)
    {
        var lenSq = x * x + y * y;
        if (lenSq < 1e-8f)
            return (0f, 1f);
        var inv = 1f / MathF.Sqrt(lenSq);
        return (x * inv, y * inv);
    }

    /// <summary>Normalized wind direction on water plane (physics XZ = game horizontal X,Y).</summary>
    private static (float wx, float wy) GetWindDirNormalized(Slave slave)
    {
        var f = slave.CachedWaterFlow;
        var lenSq = f.X * f.X + f.Y * f.Y;
        if (lenSq > 1e-8f)
        {
            var inv = 1f / MathF.Sqrt(lenSq);
            return (f.X * inv, f.Y * inv);
        }

        return GetOpenSeaWind(slave);
    }

    /// <summary>Square rig: best speed down/up wind (same as original).</summary>
    private static float GetWindSpeedMulSquareRig(float dotMove)
    {
        var cosCone = MathF.Cos(ShipMotionDefaults.WindConeHalfAngleDeg * MathF.PI / 180f);
        if (dotMove >= cosCone)
            return ShipMotionDefaults.WindWithMaxMul;
        if (dotMove <= -cosCone)
            return ShipMotionDefaults.WindAgainstMaxMul;
        return 1f + (ShipMotionDefaults.WindWithMaxMul - 1f) * (dotMove / cosCone);
    }

    /// <summary>Lateen / fore-and-aft: best speed on a beam reach (perpendicular to wind).</summary>
    private static float GetWindSpeedMulLateenRig(float dotMove)
    {
        var cosCone = MathF.Cos(ShipMotionDefaults.WindConeHalfAngleDeg * MathF.PI / 180f);
        var sinCone = MathF.Sin(ShipMotionDefaults.WindConeHalfAngleDeg * MathF.PI / 180f);
        var p = 1f - MathF.Abs(dotMove);
        if (p >= cosCone)
            return ShipMotionDefaults.WindWithMaxMul;
        if (p <= sinCone)
            return ShipMotionDefaults.WindAgainstMaxMul;
        return ShipMotionDefaults.WindAgainstMaxMul + (p - sinCone) / (cosCone - sinCone) * (ShipMotionDefaults.WindWithMaxMul - ShipMotionDefaults.WindAgainstMaxMul);
    }

    /// <summary>Multiplier for max speed from wind; depends on <see cref="ResolveShipWindProfile"/>.</summary>
    private static float GetWindSpeedMul(Slave slave, float bowRad)
    {
        var model = AppConfiguration.Instance.World?.WindModel ?? WorldConfig.WindModelType.Official;

        var profile = ResolveShipWindProfile(slave);
        if (profile == ShipWindProfile.None)
            return 1f;

        var (wx, wy) = GetWindDirNormalized(slave);
        var fwdX = MathF.Cos(bowRad);
        var fwdZ = MathF.Sin(bowRad);
        var dotBow = fwdX * wx + fwdZ * wy;
        var dotMove = Math.Abs(slave.Speed) < 0.01f ? dotBow : MathF.Sign(slave.Speed) * dotBow;

        if (model == WorldConfig.WindModelType.Official)
        {
            if (slave.Template.SlaveKind is not (SlaveKind.SmallSailingShip or SlaveKind.BigSailingShip or SlaveKind.Fishboat))
                return 1f;

            // Retail-like: +15% within ±15° of the N↔S axis, both directions (no "against wind" penalty).
            var cosCone = MathF.Cos(ShipMotionDefaults.WindConeHalfAngleDeg * MathF.PI / 180f);
            var dotAbs = MathF.Abs(dotBow);
            // Hard cutoff: bonus disappears immediately beyond the angle threshold.
            return dotAbs >= cosCone ? ShipMotionDefaults.WindWithMaxMul : 1f;
        }

        return profile switch
        {
            ShipWindProfile.SquareRig => GetWindSpeedMulSquareRig(dotMove),
            ShipWindProfile.LateenRig => GetWindSpeedMulLateenRig(dotMove),
            _ => 1f
        };
    }

    ~ShipController()
    {
        try
        {
            Hull?.World.Remove(Hull);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to remove hull RigidBody from Physics world: {e}");
        }
    }

    /// <summary>
    /// Создает корпус корабля.
    /// </summary>
    public void Build(JVector initialPosition, JQuaternion initialOrientation)
    {
        // New object
        Hull = _world.CreateRigidBody();
        // Set starting position and rotation
        Hull.Position = initialPosition;
        Hull.Orientation = initialOrientation;
        // Ship shape
        // Interpretation:
        // - MassBoxSizeY = length (forward/back)
        // - MassBoxSizeX = beam (right/left)
        // Axis mapping to physics box local axes:
        // - local +X uses MassBoxSizeX (beam)
        // - local +Y uses MassBoxSizeZ (height)
        // - local +Z uses MassBoxSizeY (length)
        var sizeZ = ShipMassBoxDefaults.GetSizeZ(ShipModel.MassBoxSizeZ);
        var centerZ = ShipMassBoxDefaults.GetCenterZ(ShipModel.MassCenterZ, ShipModel.MassBoxSizeZ);

        var shipBoxShape = new BoxShape(ShipModel.MassBoxSizeX, sizeZ, ShipModel.MassBoxSizeY);
        // Center offset
        var shipCenterPoint = new TransformedShape(shipBoxShape, new JVector(ShipModel.MassCenterX, centerZ, ShipModel.MassCenterY));
        // Add shape
        Hull.AddShape(shipCenterPoint);
        // Set Mass
        Hull.SetMassInertia(ShipModel.Mass);
        Hull.DeactivationTime = TimeSpan.MaxValue;
        Hull.MotionType = MotionType.Dynamic;
        Hull.SetActivationState(true);
    }

    /// <summary>
    /// Applies forces to the Ship according to previous steering calculations
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="deltaTime"></param>
    public void ApplyForceAndTorque(Slave slave, TimeSpan deltaTime)
    {
        if (slave?.RigidBody is null)
            return;

        var rigidBody = slave.RigidBody;

        var shipModel = ModelManager.Instance.GetShipModel(slave.Template.ModelId);
        if (shipModel is null)
            return;

        var dtSec = (float)deltaTime.TotalSeconds;

        // If not in water (grounded), keep limited control to let ships get unstuck,
        // while still strongly discouraging driving deeper inland.
        //
        // Use the latched ground contact from PhysicsManager to avoid shoreline jitter causing
        // abrupt control/velocity changes ("jerks") when transitioning water<->land.
        var isGrounded = (slave.CachedFloorLevel > slave.CachedWaterSurface) || slave.GroundContactLatched;
        var shallowDepthForCaps = ShipMotionDefaults.ShallowWaterDepthForGroundSpeedCaps;
        shallowDepthForCaps = MathF.Max(0f, shallowDepthForCaps);
        var waterDepth = slave.CachedWaterSurface - slave.CachedFloorLevel; // meters
        var isShallowForCaps = waterDepth >= 0f && waterDepth <= shallowDepthForCaps;
        var isGroundedForSpeedCaps = isGrounded || isShallowForCaps;

        // Still compute movement direction for stern/bow logic.
        var rpy0 = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));
        var heading0 = rpy0.Item1 + MathUtil.HalfPi;
        var dirX0 = MathF.Cos(heading0);
        var dirZ0 = MathF.Sin(heading0);
        var along0 = rigidBody.Velocity.X * dirX0 + rigidBody.Velocity.Z * dirZ0;
        var movingBackward0 = along0 < -0.05f || (MathF.Abs(along0) <= 0.05f && slave.ThrottleRequest < 0);
        var isEscapeInputOnGround = false;
        var escapeThrottleSignOnGround = 0;

        if (isGroundedForSpeedCaps)
        {
            // Latch grounding side only when entering ground state.
            // Recomputing it every tick from current speed causes escape direction to flip mid-maneuver.
            if (!slave.GroundedLastTick)
                slave.GroundedByStern = movingBackward0;
            var groundedByStern = slave.GroundedByStern;

            var escapeThrottleSign = groundedByStern ? 1 : -1; // forward to escape stern-grounding, reverse to escape bow-grounding
            escapeThrottleSignOnGround = escapeThrottleSign;
            isEscapeInputOnGround = slave.ThrottleRequest != 0 && Math.Sign(slave.ThrottleRequest) == escapeThrottleSign;
            var isStuckOnGround = MathF.Abs(slave.Speed) < 0.08f && slave.ThrottleRequest != 0;

            if (isStuckOnGround)
                slave.GroundStuckTime += dtSec;
            else
                slave.GroundStuckTime = 0f;

            // Reverse cap on shoal: GroundReverseSpeedCapPercentOfWater (see speed clamp below).

            // If player keeps trying the "escape direction" but ship stays almost still, gradually assist.
            var assistTarget = isEscapeInputOnGround && slave.GroundStuckTime > 1.2f ? 1f : 0f;
            var assistA = 1f - MathF.Exp(-4.0f * MathF.Max(0f, dtSec));
            slave.GroundEscapeAssist += (assistTarget - slave.GroundEscapeAssist) * assistA;

            // Base asymmetric throttle on ground differs by contact side. Full reverse mul pairs with
            // no collision damping for escape-direction throttle in ShipShoreInteraction.
            var groundThrottleForwardMul = groundedByStern ? 0.78f : 0.10f;
            var groundThrottleReverseMul = 1.0f;

            // Boost only the "escape" direction while stuck; suppress direction that digs deeper inland.
            if (isEscapeInputOnGround)
            {
                var escapeBoost = 1f + 0.35f * slave.GroundEscapeAssist;
                if (escapeThrottleSign > 0)
                    groundThrottleForwardMul = MathF.Min(1f, groundThrottleForwardMul * escapeBoost);
                else
                    groundThrottleReverseMul = MathF.Min(1f, groundThrottleReverseMul * escapeBoost);
            }
            else if (slave.ThrottleRequest != 0 && !isEscapeInputOnGround)
            {
                if (slave.ThrottleRequest > 0)
                    groundThrottleForwardMul *= 0.55f;
                else
                    groundThrottleReverseMul *= 0.20f;
            }

            var groundedThrottleInputMul = slave.ThrottleRequest >= 0 ? groundThrottleForwardMul : groundThrottleReverseMul;
            slave.Throttle = (sbyte)Math.Clamp((int)Math.Round(slave.ThrottleRequest * groundedThrottleInputMul), -128, 127);
            // Avoid sbyte quantization dead-zone: keep tiny non-zero throttle while player gives
            // valid escape input, otherwise speed can oscillate 0.1 -> 0.0 repeatedly.
            if (isEscapeInputOnGround && slave.Throttle == 0 && Math.Abs(slave.ThrottleRequest) >= 8)
                slave.Throttle = (sbyte)(escapeThrottleSign * 8);
            slave.ThrottleSmoothed = slave.Throttle;

            // Keep responsive steering on ground; add a bit extra during escape assist.
            var groundSteerInputMul = groundedByStern ? 0.9f : 0.8f;
            groundSteerInputMul = MathF.Min(1f, groundSteerInputMul + 0.15f * slave.GroundEscapeAssist);
            slave.Steering = (sbyte)Math.Clamp((int)Math.Round(slave.SteeringRequest * groundSteerInputMul), -128, 127);
            slave.SteeringSmoothed = slave.Steering;

            slave.TurnSpeedVelocityMul = 1f;
        }
        else
        {
            slave.GroundedByStern = false;
            slave.GroundStuckTime = 0f;
            slave.GroundEscapeAssist = 0f;
        }
        // Must match the same "grounded for controls/caps" predicate; otherwise shallow-water-only frames
        // never latch GroundedLastTick and GroundedByStern gets recomputed every tick (breaks escape direction).
        slave.GroundedLastTick = isGroundedForSpeedCaps;

        // Minimum crawl speed when starting in that direction only. Do not apply while still moving
        // the other way — otherwise forward+reverse snaps Speed to ±1 and the ship stops instantly.
        // Do not force crawl while grounded, otherwise ships can "slide-drive" on land at low speed.
        // Do not force crawl while a harpoon line is taut: ±1 Speed keeps the hull creeping
        // against the anchor (client rope stretches / "breaks") while tow physics cannot fully cancel it.
        if (!isGrounded && !HasTautHarpoonEngaged(slave))
        {
            if (slave is { Throttle: > 0, Speed: < 1f } && slave.Speed >= 0f)
                slave.Speed = 1f;

            if (slave is { Throttle: < 0, Speed: > -1f } && slave.Speed <= 0f)
                slave.Speed = -1f;
        }

        var throttleNorm = slave.Throttle * 0.00787401575f; // sbyte -> float
        var steeringNorm = slave.Steering * 0.00787401575f; // sbyte -> float

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));
        var slaveRotRad = rpy.Item1 + MathUtil.HalfPi; // bow heading in physics XZ; reused for wind + velocity

        // Clamp speed between min and max Velocity (wind: ±15% of max speed when within ±15° of with/against wind)
        var windMul = GetWindSpeedMul(slave, slaveRotRad);
        var maxForward = shipModel.Velocity * slave.MoveSpeedMul / 2f * windMul;
        var waterMaxReverseAbs = shipModel.ReverseVelocity * slave.MoveSpeedMul / 2f * windMul;
        var maxBackward = -waterMaxReverseAbs;

        // Shoal: limit max reverse vs the same water-based cap (percent of |maxBackward| on water).
        if (isGroundedForSpeedCaps)
        {
            var revPct = ShipMotionDefaults.GroundReverseSpeedCapPercentOfWater;
            revPct = Math.Clamp(revPct, 0f, 100f);
            maxBackward = -waterMaxReverseAbs * (revPct / 100f);
        }

        // Ground escape cap override:
        // When grounded and the player holds the correct "escape" throttle direction, allow a higher cap than
        // shipModel.Velocity-driven maxForward/maxBackward. Otherwise some ship models get stuck around ~0.6 m/s
        // and cannot reliably climb off the shoal.
        // Fallback: if the ship is grounded and effectively "stuck" (can't get moving) but the player keeps applying throttle,
        // still allow the higher escape cap in the input direction. This prevents a wrong stern/bow latch from trapping ships
        // at low model-based caps (~0.6 m/s) on some hulls.
        var isFallbackEscapeOnGround = isGroundedForSpeedCaps && !isEscapeInputOnGround && slave.ThrottleRequest != 0 && slave.GroundStuckTime > 0.35f;

        if (isGroundedForSpeedCaps && (isEscapeInputOnGround || isFallbackEscapeOnGround))
        {
            var groundEscapeMaxSpeedAbs = ShipMotionDefaults.GroundEscapeMaxSpeedAbs;
            var escapeSign = isEscapeInputOnGround ? escapeThrottleSignOnGround : Math.Sign(slave.ThrottleRequest);
            if (escapeSign > 0)
                maxForward = MathF.Max(maxForward, groundEscapeMaxSpeedAbs);
            else if (escapeSign < 0)
                maxBackward = MathF.Min(maxBackward, -groundEscapeMaxSpeedAbs);
        }

        // Use data reverse_accel for braking; scale up when fighting current motion (feels less sluggish than forward-only Accel).
        var linearAccel = shipModel.Accel;
        var isOpposingThrottle = throttleNorm != 0f && slave.Speed != 0f && Math.Sign(slave.Speed) != Math.Sign(throttleNorm);
        if (isOpposingThrottle)
        {
            var reverseCap = shipModel.ReverseAccel > 0f ? shipModel.ReverseAccel : shipModel.Accel;
            linearAccel = Math.Max(shipModel.Accel, reverseCap) * ShipMotionDefaults.OpposingThrottleAccelMul * ShipMotionDefaults.OpposingThrottleBrakeTuneMul;
        }

        // Non-linear approach to max speed: accelerate strongly at low speed, taper off near the cap.
        // Applies only when accelerating in the current movement direction (not when braking/opposing throttle).
        if (throttleNorm != 0f && !isOpposingThrottle)
        {
            var capAbs = throttleNorm > 0f ? maxForward : -maxBackward;
            if (capAbs > 1e-4f)
            {
                var n = Math.Clamp(MathF.Abs(slave.Speed) / capAbs, 0f, 1f);
                const float approachPow = 2.0f; // higher = stronger slowdown near cap
                var approachMul = 1f - MathF.Pow(n, approachPow);
                approachMul = Math.Clamp(approachMul, 0.10f, 1f);
                linearAccel *= approachMul;
            }
        }

        // Calculate speed
        slave.Speed += throttleNorm * (linearAccel * dtSec) / 2f;

        // Anti-stall nudge: when grounded and escape input is held, avoid jitter around zero.
        if (isGroundedForSpeedCaps && isEscapeInputOnGround && slave.GroundStuckTime > 0.35f)
        {
            var targetEscapeSpeed = ShipMotionDefaults.GroundEscapeMaxSpeedAbs;
            var escapeA = 1f - MathF.Exp(-(6.0f + 2.0f * slave.GroundEscapeAssist) * MathF.Max(0f, dtSec));
            var target = escapeThrottleSignOnGround * targetEscapeSpeed;
            slave.Speed += (target - slave.Speed) * escapeA;
        }

        // When wind bonus disappears (especially Official "hard cutoff"), max speed can drop instantly.
        // Hard-clamping causes a visible speed snap. Instead, smoothly converge back to the new cap unless
        // the player is actively accelerating in that direction (then clamp immediately).
        const float overspeedResponse = 3.5f; // higher = faster return to cap
        var overspeedA = 1f - MathF.Exp(-overspeedResponse * MathF.Max(0.0f, dtSec));
        if (slave.Speed > maxForward)
        {
            // Always smooth down to the new cap to avoid a snap when the wind bonus disappears,
            // even if the player keeps holding throttle.
            slave.Speed = slave.Speed + (maxForward - slave.Speed) * overspeedA;
        }
        else if (slave.Speed < maxBackward)
        {
            slave.Speed = slave.Speed + (maxBackward - slave.Speed) * overspeedA;
        }

        // Track last stable movement direction so reverse steering doesn't flip when speed reaches (near) zero.
        const float MoveDirEpsilon = 0.10f;
        if (slave.Speed > MoveDirEpsilon)
            slave.LastMoveDirSign = 1;
        else if (slave.Speed < -MoveDirEpsilon)
            slave.LastMoveDirSign = -1;

        // Turning factor scales with ship speed, but never reaches zero (so the ship can still turn in place).
        var speedAbs = MathF.Abs(slave.Speed);
        var speed01 = Math.Clamp(speedAbs / ShipMotionDefaults.TurnFullFactorAtSpeed, 0f, 1f);
        var turnFactor = ShipMotionDefaults.MinTurnFactorAtZeroSpeed + (1f - ShipMotionDefaults.MinTurnFactorAtZeroSpeed) * speed01;

        // Reverse steering should be handled at input→rotSpeed stage, not at the final angular velocity assignment.
        // Otherwise when speed crosses zero (e.g. releasing reverse), the last-step inversion toggles and the ship appears
        // to start turning the opposite way even though the rudder input didn't change.
        const float ReverseSteerEpsilon = 0.05f;
        var isMovingBackward = slave.Speed < -ReverseSteerEpsilon || (MathF.Abs(slave.Speed) <= ReverseSteerEpsilon && slave.LastMoveDirSign < 0);
        var effectiveSteeringNorm = isMovingBackward ? -steeringNorm : steeringNorm;

        // Per-kind turn rate with TurnSpeed multiplier from the common bonus system
        // (same idea as MoveSpeedMul: base value + buff modifiers).
        var baseKindSteerDeg = GetSteerMaxDegFixed(slave.Template.SlaveKind);
        var turnSpeedBonusMul = MathF.Max(0.05f, slave.TurnSpeed);
        var kindSteerDeg = baseKindSteerDeg * turnSpeedBonusMul;
        var steerMaxDegNormal = Math.Max(0.05f, kindSteerDeg);
        var steerMaxDegHard = Math.Max(0.05f, kindSteerDeg * 2f);
        var steerMaxNormal = (steerMaxDegNormal * turnFactor).DegToRad();

        // Calculate rotation speed
        // TurnSpeed is now a multiplier (1.0, 1.3, ...). Keep steering response at the previous "stat-scale"
        // baseline so counter-steer and turn build-up remain responsive.
        var turnSpeed = 100f * turnSpeedBonusMul * (float)deltaTime.TotalSeconds * MathF.PI;
        var rotDelta = effectiveSteeringNorm * (turnSpeed / 100f) * (shipModel.TurnAccel / 360f) * ShipMotionDefaults.SteeringResponsivenessMul * turnFactor;
        if (slave.RotSpeed != 0f && effectiveSteeringNorm != 0f && Math.Sign(slave.RotSpeed) != Math.Sign(effectiveSteeringNorm))
            rotDelta *= ShipMotionDefaults.CounterSteerResponsivenessMul;

        // Non-linear approach: turning accelerates normally at low yaw rates, but slows down as we approach the cap.
        // This prevents the turn rate from building linearly all the way up to the limit.
        if (effectiveSteeringNorm != 0f && steerMaxNormal > 1e-6f)
        {
            var sameDir = slave.RotSpeed == 0f || Math.Sign(slave.RotSpeed) == Math.Sign(rotDelta);
            if (sameDir)
            {
                var n = Math.Clamp(MathF.Abs(slave.RotSpeed) / steerMaxNormal, 0f, 1f);
                const float approachPow = 2.0f; // higher = stronger slowdown near cap
                var approachMul = 1f - MathF.Pow(n, approachPow);
                // keep some authority even near cap, otherwise the last bit can feel "stuck"
                approachMul = Math.Clamp(approachMul, 0.10f, 1f);
                rotDelta *= approachMul;
            }
        }

        slave.RotSpeed += rotDelta;

        // Max turn rate (fixed by ship kind). Hard cap is 2x of normal.
        var steerMaxHard = (steerMaxDegHard * turnFactor).DegToRad();
        slave.RotSpeed = Math.Clamp(slave.RotSpeed, -steerMaxHard, steerMaxHard);

        // While steering, keep yaw rate within the normal per-kind limit.
        // When cap decreases (e.g. sails raised), converge smoothly instead of a one-frame snap.
        // Hard cap above still protects against spikes.
        if (slave.Steering != 0)
        {
            var steerMaxNormalRad = (steerMaxDegNormal * turnFactor).DegToRad();
            var rotAbs = MathF.Abs(slave.RotSpeed);
            if (rotAbs > steerMaxNormalRad)
            {
                const float turnCapResponse = 8.0f;
                var capA = 1f - MathF.Exp(-turnCapResponse * MathF.Max(0f, dtSec));
                var target = MathF.Sign(slave.RotSpeed) * steerMaxNormalRad;
                slave.RotSpeed += (target - slave.RotSpeed) * capA;
            }
        }

        var steerMax = (steerMaxDegNormal * turnFactor).DegToRad();

        // Up to configured turn-speed slowdown at full yaw rate; smooth return on straight course (forward and reverse).
        var steerMaxSafe = Math.Max(steerMax, 1e-5f);
        var turnRateNorm = Math.Clamp(MathF.Abs(slave.RotSpeed) / steerMaxSafe, 0f, 1f);
        var targetTurnVelMul = 1f - ShipMotionDefaults.TurnSpeedSlowdownFrac * turnRateNorm;
        var turnMulA = 1f - MathF.Exp(-ShipMotionDefaults.TurnSpeedVelocityMulResponse * MathF.Max(0f, dtSec));
        slave.TurnSpeedVelocityMul += (targetTurnVelMul - slave.TurnSpeedVelocityMul) * turnMulA;

        // Slow down turning if no steering active
        const float AngularDamping = 0.975f; // Damping of angular velocity
        if (slave.Steering == 0)
        {
            slave.RotSpeed *= AngularDamping;
        }

        // If not in water, seriously slow down the velocity
        const float FloorCollisionSpeedMultiplier = 0.96f;
        if (isGroundedForSpeedCaps)
        {
            // While applying escape throttle, do not damp speed/velocity — otherwise it creates an artificial
            // steady-state ceiling well below GroundEscapeMaxSpeedAbs (e.g. ~0.7).
            var groundDamping = isEscapeInputOnGround ? 1.0f : FloorCollisionSpeedMultiplier;
            slave.Speed *= groundDamping;
            slave.RigidBody.Velocity *= groundDamping;
        }

        // Holding throttle into the beach (e.g. reverse while stern-grounded) can settle at a tiny non-zero
        // speed (~0.1) from accel/damping balance and MoveDirEpsilon; snap to full stop when nearly still.
        if (isGroundedForSpeedCaps && slave.ThrottleRequest != 0 && !isEscapeInputOnGround && MathF.Abs(slave.Speed) < 0.12f)
            slave.Speed = 0f;

        // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
        if (slave.Throttle == 0)
        {
            // Smooth drag towards zero without a hard cutoff (prevents the "snap stop" when speed crosses a threshold).
            var dt = (float)deltaTime.TotalSeconds;
            var drag = MathF.Max(0f, shipModel.WaterResistance);

            // Make the last bit of coasting smoother: reduce effective drag as we approach standstill.
            // This avoids the feeling of a quick "final bite" from ~1 to 0.
            var speedAbsNow = MathF.Abs(slave.Speed);
            var lowSpeed01 = Math.Clamp(speedAbsNow / 2.0f, 0f, 1f); // 0..2 speed range
            var lowSpeedCurve = lowSpeed01 * lowSpeed01; // keep drag lower for longer in 0..2
            // Also keep coasting gentle even at high speed (otherwise letting go of throttle "slams the brakes").
            const float coastDragMinMul = 0.15f; // near standstill
            var coastDragMaxMul = isGrounded ? 0.45f : 0.28f;
            var dragMul = coastDragMinMul + (coastDragMaxMul - coastDragMinMul) * lowSpeedCurve;
            var effectiveDrag = drag * dragMul;

            // Ground friction/braking: when beached we need to avoid long inland drift.
            if (isGrounded)
                effectiveDrag *= 1.35f;

            // Hard cap so "let go of throttle" never brakes harder than intended,
            // even if ship_models.water_resistance is high for some templates.
            var maxCoastDragPerSecond = isGrounded
                ? 0.26f
                : (isGroundedForSpeedCaps ? ShipMotionDefaults.ShoalCoastDragCapPerSec : ShipMotionDefaults.OpenWaterCoastDragCapPerSec);
            effectiveDrag = MathF.Min(effectiveDrag, maxCoastDragPerSecond);

            var decay = MathF.Exp(-effectiveDrag * dt);
            slave.Speed *= decay;

            var stopEpsilon = isGrounded ? 0.04f : 0.002f;
            if (MathF.Abs(slave.Speed) < stopEpsilon)
                slave.Speed = 0f;
        }

        var forceThrottle = slave.Speed * slave.MoveSpeedMul / 4f * slave.TurnSpeedVelocityMul; // Not sure if correct, but it feels correct

        // Longitudinal speed from Slave.Speed; preserve damped lateral XZ (perpendicular to bow) so hull impulses can briefly show sideways motion.
        // On land/shore keep the old overwrite-only model (avoids odd sliding while beached).
        var fx = MathF.Cos(slaveRotRad);
        var fz = MathF.Sin(slaveRotRad);
        if (!isGrounded)
        {
            var v = rigidBody.Velocity;
            // Do not zero vertical velocity (lets ships fall off waterfalls),
            // but damp it when submerged to prevent buoyancy oscillations / runaway upward launch.
            var submergedNow = MathF.Max(0f, slave.CachedWaterSurface - rigidBody.Position.Y);
            if (submergedNow > 0.01f)
            {
                v.Y *= MathF.Exp(-ShipMotionDefaults.WaterVerticalVelocityDampPerSec * MathF.Max(0f, dtSec));
                v.Y = Math.Clamp(v.Y, -18f, 6f);
            }
            var alongDot = v.X * fx + v.Z * fz;
            var perpX = v.X - alongDot * fx;
            var perpZ = v.Z - alongDot * fz;
            var lateralDamp = MathF.Exp(-ShipMotionDefaults.LateralVelocityDampPerSec * MathF.Max(0f, dtSec));
            rigidBody.Velocity = new JVector(
                forceThrottle * fx + perpX * lateralDamp,
                v.Y,
                forceThrottle * fz + perpZ * lateralDamp);
        }
        else
        {
            var v = rigidBody.Velocity;
            rigidBody.Velocity = new JVector(forceThrottle * fx, v.Y, forceThrottle * fz);
        }

        var speedToAlongVel = slave.MoveSpeedMul / 4f * MathF.Max(0.001f, slave.TurnSpeedVelocityMul);
        ShipHarpoonTowPhysics.ApplyTerrainHookTow(slave, rigidBody, dtSec, fx, fz, speedToAlongVel, maxForward, maxBackward);

        if (!isGrounded)
        {
            var submerged = MathF.Max(0f, slave.CachedWaterSurface - rigidBody.Position.Y);
            if (submerged >= ShipMotionDefaults.UprightStabilizeMinSubmergedMeters)
                ApplyWaterUprightStabilization(rigidBody, dtSec);
        }

        rigidBody.AngularVelocity = new JVector(0, slave.RotSpeed * -1f, 0);

        //Logger.Debug($"Slave: {slave.Name}, Throttle: {throttleFloatVal:F1} ({slave.ThrottleRequest}), Steering {steeringFloatVal:F1} ({slave.SteeringRequest}), speed: {slave.Speed}, rotSpeed: {slave.RotSpeed}");
    }

    /// <summary>
    /// Same taut-vs-slack test as <see cref="ShipHarpoonTowPhysics.ApplyTerrainHookTow"/> (paid rope vs cannon–hook
    /// distance + margin). Only <see cref="Slave.AttachedSlaves"/> of the hull (direct children), not the full mount
    /// subtree: this gates the ±1 crawl-speed floor on the hull; expanding to descendants matched retail layout poorly
    /// in practice (false taut). Tow impulses still walk descendants in <see cref="ShipHarpoonTowPhysics.ApplyTerrainHookTow"/>.
    /// Counts any engaged harpoon taut by this test — not only terrain hooks — so crawl does not fight a taut line
    /// when tow force is inactive (e.g. hook in water).
    /// </summary>
    private static bool HasTautHarpoonEngaged(Slave hull)
    {
        if (hull.AttachedSlaves is not { Count: > 0 })
            return false;

        foreach (var child in hull.AttachedSlaves)
        {
            var st = child.HarpoonRope;
            if (!st.IsEngaged)
                continue;

            var hookWorld = ShipHarpoonRopeController.GetHookWorldPosition(child);
            var dist = Vector3.Distance(child.Transform.World.Position, hookWorld);
            var paid = st.RopeLength + ShipHarpoonTowPhysics.ServerRopePaidLengthAdditiveMeters;
            if (paid <= dist + ShipHarpoonTowPhysics.SlackMarginMeters)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Nudges <paramref name="body"/> so local +Y matches world up (shortest path), limited per frame.
    /// Collision/physics can leave pitch/roll in the quaternion while angular velocity is yaw-only; this corrects that on open water.
    /// </summary>
    private static void ApplyWaterUprightStabilization(RigidBody body, float dtSec)
    {
        if (dtSec <= 0f)
            return;

        var jo = body.Orientation;
        var q = Quaternion.Normalize(new Quaternion(jo.X, jo.Y, jo.Z, jo.W));
        var bodyUp = Vector3.Transform(Vector3.UnitY, q);
        var worldUp = Vector3.UnitY;

        var axis = Vector3.Cross(bodyUp, worldUp);
        var lenSq = axis.LengthSquared();
        float angle;

        if (lenSq < 1e-14f)
        {
            var dotParallel = Vector3.Dot(bodyUp, worldUp);
            if (dotParallel > 1f - 1e-6f)
                return;
            if (dotParallel < -1f + 1e-6f)
            {
                axis = Vector3.Normalize(Vector3.Cross(bodyUp, Vector3.UnitX));
                if (axis.LengthSquared() < 1e-12f)
                    axis = Vector3.Normalize(Vector3.Cross(bodyUp, Vector3.UnitZ));
                angle = MathF.PI;
            }
            else
                return;
        }
        else
        {
            axis = Vector3.Normalize(axis);
            var dot = Math.Clamp(Vector3.Dot(bodyUp, worldUp), -1f, 1f);
            angle = MathF.Acos(dot);
        }

        if (angle < ShipMotionDefaults.UprightStabilizeAngleDeadZoneRad)
            return;

        var maxStep = ShipMotionDefaults.UprightStabilizeMaxRadPerSec * dtSec;
        var step = MathF.Min(angle, maxStep);
        var deltaQ = Quaternion.CreateFromAxisAngle(axis, step);
        var qNew = Quaternion.Normalize(Quaternion.Multiply(deltaQ, q));
        body.Orientation = new JQuaternion(qNew.X, qNew.Y, qNew.Z, qNew.W);
    }
}
