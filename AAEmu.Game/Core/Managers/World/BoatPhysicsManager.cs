using System;
using System.Numerics;
using System.Threading;

using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using Jitter.Collision;
using Jitter.Collision.Shapes;
using Jitter.Dynamics;
using Jitter.LinearMath;

using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Core.Managers.World;

public class BoatPhysicsManager//: Singleton<BoatPhysicsManager>
{
    /// <summary>
    /// Ticks per second for the physics engine
    /// </summary>
    private float TargetPhysicsTps { get; set; } = 5f;
    private Thread _thread;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private CollisionSystem _collisionSystem;
    private Jitter.World _physWorld;
    private Buoyancy _buoyancy;
    private uint _tickCount;
    private bool ThreadRunning { get; set; }
    public InstanceWorld SimulationWorld { get; set; }
    private object _slaveListLock = new();
    private Random _random = new();

    private bool CustomWater(ref JVector area)
    {
        // Query world if it's water and treat everything below 100 as water as a fallback
        return SimulationWorld?.IsWater(new Vector3(area.X, area.Z, area.Y)) ?? area.Y <= 100f;
    }

    public void Initialize()
    {
        _collisionSystem = new CollisionSystemSAP();
        _physWorld = new Jitter.World(_collisionSystem);
        _buoyancy = new Buoyancy(_physWorld);
        _buoyancy.UseOwnFluidArea(CustomWater);
        // _buoyancy.FluidBox = new JBBox(new JVector(0, 0, 0), new JVector(100000, 100, 100000));

        // Добавим поверхность земли // Add ground surface
        if (SimulationWorld.Name != "main_world") { return; }
        try
        {
            var hmap = WorldManager.Instance.GetWorld(0).HeightMaps;
            var heightMaxCoefficient = WorldManager.Instance.GetWorld(0).HeightMaxCoefficient;
            var dx = hmap.GetLength(0);
            var dz = hmap.GetLength(1);
            var hmapTerrain = new float[dx, dz];
            for (var x = 0; x < dx; x += 1)
                for (var y = 0; y < dz; y += 1)
                    hmapTerrain[x, y] = (float)(hmap[x, y] / heightMaxCoefficient);
            var terrain = new TerrainShape(hmapTerrain, 2.0f, 2.0f);
            var body = new RigidBody(terrain) { IsStatic = true };
            _physWorld.AddBody(body);
        }
        catch (Exception e)
        {
            Logger.Error("{0}\n{1}", e.Message, e.StackTrace);
        }
    }

    public void StartPhysics()
    {
        ThreadRunning = true;
        _thread = new Thread(PhysicsThread);
        _thread.Name = "Physics-" + (SimulationWorld?.Name ?? "???");
        _thread.Start();
    }

    private void PhysicsThread()
    {
        try
        {
            Logger.Debug($"PhysicsThread Start: {Thread.CurrentThread.Name} ({Environment.CurrentManagedThreadId})");
            var simulatedSlaveTypeList = new[]
            {
                SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip,
                SlaveKind.MerchantShip, SlaveKind.Speedboat
            };
            while (ThreadRunning && Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep((int)Math.Floor(1000f / TargetPhysicsTps));
                _physWorld.Step(1f / TargetPhysicsTps, false);
                _tickCount++;

                lock (_slaveListLock)
                {
                    // Not sure if it's better to query it each tick, or track them locally
                    var slaveList = SlaveManager.Instance.GetActiveSlavesByKinds(simulatedSlaveTypeList, SimulationWorld.Id);
                    if (slaveList == null)
                        continue;

                    foreach (var slave in slaveList)
                    {
                        if (slave.Transform.WorldId != SimulationWorld.Id)
                        {
                            Logger.Debug($"Skip {slave.Name}");
                            continue;
                        }

                        // Skip simulation if still summoning
                        if (slave.SpawnTime.AddSeconds(slave.Template.PortalTime) > DateTime.UtcNow)
                            continue;

                        // Skip simulation if no rigidbody applied to slave
                        var slaveRigidBody = slave.RigidBody;
                        if (slaveRigidBody == null)
                            continue;

                        // Note: Y, Z swapped
                        var xDelta = slaveRigidBody.Position.X - slave.Transform.World.Position.X;
                        var yDelta = slaveRigidBody.Position.Z - slave.Transform.World.Position.Y;
                        var zDelta = slaveRigidBody.Position.Y - slave.Transform.World.Position.Z;

                        slave.Transform.Local.Translate(xDelta, yDelta, zDelta);
                        var rot = JQuaternion.CreateFromMatrix(slaveRigidBody.Orientation);
                        slave.Transform.Local.ApplyFromQuaternion(rot.X, rot.Z, rot.Y, rot.W);

                        // if (_tickCount % 6 == 0)
                        _physWorld.CollisionSystem.Detect(true);
                        BoatPhysicsTick(slave, slaveRigidBody);
                        // Logger.Debug($"{_thread.Name}, slave: {slave.Name} collision check tick");
                    }
                }
            }
            Logger.Debug($"PhysicsThread End: {Thread.CurrentThread.Name} ({Environment.CurrentManagedThreadId})");
        }
        catch (Exception e)
        {
            Logger.Error($"StartPhysics: {e}");
        }
    }

