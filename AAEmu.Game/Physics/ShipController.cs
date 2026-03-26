#nullable enable

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

public class ShipController(World world, ShipModelV1 shipModel, float waterLevel = 100f)
{
    private readonly World _world = world ?? throw new ArgumentNullException(nameof(world));

    public RigidBody Hull { get; private set; } = null!;
    public ShipModelV1 ShipModel { get; init; } = shipModel ?? throw new ArgumentNullException(nameof(shipModel));

    private readonly float _waterLevel = waterLevel;
    private const float FluidDensity = 1025f; // kg/m³

    /// <summary>Extra deceleration when throttle opposes current speed (reverse while moving forward, etc.).</summary>
    private const float OpposingThrottleAccelMul = 1.75f;

    /// <summary>Only for opposing throttle — extra braking on top (does not affect forward accel).</summary>
    private const float OpposingThrottleBrakeTuneMul = 1.2f;

    /// <summary>Steering builds turn rate faster without changing the max turn cap.</summary>
    private const float SteeringResponsivenessMul = 1.45f;

    /// <summary>When rudder fights current yaw rate — faster decay; same-direction turn rate unchanged.</summary>
    private const float CounterSteerResponsivenessMul = 1.35f;

    /// <summary>ship_models.steer_vel is often a small coefficient (~1), not °/s — only trust it above this.</summary>
    private const float MinSteerVelAsDegPerSec = 8f;

    /// <summary>At zero speed, keep this fraction of turning ability (so you can still rotate in place).</summary>
    private const float MinTurnFactorAtZeroSpeed = 0.5f;

    /// <summary>Speed (in current ship speed units) at which turning reaches 100%.</summary>
    private const float TurnFullFactorAtSpeed = 2.5f;

    /// <summary>Max forward/back speed multiplier removed at full yaw rate (linear in |ω|/ω_max).</summary>
    private const float TurnSpeedSlowdownFrac = 0.1f;

    /// <summary>Higher = snappier convergence of <see cref="Slave.TurnSpeedVelocityMul"/> toward the turn target.</summary>
    private const float TurnSpeedVelocityMulResponse = 5.5f;

    /// <summary>
    /// Fixed max yaw rate (degrees/s) by <see cref="SlaveKind"/>.
    /// NOTE: We intentionally do NOT use ship_models.steer_vel because values in DB are not in valid units.
    /// </summary>
    private static float GetSteerMaxDegFixed(SlaveKind kind) => kind switch
    {
        SlaveKind.Boat => 8.7f,                // лодки
        SlaveKind.SmallSailingShip => 5.2f,    // малыепарусник
        SlaveKind.BigSailingShip => 4.7f,      // большиепарусник
        SlaveKind.Fishboat => 6.7f,            // рыбацкие корабли
        SlaveKind.Speedboat => 10.7f,          // катер
        SlaveKind.MerchantShip => 6.2f,        // шхуна
        _ => 6.7f
    };

    /// <summary>Horizontal wind when no river flow and clock wind is off/unavailable (game X,Y).</summary>
    private const float DefaultWindDirX = 0f;
    private const float DefaultWindDirY = 1f;

    /// <summary>Open-sea wind rotates with <see cref="TimeManager"/>; river <see cref="Slave.CachedWaterFlow"/> still wins.</summary>
    private const bool WindFollowsTimeOfDay = true;

    /// <summary>Shift wind phase in game hours (e.g. if «полночь» в клиенте не совпадает с 0:00).</summary>
    private const float WindTimePhaseOffsetHours = 0f;

    /// <summary>Within this cone from wind axis (±15°) apply full ±15% speed limits.</summary>
    private const float WindConeHalfAngleDeg = 15f;

    private const float WindWithMaxMul = 1.15f;
    private const float WindAgainstMaxMul = 0.85f;

    /// <summary>How wind affects max speed: none (rowing/motor), square (downwind best), lateen (beam reach best).</summary>
    private enum ShipWindProfile
    {
        None,
        SquareRig,
        LateenRig
    }

    /// <summary>Override: these <see cref="SlaveTemplate.Id"/> use lateen (e.g. trimaran under SmallSailingShip).</summary>
    private static readonly HashSet<uint> WindProfileLateenTemplateIds = [];

    /// <summary>Override: these template ids use square rig (e.g. Harani sailboat under SmallSailingShip).</summary>
    private static readonly HashSet<uint> WindProfileSquareTemplateIds = [];

    /// <summary>Override: force no wind (e.g. a sail template you want to treat as motor-only).</summary>
    private static readonly HashSet<uint> WindProfileNoneTemplateIds = [];

    private static ShipWindProfile ResolveShipWindProfile(Slave slave)
    {
        var tid = slave.Template.Id;
        if (WindProfileNoneTemplateIds.Contains(tid))
            return ShipWindProfile.None;
        if (WindProfileLateenTemplateIds.Contains(tid))
            return ShipWindProfile.LateenRig;
        if (WindProfileSquareTemplateIds.Contains(tid))
            return ShipWindProfile.SquareRig;

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
        var cosCone = MathF.Cos(WindConeHalfAngleDeg * MathF.PI / 180f);
        if (dotMove >= cosCone)
            return WindWithMaxMul;
        if (dotMove <= -cosCone)
            return WindAgainstMaxMul;
        return 1f + (WindWithMaxMul - 1f) * (dotMove / cosCone);
    }

