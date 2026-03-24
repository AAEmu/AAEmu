#nullable enable

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Models;
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

    /// <summary>Max yaw rate (degrees/s). Legacy code used (velocity×2)°/s — often ~25°/s on large ships.</summary>
    private const float MaxSteerDegPerSec = 5.2f;

    /// <summary>ship_models.steer_vel is often a small coefficient (~1), not °/s — only trust it above this.</summary>
    private const float MinSteerVelAsDegPerSec = 8f;

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
        }

        // Minimum crawl speed when starting in that direction only. Do not apply while still moving
        // the other way — otherwise forward+reverse snaps Speed to ±1 and the ship stops instantly.
        if (slave is { Throttle: > 0, Speed: < 1f } && slave.Speed >= 0f)
            slave.Speed = 1f;

        if (slave is { Throttle: < 0, Speed: > -1f } && slave.Speed <= 0f)
            slave.Speed = -1f;

        var throttleNorm = slave.Throttle * 0.00787401575f; // sbyte -> float
        var steeringNorm = slave.Steering * 0.00787401575f; // sbyte -> float

        // Use data reverse_accel for braking; scale up when fighting current motion (feels less sluggish than forward-only Accel).
        var linearAccel = shipModel.Accel;
        if (throttleNorm != 0f && slave.Speed != 0f && Math.Sign(slave.Speed) != Math.Sign(throttleNorm))
        {
            var reverseCap = shipModel.ReverseAccel > 0f ? shipModel.ReverseAccel : shipModel.Accel;
            linearAccel = Math.Max(shipModel.Accel, reverseCap) * OpposingThrottleAccelMul * OpposingThrottleBrakeTuneMul;
        }

        // Calculate speed
        slave.Speed += throttleNorm * (linearAccel * (float)deltaTime.TotalSeconds) / 2f;

        // Clamp speed between min and max Velocity
        var maxForward = shipModel.Velocity * slave.MoveSpeedMul / 2f;
        var maxBackward = -shipModel.ReverseVelocity * slave.MoveSpeedMul / 2f;
        slave.Speed = Math.Clamp(slave.Speed, maxBackward, maxForward);

        // Calculate rotation speed
        var turnSpeed = slave.TurnSpeed == 0 ? 10f : slave.TurnSpeed * (float)deltaTime.TotalSeconds * MathF.PI;
        var rotDelta = steeringNorm * (turnSpeed / 100f) * (shipModel.TurnAccel / 360f) * SteeringResponsivenessMul;
        if (slave.RotSpeed != 0f && steeringNorm != 0f && Math.Sign(slave.RotSpeed) != Math.Sign(steeringNorm))
            rotDelta *= CounterSteerResponsivenessMul;
        slave.RotSpeed += rotDelta;

        // Max turn rate: prefer velocity×2°/s (legacy). steer_vel in DB is often ~1 (wrong units) — only use as °/s when plausible.
        var steerMaxDeg = shipModel.SteerVel >= MinSteerVelAsDegPerSec
            ? Math.Min(shipModel.SteerVel, MaxSteerDegPerSec)
            : Math.Min(shipModel.Velocity * 2f, MaxSteerDegPerSec);
        var steerMax = steerMaxDeg.DegToRad();
        slave.RotSpeed = Math.Clamp(slave.RotSpeed, -steerMax, steerMax);

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
            slave.Speed -= (float)deltaTime.TotalSeconds * float.Sign(slave.Speed) * shipModel.WaterResistance;
            if (Math.Abs(slave.Speed) < 1)
            {
                slave.Speed = 0;
                slave.RigidBody.Velocity = JVector.Zero;
            }
        }

        // Get current rotation of the ship
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));
        var slaveRotRad = rpy.Item1 + 1.57f; // 90 degrees in radians

        var forceThrottle = slave.Speed * slave.MoveSpeedMul / 4f; // Not sure if correct, but it feels correct

        // Apply directional force
        rigidBody.Velocity = new JVector(forceThrottle * MathF.Cos(slaveRotRad), 0.0f, forceThrottle * MathF.Sin(slaveRotRad));

        var steer = slave.RotSpeed * -1;

        // Make sure the steering is reversed when going backwards.
        if (forceThrottle < 0)
            steer *= -1;

        rigidBody.AngularVelocity = new JVector(0, steer, 0);

        //Logger.Debug($"Slave: {slave.Name}, Throttle: {throttleFloatVal:F1} ({slave.ThrottleRequest}), Steering {steeringFloatVal:F1} ({slave.SteeringRequest}), speed: {slave.Speed}, rotSpeed: {slave.RotSpeed}");
    }
}