    public void AddShip(Slave slave)
    {
        var shipModel = ModelManager.Instance.GetShipModel(slave.ModelId);
        if (shipModel == null) { return; }
        var slaveBox = new BoxShape(shipModel.MassBoxSizeX, shipModel.MassBoxSizeZ, shipModel.MassBoxSizeY);
        var slaveMaterial = new Material();
        // TODO: Add the center of mass settings into JitterPhysics somehow

        var rigidBody = new RigidBody(slaveBox, slaveMaterial)
        {
            Position = new JVector(slave.Transform.World.Position.X, slave.Transform.World.Position.Z, slave.Transform.World.Position.Y),
            // Mass = shipModel.Mass, // Using the actually defined mass of the DB doesn't really work
            Orientation = JMatrix.CreateRotationY(slave.Transform.World.Rotation.Z)
        };

        _buoyancy.Add(rigidBody, 3);
        _physWorld.AddBody(rigidBody);
        slave.RigidBody = rigidBody;
        Logger.Debug($"AddShip {slave.Name} -> {SimulationWorld.Name}");
    }

    public void RemoveShip(Slave slave)
    {
        if (slave.RigidBody == null) return;
        _buoyancy.Remove(slave.RigidBody);
        _physWorld.RemoveBody(slave.RigidBody);
        Logger.Debug($"RemoveShip {slave.Name} <- {SimulationWorld.Name}");
    }

    private void BoatPhysicsTick(Slave slave, RigidBody rigidBody)
    {
        var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
        moveType.UseSlaveBase(slave);
        var shipModel = ModelManager.Instance.GetShipModel(slave.Template.ModelId);

        // If no driver, then no steering allowed
        if (!slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver))
        {
            slave.ThrottleRequest = 0;
            slave.SteeringRequest = 0;
        }

        slave.Throttle = ComputeThrottledInput(slave.ThrottleRequest, slave.Throttle, (int)Math.Ceiling(shipModel.Velocity / shipModel.Accel));
        slave.Steering = ComputeThrottledInput(slave.SteeringRequest, slave.Steering, (int)Math.Ceiling(shipModel.SteerVel / shipModel.TurnAccel));
        slave.RigidBody.IsActive = true;

        // Provide minimum speed of 1 when Throttle is used
        if (slave.Throttle > 0 && slave.Speed < 1f)
            slave.Speed = 1f;
        if (slave.Throttle < 0 && slave.Speed > -1f)
            slave.Speed = -1f;

        var throttleFloatVal = slave.Throttle * 0.00787401575f; // sbyte -> float
        var steeringFloatVal = slave.Steering * 0.00787401575f; // sbyte -> float

        // Calculate speed
        slave.Speed += throttleFloatVal * (shipModel.Accel / 10f);
        // Clamp speed between min and max Velocity
        slave.Speed = Math.Min(slave.Speed, shipModel.Velocity);
        slave.Speed = Math.Max(slave.Speed, -shipModel.ReverseVelocity);