    /// <summary>Lateen / fore-and-aft: best speed on a beam reach (perpendicular to wind).</summary>
    private static float GetWindSpeedMulLateenRig(float dotMove)
    {
        var cosCone = MathF.Cos(WindConeHalfAngleDeg * MathF.PI / 180f);
        var sinCone = MathF.Sin(WindConeHalfAngleDeg * MathF.PI / 180f);
        var p = 1f - MathF.Abs(dotMove);
        if (p >= cosCone)
            return WindWithMaxMul;
        if (p <= sinCone)
            return WindAgainstMaxMul;
        return WindAgainstMaxMul + (p - sinCone) / (cosCone - sinCone) * (WindWithMaxMul - WindAgainstMaxMul);
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
            var cosCone = MathF.Cos(WindConeHalfAngleDeg * MathF.PI / 180f);
            var dotAbs = MathF.Abs(dotBow);
            // Hard cutoff: bonus disappears immediately beyond the angle threshold.
            return dotAbs >= cosCone ? WindWithMaxMul : 1f;
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
        var shipBoxShape = new BoxShape(ShipModel.MassBoxSizeY, ShipModel.MassBoxSizeZ, ShipModel.MassBoxSizeX);
        // Center offset
        var shipCenterPoint = new TransformedShape(shipBoxShape, new JVector(ShipModel.MassCenterX, ShipModel.MassCenterZ, ShipModel.MassCenterY));
        // Add shape
        Hull.AddShape(shipCenterPoint);
        // Set Mass
        Hull.SetMassInertia(ShipModel.Mass);
        Hull.DeactivationTime = TimeSpan.MaxValue;
        Hull.IsStatic = false;
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

        // If not in water, disable input for ships
        if (slave.CachedFloorLevel > slave.CachedWaterSurface)
        {
            slave.Throttle = 0;
            slave.Steering = 0;
            slave.ThrottleSmoothed = 0f;
            slave.SteeringSmoothed = 0f;
            slave.TurnSpeedVelocityMul = 1f;
        }

        // Minimum crawl speed when starting in that direction only. Do not apply while still moving
        // the other way — otherwise forward+reverse snaps Speed to ±1 and the ship stops instantly.
        if (slave is { Throttle: > 0, Speed: < 1f } && slave.Speed >= 0f)
            slave.Speed = 1f;

        if (slave is { Throttle: < 0, Speed: > -1f } && slave.Speed <= 0f)
            slave.Speed = -1f;

        var throttleNorm = slave.Throttle * 0.00787401575f; // sbyte -> float
        var steeringNorm = slave.Steering * 0.00787401575f; // sbyte -> float
        var dtSec = (float)deltaTime.TotalSeconds;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));
        var slaveRotRad = rpy.Item1 + 1.57f; // bow heading in physics XZ; reused for wind + velocity

        // Use data reverse_accel for braking; scale up when fighting current motion (feels less sluggish than forward-only Accel).
        var linearAccel = shipModel.Accel;
        if (throttleNorm != 0f && slave.Speed != 0f && Math.Sign(slave.Speed) != Math.Sign(throttleNorm))
        {
            var reverseCap = shipModel.ReverseAccel > 0f ? shipModel.ReverseAccel : shipModel.Accel;
            linearAccel = Math.Max(shipModel.Accel, reverseCap) * OpposingThrottleAccelMul * OpposingThrottleBrakeTuneMul;
        }

        // Calculate speed
        slave.Speed += throttleNorm * (linearAccel * dtSec) / 2f;

        // Clamp speed between min and max Velocity (wind: ±15% of max speed when within ±15° of with/against wind)
        var windMul = GetWindSpeedMul(slave, slaveRotRad);
        var maxForward = shipModel.Velocity * slave.MoveSpeedMul / 2f * windMul;
        var maxBackward = -shipModel.ReverseVelocity * slave.MoveSpeedMul / 2f * windMul;

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
        var speed01 = Math.Clamp(speedAbs / TurnFullFactorAtSpeed, 0f, 1f);
        var turnFactor = MinTurnFactorAtZeroSpeed + (1f - MinTurnFactorAtZeroSpeed) * speed01;

        // Reverse steering should be handled at input→rotSpeed stage, not at the final angular velocity assignment.
        // Otherwise when speed crosses zero (e.g. releasing reverse), the last-step inversion toggles and the ship appears
        // to start turning the opposite way even though the rudder input didn't change.
        const float ReverseSteerEpsilon = 0.05f;
        var isMovingBackward = slave.Speed < -ReverseSteerEpsilon || (MathF.Abs(slave.Speed) <= ReverseSteerEpsilon && slave.LastMoveDirSign < 0);
        var effectiveSteeringNorm = isMovingBackward ? -steeringNorm : steeringNorm;

        // Per-kind turn rate (normal cap), used both for clamping and for non-linear "approach-to-cap" behavior.
        var kindSteerDeg = GetSteerMaxDegFixed(slave.Template.SlaveKind);
        var steerMaxDegNormal = Math.Max(0.05f, kindSteerDeg);
        var steerMaxDegHard = Math.Max(0.05f, kindSteerDeg * 2f);
        var steerMaxNormal = (steerMaxDegNormal * turnFactor).DegToRad();

        // Calculate rotation speed
        var turnSpeed = slave.TurnSpeed == 0 ? 10f : slave.TurnSpeed * (float)deltaTime.TotalSeconds * MathF.PI;
        var rotDelta = effectiveSteeringNorm * (turnSpeed / 100f) * (shipModel.TurnAccel / 360f) * SteeringResponsivenessMul * turnFactor;
        if (slave.RotSpeed != 0f && effectiveSteeringNorm != 0f && Math.Sign(slave.RotSpeed) != Math.Sign(effectiveSteeringNorm))
            rotDelta *= CounterSteerResponsivenessMul;

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

        // Max turn rate (fixed by ship kind).
        //
        // DB-based steer cap is intentionally disabled because ship_models.steer_vel is not in valid units.
        // If needed later, restore this block and remove GetSteerMaxDegFixed usage:
        // var steerMaxDeg = shipModel.SteerVel >= MinSteerVelAsDegPerSec
        //     ? Math.Min(shipModel.SteerVel, MaxSteerDegPerSec)
        //     : Math.Min(shipModel.Velocity * 2f, MaxSteerDegPerSec);
        // Normal (design) max turn rate is per-ship-kind; hard cap is 2x of that value.
        // This avoids ships constantly saturating at the "cap" during normal steering.
        var steerMaxHard = (steerMaxDegHard * turnFactor).DegToRad();
        slave.RotSpeed = Math.Clamp(slave.RotSpeed, -steerMaxHard, steerMaxHard);

        // While steering, keep yaw rate within the normal per-kind limit.
        // (Hard cap still protects from spikes / edge cases.)
        if (slave.Steering != 0)
        {
            var steerMaxNormalRad = (steerMaxDegNormal * turnFactor).DegToRad();
            slave.RotSpeed = Math.Clamp(slave.RotSpeed, -steerMaxNormalRad, steerMaxNormalRad);
        }

        var steerMax = (steerMaxDegNormal * turnFactor).DegToRad();

        // Up to TurnSpeedSlowdownFrac slower at full yaw rate; smooth return on straight course (forward and reverse).
        var steerMaxSafe = Math.Max(steerMax, 1e-5f);
        var turnRateNorm = Math.Clamp(MathF.Abs(slave.RotSpeed) / steerMaxSafe, 0f, 1f);
        var targetTurnVelMul = 1f - TurnSpeedSlowdownFrac * turnRateNorm;
        var turnMulA = 1f - MathF.Exp(-TurnSpeedVelocityMulResponse * MathF.Max(0f, dtSec));
        slave.TurnSpeedVelocityMul += (targetTurnVelMul - slave.TurnSpeedVelocityMul) * turnMulA;

        // Slow down turning if no steering active
        const float AngularDamping = 0.975f; // Damping of angular velocity
        if (slave.Steering == 0)
        {
            slave.RotSpeed *= AngularDamping;
        }

        // If not in water, seriously slow down the velocity
        const float FloorCollisionSpeedMultiplier = 0.975f;
        if (slave.CachedFloorLevel > slave.CachedWaterSurface)
        {
            slave.Speed *= FloorCollisionSpeedMultiplier;
            slave.RigidBody.Velocity *= FloorCollisionSpeedMultiplier;
        }

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
            const float coastDragMaxMul = 0.45f; // at/above ~2 speed units
            var dragMul = coastDragMinMul + (coastDragMaxMul - coastDragMinMul) * lowSpeedCurve;
            var effectiveDrag = drag * dragMul;

            // Hard cap so "let go of throttle" never brakes harder than intended,
            // even if ship_models.water_resistance is high for some templates.
            const float maxCoastDragPerSecond = 0.22f;
            effectiveDrag = MathF.Min(effectiveDrag, maxCoastDragPerSecond);

            var decay = MathF.Exp(-effectiveDrag * dt);
            slave.Speed *= decay;

            if (MathF.Abs(slave.Speed) < 0.002f)
                slave.Speed = 0f;
        }

        var forceThrottle = slave.Speed * slave.MoveSpeedMul / 4f * slave.TurnSpeedVelocityMul; // Not sure if correct, but it feels correct

        // Apply directional force
        rigidBody.Velocity = new JVector(forceThrottle * MathF.Cos(slaveRotRad), 0.0f, forceThrottle * MathF.Sin(slaveRotRad));

        rigidBody.AngularVelocity = new JVector(0, slave.RotSpeed * -1f, 0);

        //Logger.Debug($"Slave: {slave.Name}, Throttle: {throttleFloatVal:F1} ({slave.ThrottleRequest}), Steering {steeringFloatVal:F1} ({slave.SteeringRequest}), speed: {slave.Speed}, rotSpeed: {slave.RotSpeed}");
    }
}