        // Calculate rotation speed
        slave.RotSpeed += steeringFloatVal * (slave.TurnSpeed / 100f) * (shipModel.TurnAccel / 360f);
        // Clamp to Steer Velocity
        slave.RotSpeed = Math.Min(slave.RotSpeed, shipModel.SteerVel);
        slave.RotSpeed = Math.Max(slave.RotSpeed, -shipModel.SteerVel);

        // Slow down turning if no steering active
        if (slave.Steering == 0)
        {
            slave.RotSpeed -= slave.RotSpeed / (TargetPhysicsTps * 5);
            if (Math.Abs(slave.RotSpeed) <= 0.01)
                slave.RotSpeed = 0;
        }
        slave.RotSpeed = Math.Clamp(slave.RotSpeed, -1f, 1f);

        // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
        if (slave.Throttle == 0)
        {
            slave.Speed -= slave.Speed / (TargetPhysicsTps * 5f);
            if (Math.Abs(slave.Speed) < 0.01)
                slave.Speed = 0;
        }
        // Logger.Debug($"Slave: {slave.Name}, Throttle: {throttleFloatVal:F1} ({slave.ThrottleRequest}), Steering {steeringFloatVal:F1} ({slave.SteeringRequest}), speed: {slave.Speed}, rotSpeed: {slave.RotSpeed}");

        // Calculate some stuff for later
        var boxSize = rigidBody.Shape.BoundingBox.Max - rigidBody.Shape.BoundingBox.Min;
        var tubeVolume = shipModel.TubeLength * shipModel.TubeRadius * MathF.PI;
        var solidVolume = MathF.Abs(rigidBody.Mass - tubeVolume);

        // Get the floor height coordinates
        var floor = WorldManager.Instance.GetHeight(slave.Transform);
        Logger.Trace($"[Height] Z-Pos: {slave.Transform.World.Position.Z} - Floor: {floor}");

        // Check floor collision
        if (slave.Transform.World.Position.Z - boxSize.Y / 2 - floor < 1.0 && AppConfiguration.Instance.HeightMapsEnable)
        {
            if (slave.Hp <= 0)
            {
                slave.Speed = 0;
                return;
            }

            var damage = _random.Next(500, 750); // damage randomly 500-750
            if (damage > 0)
            {
                slave.DoFloorCollisionDamage(damage, false, KillReason.Collide);
            }

            Logger.Debug($"Slave: {slave.ObjId}, speed: {slave.Speed}, rotSpeed: {slave.RotSpeed}, floor: {floor}, Z: {slave.Transform.World.Position.Z}, damage: {damage}");
        }

        // Get current rotation of the ship
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(rigidBody.Orientation);
        var slaveRotRad = rpy.Item1 + 90 * (MathF.PI / 180.0f);

        var forceThrottle = slave.Speed * slave.MoveSpeedMul; // Not sure if correct, but it feels correct
        // Apply directional force
        rigidBody.AddForce(new JVector(forceThrottle * rigidBody.Mass * MathF.Cos(slaveRotRad), 0.0f, forceThrottle * rigidBody.Mass * MathF.Sin(slaveRotRad)));

        var steer = slave.RotSpeed * 60f;
        // Make sure the steering is reversed when going backwards.
        if (forceThrottle < 0)
            steer *= -1;

        // Calculate Steering Force based on bounding box
        var steerForce = -steer * (solidVolume * boxSize.X * boxSize.Y / 172.5f * 2f); // Totally random value, but it feels right
        //var steerForce = -steer * solidVolume ;
        rigidBody.AddTorque(new JVector(0, steerForce, 0));

        /*
        if ((slave.Steering != 0) || (slave.Throttle != 0))
            Logger.Debug($"Request: {slave.SteeringRequest}, Steering: {slave.Steering}, steer: {steer}, vol: {solidVolume} mass: {rigidBody.Mass}, force: {steerForce}, torque: {rigidBody.Torque}");
        */

        // Insert new Rotation data into MoveType
        var (rotZ, rotY, rotX) = MathUtil.GetSlaveRotationFromDegrees(rpy.Item1, rpy.Item2, rpy.Item3);
        moveType.RotationX = rotX;
        moveType.RotationY = rotY;
        moveType.RotationZ = rotZ;

        // Fill in the Velocity Data into the MoveType
        moveType.Velocity = new Vector3(rigidBody.LinearVelocity.X, rigidBody.LinearVelocity.Z, rigidBody.LinearVelocity.Y);
        moveType.AngVelX = rigidBody.AngularVelocity.X;
        moveType.AngVelY = rigidBody.AngularVelocity.Z;
        moveType.AngVelZ = rigidBody.AngularVelocity.Y;

        // Seems display the correct speed this way, but what happens if you go over the bounds ?
        moveType.VelX = (short)(rigidBody.LinearVelocity.X * 1024);
        moveType.VelY = (short)(rigidBody.LinearVelocity.Z * 1024);
        moveType.VelZ = (short)(rigidBody.LinearVelocity.Y * 1024);

        // Create virtual offset, this is not a good solution, but it'll have to do for now.
        // This will likely create issues with skill that generate position specified plots likely not having this offset when on the ship

        // Don't know how to handle X/Y for this, if we even should ...
        // moveType.X += shipModel.MassCenterX;
        // moveType.Y += shipModel.MassCenterY;

        // We can more or less us the model Mass Center Z value to get how much it needs to sink
        // It doesn't actually do this server-side, as we only modify the packet sent to the players
        // If center of mass is positive rather than negative, we need to ignore it here to prevent the boat from floating
        moveType.Z += (shipModel.MassCenterZ < 0f ? shipModel.MassCenterZ / 2f : 0f); // - shipModel.KeelHeight;

        // Do not allow the body to flip
        slave.RigidBody.Orientation = JMatrix.CreateFromYawPitchRoll(rpy.Item1, 0, 0); // TODO: Fix me with proper physics

        // Apply new Location/Rotation to GameObject
        slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
        var jRot = JQuaternion.CreateFromMatrix(rigidBody.Orientation);
        slave.Transform.Local.ApplyFromQuaternion(jRot.X, jRot.Z, jRot.Y, jRot.W);

        // Send the packet
        slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
        // Logger.Debug("Island: {0}", slave.RigidBody.CollisionIsland.Bodies.Count);

        // Update all to main Slave and it's children
        slave.Transform.FinalizeTransform();
    }

    internal void Stop()
    {
        ThreadRunning = false;
    }

    /// <summary>
    /// Calculate smooth transition for throttle and steering
    /// </summary>
    /// <param name="inputRequest">New request value</param>
    /// <param name="currentValue">Current calculated value</param>
    /// <param name="acceleration">Step size</param>
    /// <returns></returns>
    private static sbyte ComputeThrottledInput(sbyte inputRequest, sbyte currentValue, int acceleration)
    {
        var inputSign = Math.Sign(inputRequest);
        var inputVal = Math.Abs(inputRequest);

        var curSign = Math.Sign(currentValue);
        var curVal = Math.Abs(currentValue);

        var sameDirectionPush = ((inputSign > 0) && (curSign >= 0)) || ((inputSign < 0) && (curSign <= 0));
        var oppositeDirectionPush = ((inputSign < 0) && (curSign >= 0)) || ((inputSign > 0) && (curSign <= 0));

        // Slowing down?
        if (sameDirectionPush && inputVal < curVal)
        {
            sameDirectionPush = false;
            oppositeDirectionPush = true;
        }

        if (sameDirectionPush)
            return (sbyte)(Math.Clamp(Math.Max(inputVal, curVal) + acceleration, sbyte.MinValue, sbyte.MaxValue) * inputSign);

        if (oppositeDirectionPush)
            return (sbyte)(Math.Clamp(Math.Max(inputVal, curVal) - acceleration, sbyte.MinValue, sbyte.MaxValue) * inputSign);

        return 0;
    }
}
