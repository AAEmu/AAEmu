using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Physics.HeightMaps;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

// ReSharper disable HollowTypeName
public class PhysicsManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    /// <summary>
    /// WorldInstance this physics engine is running for
    /// </summary>
    public WorldInstance SimulationWorld { get; init; }

    private const float DefaultWaterLevel = 100f;

    /// <summary>
    /// Target Ticks per Second for Physics in this world, use setting as default value
    /// </summary>
    // TODO: Make this variable or configurable from a GM command or dynamic load system
    public float TargetPhysicsTps { get; set; } = AppConfiguration.Instance.World.TargetPhysicsTps;
    public float TargetPhysicsTickTime => 1f / TargetPhysicsTps;
    internal Thread _thread;

    /// <summary>
    /// The physics engine's World
    /// </summary>
    internal Jitter2.World _physWorld;

    internal Buoyancy _buoyancy;
    internal bool ThreadRunning { get; set; }

    /// <summary>
    /// List of Ship controllers (slaveId, controller)
    /// </summary>
    private readonly Dictionary<uint, ShipController> _shipControllers = new();

    private readonly ConcurrentQueue<Action> _pendingActions = new();
    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _worldLock = new();
    private readonly List<RigidBody> _bodies = [];

    /// <summary>
    /// Used heightmap tester, saved so it can be edited later
    /// </summary>
    private HeightmapTester WorldHeightMapTester { get; set; }

    /// <summary>
    /// Initialize the Physics engine and creates the ocean water body
    /// </summary>
    public void Initialize()
    {
        _physWorld = new Jitter2.World();
        _physWorld.Gravity = new JVector(0, -9.81f, 0);

        _buoyancy = new Buoyancy(_physWorld) {
            FluidBox = new JBoundingBox(
                new JVector(0, 0, 0), // Bottom
                new JVector(SimulationWorld.Template.CellX * WorldManager.CELL_SIZE, SimulationWorld.Template.OceanLevel, SimulationWorld.Template.CellY * WorldManager.CELL_SIZE) // Surface
            )
        };
        _buoyancy.UseOwnFluidArea(CustomWater);

        Logger.Info($"{SimulationWorld.Template.Name} initialized.");
    }

    /// <summary>
    /// Create terrain data for the physics world (old)
    /// </summary>
    public void InitializeTerrain()
    {
        // Add terrain shape based on height map
        // if (SimulationWorld.Id != WorldManager.DefaultInstanceId) { return; }

        Logger.Debug($"{SimulationWorld.Template.Name} initializing terrain.");
        try
        {
            var dataX = SimulationWorld.Template.CellX * WorldManager.CELL_HMAP_RESOLUTION;
            var dataZ = SimulationWorld.Template.CellY * WorldManager.CELL_HMAP_RESOLUTION;
            var hmapTerrain = new float[dataX, dataZ];
            var cellCountMax = SimulationWorld.Template.CellX * SimulationWorld.Template.CellY * 1f;
            var cellCount = 0;
            for (var cellY = 0; cellY < SimulationWorld.Template.CellY; cellY++)
            {
                for (var cellX = 0; cellX < SimulationWorld.Template.CellX; cellX++)
                {
                    cellCount++;
                    var cell = SimulationWorld.Template.Cells[cellX, cellY];
                    if (!cell.Loaded)
                        continue; // ignore if not loaded
                    for (var inX = 0; inX < WorldManager.CELL_HMAP_RESOLUTION; inX++)
                    for (var inY = 0; inY < WorldManager.CELL_HMAP_RESOLUTION; inY++)
                    {
                        var x = cellX * WorldManager.CELL_HMAP_RESOLUTION + inX;
                        var y = cellY * WorldManager.CELL_HMAP_RESOLUTION + inY;
                        hmapTerrain[x, y] = cell.GetHeightMapDataInCell(x % WorldManager.CELL_HMAP_RESOLUTION,
                            y % WorldManager.CELL_HMAP_RESOLUTION);
                    }
                }

                if (AppConfiguration.Instance.World.PreLoadTerrain)
                    Logger.Debug($"Loading {SimulationWorld} heightmap data {cellCount / cellCountMax * 100f:F0}%");
            }

            var heightmap = new Heightmap(hmapTerrain);
            WorldHeightMapTester = new HeightmapTester(heightmap);
            _physWorld.BroadPhaseFilter = new HeightmapDetection(_physWorld, WorldHeightMapTester);
            _physWorld.DynamicTree.AddProxy(WorldHeightMapTester, false);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        Logger.Info($"{SimulationWorld.Template.Name} initialized terrain.");
    }

    /// <summary>
    /// Starts the Physics processing thread
    /// </summary>
    public void StartPhysics()
    {
        ThreadRunning = true;
        _thread = new Thread(PhysicsThread) { Name = "Physics-" + SimulationWorld };
        _thread.Start();
    }

    /// <summary>
    /// Handle physics loop
    /// </summary>
    private void PhysicsThread()
    {
        try
        {
            Logger.Debug($"Start: {Thread.CurrentThread.Name}, targetting {TargetPhysicsTps} TPS");

            var lastTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var accumulatedTime = TimeSpan.Zero;
            Thread.Sleep((int)TargetPhysicsTickTime);

            while (ThreadRunning)
            {
                var targetStepTime = TimeSpan.FromSeconds(TargetPhysicsTickTime);
                var currentTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var timeSinceLastTick = currentTick - lastTick;
                accumulatedTime += timeSinceLastTick;
                var timeToNextStep = lastTick + targetStepTime - currentTick;
                // Only sleep if needed, otherwise, directly continue
                if (timeToNextStep.TotalMilliseconds > 1)
                {
                    Thread.Sleep((int)timeToNextStep.TotalMilliseconds);
                }
                else
                if (timeToNextStep.TotalMilliseconds < -TargetPhysicsTps)
                {
                    // If it's taking more than double the expected time, toss a warning
                    Logger.Warn($"Physics thread is running slow in {SimulationWorld} at {timeSinceLastTick.TotalMilliseconds:F1} / {targetStepTime.TotalMilliseconds:F1} ms");
                }

                var physicsTotalDelta = TimeSpan.FromMilliseconds(Environment.TickCount64) - lastTick; 
                lastTick = currentTick;

                // 1. Process pending add/remove actions
                while (_pendingActions.TryDequeue(out var action)) { action(); }

                List<(RigidBody body, JVector vel, bool moving)> snapshot = [];

                lock (_worldLock)
                {

                    // 2. Take snapshot of bodies for state synchronization
                    foreach (var body in _bodies)
                    {
                        if (body == null) { continue; }

                        var vel = body.Velocity;
                        var moving = vel.LengthSquared() > 0.001f;
                        snapshot.Add((body, vel, moving));
                    }

                    // 3. Step the physics world
                    // Potentially step multiple times to catch up if we were running behind.
                    _physWorld.Step((float)physicsTotalDelta.TotalSeconds, false);

                    // 4. Sync positions and broadcast outside lock
                    // body, velocity, isMoving
                    foreach (var (body, _, _) in snapshot)
                    {
                        /*
                        if (body.Tag is Npc npc)
                        {
                            // Update transform
                            //UpdateNpcTransform(npc, velocity, isMoving);

                            // Update avoidance controller
                            //npc.AvoidanceController.Update(0.01f);
                        }
                        */

                        if (body.Tag is not Slave slave)
                            continue;

                        try
                        {
                            if (slave.Transform.WorldId != SimulationWorld.Id)
                                continue;

                            // Skip simulation if still summoning
                            if (slave.SpawnTime.AddSeconds(slave.Template.PortalTime) > DateTime.UtcNow)
                                continue;

                            // Skip simulation if no rigidbody applied to slave
                            if (!body.IsActive)
                                continue;

                            // TODO: move this
                            var underPos = slave.Transform.World.Position + Vector3.UnitZ * (slave.ShipController?.ShipModel.MassBoxSizeZ ?? 1f) / -2f * slave.Scale;
                            if (SimulationWorld.Water.IsWater(underPos, out var flowDirection))
                            {
                                if (flowDirection.Length() > 0f)
                                {
                                    // We are in moving water, apply force
                                    // var multiplier = slave.RigidBody.Mass / TargetPhysicsTickTime;
                                    // slave.RigidBody.AddForce(new JVector(flowDirection.X * multiplier, flowDirection.Z * multiplier, flowDirection.Y * multiplier));
                                    slave.RigidBody.Position += new JVector(flowDirection.X * (float)physicsTotalDelta.TotalSeconds,flowDirection.Z * (float)physicsTotalDelta.TotalSeconds, flowDirection.Y * (float)physicsTotalDelta.TotalSeconds);
                                }
                            }

                            if (_shipControllers.TryGetValue(slave.Id, out var boat))
                            {
                                // Create floor/surface cache
                                slave.CreateWaterAndLandSurfaceCache();
                                // Sync transform
                                SyncTransformWithRigidBody(slave);
                                // Do physics tick
                                BoatPhysicsTick(slave, physicsTotalDelta);
                                // Check if we collided
                                CheckLandCollisions(slave, physicsTotalDelta);
                                slave.DoFloorCollisionDamage(physicsTotalDelta);
                                // Update Controls
                                boat.ApplyForceAndTorque(slave, physicsTotalDelta);
                                SendUpdatedMovementData(slave, slave.RigidBody, physicsTotalDelta);
                            }
                        }
                        catch (Exception slaveException)
                        {
                            // Put a separate catch here to catch individual errors without it breaking all the physics in this world 
                            Logger.Error($"PhysicsThread Error on Slave {slave.Id} {slave.Name} ({slave.ObjId}): {slaveException.Message}\n{slaveException.StackTrace}");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"PhysicsThread Error: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            Logger.Debug($"PhysicsThread End: {Thread.CurrentThread.Name}");
        }
    }

    /// <summary>
    /// Copies physics engine's positions back to game server's positions
    /// </summary>
    /// <param name="slave"></param>
    private void SyncTransformWithRigidBody(Slave slave)
    {
        var slaveRigidBody = slave.RigidBody;
        var xDelta = slaveRigidBody.Position.X - slave.Transform.World.Position.X;
        var yDelta = slaveRigidBody.Position.Z - slave.Transform.World.Position.Y;
        var zDelta = slaveRigidBody.Position.Y - slave.Transform.World.Position.Z;
        //if (zDelta < -3)
        //{
        //    slaveRigidBody.Position = slaveRigidBody.Position with { Y = slave.Transform.World.Position.Z };
        //    zDelta = 0;
        //    Logger.Info($"SyncTransformWithRigidBody {slave.Name} -> {SimulationWorld.Name}, _waterLevel={DefaultWaterLevel}, OceanLevel={SimulationWorld.OceanLevel}, slave.Position.Z={slave.Transform.World.Position.Z}");
        //}

        slave.Transform.Local.Translate(xDelta, yDelta, zDelta);
        var rotation = slaveRigidBody.Orientation;
        slave.Transform.Local.ApplyFromQuaternion(rotation.X, rotation.Z, rotation.Y, rotation.W);
    }

    /// <summary>
    /// Adds a ship to physics engine
    /// </summary>
    /// <param name="slave"></param>
    public void AddShip(Slave slave)
    {
        var shipModel = ModelManager.Instance.GetShipModel(slave.ModelId);
        if (shipModel == null || shipModel.Mass <= 0)
        {
            Logger.Error($"Invalid ship model for slave {slave.Name}");
            return;
        }

        var pos = new JVector(slave.Transform.World.Position.X, slave.Transform.World.Position.Z, slave.Transform.World.Position.Y);

        // When a ship is summoned, buoyancy/gravity is disabled until PortalTime ends.
        // If the spawn point height is below the water surface, the ship will appear heavily submerged
        // and then "pop" up once buoyancy kicks in. Clamp the initial physics height closer to the waterline.
        try
        {
            var waterSurface = SimulationWorld.Water.GetWaterSurface(slave.Transform.World.Position, out _);
            if (waterSurface > 0f)
            {
                var hullHeight = (slave.ShipController?.ShipModel.MassBoxSizeZ ?? shipModel.MassBoxSizeZ) * slave.Scale;
                // Keep the ship close to the surface at spawn; buoyancy will settle the final draft.
                var minCenterY = waterSurface - hullHeight * 0.02f;
                if (pos.Y < minCenterY)
                    pos.Y = minCenterY;
            }
        }
        catch
        {
            // If water query fails, keep original spawn height.
        }
        var rot = JQuaternion.CreateRotationY(slave.Transform.World.Rotation.Z);
        //                                     Width                   Length                  Height
        // var dimensions = new JVector(shipModel.MassBoxSizeX, shipModel.MassBoxSizeY, shipModel.MassBoxSizeZ);
        var ctrl = new ShipController(_physWorld, shipModel, waterLevel: DefaultWaterLevel);

        ctrl.Build(initialPosition: pos, initialOrientation: rot);

        _shipControllers[slave.Id] = ctrl;
        slave.RigidBody = ctrl.Hull;
        slave.RigidBody.Tag = slave;
        slave.ShipController = ctrl;

        // During PortalTime the physics thread skips ship processing (including transform sync),
        // so ensure the initial server-side Transform matches the physics spawn position.
        SyncTransformWithRigidBody(slave);
        slave.Transform.FinalizeTransform();

        EnqueueAddBody(slave.RigidBody);
        _buoyancy.AddForRectangularParallelepiped(slave.RigidBody, 3);

        Logger.Debug($"AddShip {slave.Name} -> {SimulationWorld.Template.Name}");
    }

    /// <summary>
    /// Removes a ship from the physics engine
    /// </summary>
    /// <param name="slave"></param>
    public void RemoveShip(Slave slave)
    {
        if (slave.RigidBody == null) return;

        var rigidBody = slave.RigidBody;
        rigidBody.SetActivationState(false);
        EnqueueRemoveBody(rigidBody);
        _physWorld.Remove(rigidBody);
        _buoyancy.Remove(rigidBody);
        slave.RigidBody = null;

        Logger.Debug($"RemoveShip {slave.Name} <- {SimulationWorld.Template.Name}");
    }

    /// <summary>
    /// Handles physics tick for a ship 
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="deltaTime"></param>
    private void BoatPhysicsTick(Slave slave, TimeSpan deltaTime)
    {
        var shipModel = slave.ShipController?.ShipModel;
        if (shipModel == null) return;

        // Calculate submerged depth and buoyancy force
        var submergedDepth = Math.Max(0, slave.CachedWaterSurface - slave.RigidBody.Position.Y);
        var isOnWater = submergedDepth > 0;
        var isOnLand = !isOnWater && submergedDepth <= 0;

        if (isOnLand)
        {
            // Apply ground friction and stop the ship
            const float GroundFriction = 0.4f; // Sand: around 0.4
            var frictionForce = new JVector(-slave.RigidBody.Velocity.X * GroundFriction, 0, -slave.RigidBody.Velocity.Z * GroundFriction);
            slave.RigidBody.AddForce(frictionForce);

            // Gradually reduce speed
            const float CollisionDamping = 0.5f;
            slave.RigidBody.Velocity *= CollisionDamping;
            slave.RigidBody.AngularVelocity *= CollisionDamping;

            // Stop the ship and apply roll
            if (slave.RigidBody.Velocity.Length() < 0.01f)
            {
                slave.RigidBody.Velocity = JVector.Zero;
                slave.RigidBody.AngularVelocity = JVector.Zero;

                // Apply roll to the ship
                var rollAngle = GetRollAngle(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation));
                if (Math.Abs(rollAngle) < 0.1f)
                {
                    var correctionTorque = new JVector(0, 0, -rollAngle * slave.RigidBody.Mass * 0.1f);
                    slave.RigidBody.AddForce(correctionTorque);
                }

                // Disable control
                slave.ThrottleRequest = 0;
                slave.SteeringRequest = 0;
                slave.Throttle = 0;
                slave.Steering = 0;
                slave.ThrottleSmoothed = 0f;
                slave.SteeringSmoothed = 0f;
            }
        }

        // Check if the ship has a driver
        var hasDriver = slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver);
        if (hasDriver)
        {
            // Smooth toward client input in float space, then round — avoids sbyte stair-stepping on rudder animation.
            const float SmoothingFactor = 0.12f;
            slave.ThrottleSmoothed += (slave.ThrottleRequest - slave.ThrottleSmoothed) * SmoothingFactor;
            slave.SteeringSmoothed += (slave.SteeringRequest - slave.SteeringSmoothed) * SmoothingFactor;
            slave.Throttle = (sbyte)Math.Clamp((int)Math.Round(slave.ThrottleSmoothed), -128, 127);
            slave.Steering = (sbyte)Math.Clamp((int)Math.Round(slave.SteeringSmoothed), -128, 127);
        }
        else
        {
            // If there is no driver, we reset the control
            slave.ThrottleRequest = 0;
            slave.SteeringRequest = 0;
            slave.Throttle = 0;
            slave.Steering = 0;
            slave.ThrottleSmoothed = 0f;
            slave.SteeringSmoothed = 0f;
        }
    }

    /// <summary>
    /// Update ship's movement data and broadcasts it 
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="rigidBody"></param>
    private void SendUpdatedMovementData(Slave slave, RigidBody rigidBody, TimeSpan deltaTime)
    {
        var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
        moveType.UseSlaveBase(slave);

        // Get current rotation of the ship
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));

        // Visual-only bank (ship leans into turns). Applied to replicated rotation, not physics.
        // Coordinate mapping is legacy: GetSlaveRotationFromDegrees reorders axes, so injecting into rpy.Item2 affects client-side roll.
        var maxBankDeg = slave.Template.SlaveKind switch
        {
            SlaveKind.BigSailingShip => 10.0f,
            SlaveKind.SmallSailingShip => 10.0f,
            SlaveKind.Speedboat => 8.0f,
            SlaveKind.Fishboat => 8.0f,
            SlaveKind.MerchantShip => 8.0f,
            SlaveKind.Leviathan => 8.0f,
            SlaveKind.Boat => 8.0f,
            _ => 0f
        };
        const float bankResponse = 7.5f; // higher = snappier
        var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
        var maxBankRad = maxBankDeg.DegToRad();
        var yawRate = rigidBody.AngularVelocity.Y; // rad/s (see ShipController)
        var horizSpeed = MathF.Sqrt(
            rigidBody.Velocity.X * rigidBody.Velocity.X +
            rigidBody.Velocity.Z * rigidBody.Velocity.Z);
        var speedFactor = Math.Clamp(horizSpeed / 2.5f, 0f, 1f);
        var targetBank = Math.Clamp(-yawRate * 0.9f, -maxBankRad, maxBankRad) * speedFactor;
        var a = 1f - MathF.Exp(-bankResponse * dt);
        slave.BankAngle += (targetBank - slave.BankAngle) * a;

        // Visual-only pitch on shoal/ground: align nose/stern to local terrain slope.
        // This does not affect rigidbody Y/physics, only replicated rotation.
        const float groundPitchMaxDeg = 8.0f;
        const float groundPitchProbeDistance = 6.0f;
        const float groundPitchResponse = 2.0f; // smoother to avoid pitch jitter
        var targetGroundPitch = 0f;
        if (slave.CachedFloorLevel > slave.CachedWaterSurface || slave.GroundContactLatched)
        {
            var yaw = rpy.Item1 + 1.57f; // bow heading in world X/Y plane
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

            // Smooth sampled heights to avoid geo noise causing visual pitch jitter.
            const float pitchFloorSmoothResponse = 8.0f;
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

            // When beaching in reverse (stern on ground), invert pitch so the stern rises (not the bow).
            if (slave.GroundedByStern)
                targetGroundPitch = -targetGroundPitch;
        }
        else
            slave.GroundPitchFloorSmoothingSeeded = false;

        var pitchA = 1f - MathF.Exp(-groundPitchResponse * dt);
        slave.GroundPitchAngle += (targetGroundPitch - slave.GroundPitchAngle) * pitchA;

        var bankedRpy = (rpy.Item1, rpy.Item2 + slave.BankAngle, rpy.Item3 + slave.GroundPitchAngle);

        // Insert new Rotation data into MoveType
        var (rotZ, rotY, rotX) = MathUtil.GetSlaveRotationFromDegrees(bankedRpy.Item1, bankedRpy.Item2, bankedRpy.Item3);
        moveType.RotationX = rotX;
        moveType.RotationY = rotY;
        moveType.RotationZ = rotZ;

        // Fill in the Velocity Data into the MoveType.
        // moveType.Velocity = new Vector3(rigidBody.Velocity.X, rigidBody.Velocity.Z, rigidBody.Velocity.Y);
        moveType.AngVelX = rigidBody.AngularVelocity.X;
        moveType.AngVelY = rigidBody.AngularVelocity.Z;
        moveType.AngVelZ = rigidBody.AngularVelocity.Y;

        // Seems display the correct speed this way, but what happens if you go over the bounds ?
        var velMultiplier = 2048; // 1024;
        moveType.VelX = (short)(rigidBody.Velocity.X * velMultiplier);
        moveType.VelY = (short)(rigidBody.Velocity.Z * velMultiplier);
        moveType.VelZ = (short)(rigidBody.Velocity.Y * velMultiplier);

        // Do not allow the body to flip
        //slave.RigidBody.Orientation = JMatrix.CreateFromYawPitchRoll(rpy.Item1, 0, 0); // TODO: Fix me with proper physics

        // Apply new Location/Rotation to GameObject
        slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
        slave.Transform.Local.ApplyFromQuaternion(rigidBody.Orientation);
        slave.Transform.Local.SetRotation(
            slave.Transform.Local.Rotation.X,
            slave.Transform.Local.Rotation.Y + slave.BankAngle,
            slave.Transform.Local.Rotation.Z + slave.GroundPitchAngle);

        // Send the packet
        slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);

        // Update all to main Slave and it's children
        slave.Transform.FinalizeTransform();
    }

    /// <summary>
    /// Apply land collision between the ship and the expected terrain
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="deltaTime"></param>
    private void CheckLandCollisions(Slave slave, TimeSpan deltaTime)
    {
        if (slave.ShipController?.ShipModel is null)
            return;

        // Terrain contact is evaluated at a probe point in front of bow / behind stern (not hull center).
        // Requested tuning: probe is placed at 2x hull length from center.
        var boatBottom = slave.RigidBody.Position.Y;

        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(slave.RigidBody.Orientation));
        var heading = rpy.Item1 + 1.57f;
        var dirX = MathF.Cos(heading);
        var dirZ = MathF.Sin(heading);

        var vX = slave.RigidBody.Velocity.X;
        var vZ = slave.RigidBody.Velocity.Z;
        var along = vX * dirX + vZ * dirZ; // >0 forward, <0 backward
        var movingBackward = along < -0.05f || (MathF.Abs(along) <= 0.05f && slave.ThrottleRequest < 0);

        const float BowProbeMul = 1.5f;
        const float SternProbeMul = 1.5f;
        // IMPORTANT: once we latched ground contact, keep the same hull-end selection stable.
        // Otherwise, releasing reverse at standstill can flip the probe from stern->bow,
        // making contactFloor drop and causing GroundContactLatched + visual pitch to flicker.
        var useSternProbe = slave.GroundContactLatched ? slave.GroundedByStern : movingBackward;
        var probeMul = useSternProbe ? SternProbeMul : BowProbeMul;
        var probeDist = MathF.Max(1.0f, slave.ShipController.ShipModel.MassBoxSizeX * probeMul * slave.Scale);
        var probeSign = useSternProbe ? -1f : 1f; // stern / bow
        var probeX = slave.RigidBody.Position.X + dirX * probeDist * probeSign;
        var probeY = slave.RigidBody.Position.Z + dirZ * probeDist * probeSign;
        var contactFloor = slave.ParentWorld.GetHeight(probeX, probeY);

        // Simple "cliff collision" rule:
        // look ahead/behind at 2x hull length; if slope is steeper than 45% treat as a barrier.
        // "Wall / cliff" detection probe distance (independent from beach contact probe).
        const float CliffProbeMul = 1.45f;
        // ~30 degrees slope threshold (rounded): 57%
        const float CliffSlopeFracThreshold = 0.57f;
        var cliffDist = MathF.Max(1.0f, slave.ShipController.ShipModel.MassBoxSizeX * CliffProbeMul * slave.Scale);
        var cliffX = slave.RigidBody.Position.X + dirX * cliffDist * probeSign;
        var cliffY = slave.RigidBody.Position.Z + dirZ * cliffDist * probeSign;
        var cliffFloor = slave.ParentWorld.GetHeight(cliffX, cliffY);
        // Prevent underwater terrain ("sea floor") from being treated as a wall:
        // only run cliff-barrier logic when the probe sample is at/above water; otherwise fall through to beaching.
        const float CliffAboveWaterMargin = 0.20f;
        if (cliffFloor > slave.CachedWaterSurface + CliffAboveWaterMargin)
        {
            var dh = cliffFloor - slave.CachedFloorLevel;
            var slopeFrac = dh / MathF.Max(0.01f, cliffDist);
            if (slopeFrac > CliffSlopeFracThreshold)
            {
                // Collision: remove the component of horizontal velocity pushing into the cliff.
                var v = slave.RigidBody.Velocity;
                var vAlong = v.X * dirX + v.Z * dirZ;
                var pushingIntoBarrier = useSternProbe ? (vAlong < 0f) : (vAlong > 0f);
                if (pushingIntoBarrier)
                {
                    // Stop the "poke": ShipController will otherwise re-apply forward/back velocity next tick from slave.Speed.
                    slave.Speed = 0f;
                    var newVX = v.X - vAlong * dirX;
                    var newVZ = v.Z - vAlong * dirZ;
                    slave.RigidBody.Velocity = new JVector(newVX * 0.85f, 0f, newVZ * 0.85f);
                    slave.RigidBody.AngularVelocity *= 0.85f;
                }

                // Push out of the barrier so we don't clip into wall textures.
                // Single small push opposite to the barrier-facing direction (no loops -> no jitter).
                var pushDirSign = useSternProbe ? 1f : -1f;
                var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
                var pushStep = MathF.Min(0.50f, MathF.Max(0.08f, MathF.Abs(vAlong) * dt * 1.10f));
                slave.RigidBody.Position += new JVector(dirX * pushStep * pushDirSign, 0f, dirZ * pushStep * pushDirSign);

                return;
            }
        }

        // Latch ground contact with enter/exit hysteresis to avoid rapid toggling (visual jitter).
        const float ShoreEnterHyst = 0.35f;
        const float ShoreExitHyst = 0.10f;

        // Smooth contact floor itself (geo height noise + probe jitter).
        const float FloorSmoothResponse = 10.0f;
        {
            var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
            var a = 1f - MathF.Exp(-FloorSmoothResponse * dt);
            if (!slave.GroundContactLatched && !slave.GroundContactFloorSmoothingSeeded)
            {
                slave.GroundContactFloorSmoothed = contactFloor;
                slave.GroundContactFloorSmoothingSeeded = true;
            }
            else
                slave.GroundContactFloorSmoothed += (contactFloor - slave.GroundContactFloorSmoothed) * a;
        }
        var floorSmoothed = slave.GroundContactFloorSmoothed;

        // Pre-shore band: damp vertical bobbing right before latch triggers.
        // This targets the last few "bumps" while approaching the shoreline.
        const float PreShoreBand = 0.25f;
        var enterDelta = (slave.CachedWaterSurface + ShoreEnterHyst) - floorSmoothed; // >=0 means "still water side"
        if (!slave.GroundContactLatched && enterDelta >= 0f && enterDelta <= PreShoreBand)
        {
            var v = slave.RigidBody.Velocity;
            // Fade Y velocity to zero as we approach latch point.
            var t = 1f - (enterDelta / PreShoreBand); // 0..1
            var damp = 1f - 0.85f * t;
            slave.RigidBody.Velocity = new JVector(v.X, v.Y * damp, v.Z);
        }

        if (!slave.GroundContactLatched)
        {
            if (slave.CachedWaterSurface + ShoreEnterHyst >= floorSmoothed)
                return;
            slave.GroundContactLatched = true;
            slave.GroundContactLatchedTime = 0f;
        }
        else
        {
            // Release latch only when we are clearly back on water side.
            if (slave.CachedWaterSurface + ShoreExitHyst >= floorSmoothed)
            {
                slave.GroundContactLatched = false;
                slave.GroundContactLatchedTime = 0f;
                return;
            }
        }

        // Remove vertical bobbing near shoreline / on ground contact.
        // This targets the remaining "bumpy" height jerks right before beaching.
        {
            var v = slave.RigidBody.Velocity;
            if (MathF.Abs(v.Y) > 0.01f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        slave.GroundContactLatchedTime += Math.Max(0f, (float)deltaTime.TotalSeconds);

        var penetration = floorSmoothed - boatBottom;
        if (penetration <= 0.0f)
            return;

        // Smooth/clamp the vertical correction to prevent shaking on small height changes.
        const float PenetrationEpsilon = 0.02f;
        const float PenetrationResponse = 4.5f; // even smoother vertical correction
        var maxUpStepPerTick = slave.GroundContactLatchedTime < 0.30f ? 0.04f : 0.07f;
        if (penetration > PenetrationEpsilon)
        {
            var dt = Math.Max(0.0001f, (float)deltaTime.TotalSeconds);
            var a = 1f - MathF.Exp(-PenetrationResponse * dt);
            var step = MathF.Min(penetration * a, maxUpStepPerTick);
            slave.RigidBody.Position += new JVector(0, step, 0); // lift toward terrain smoothly

            // Kill vertical bounce when we manually resolve penetration (prevents small height jerks).
            var v = slave.RigidBody.Velocity;
            if (MathF.Abs(v.Y) > 0.01f)
                slave.RigidBody.Velocity = new JVector(v.X, 0f, v.Z);
        }
        var collisionForce = _physWorld.Gravity * -1f;
        slave.RigidBody.AddForce(collisionForce);

        // Gradually reduce speed.
        // Keep much more momentum for valid escape direction; otherwise the ship can get stuck jittering.
        var escapeThrottleSign = slave.GroundedByStern ? 1 : -1;
        var isEscapeThrottle = slave.ThrottleRequest != 0 && Math.Sign(slave.ThrottleRequest) == escapeThrottleSign;
        // Less aggressive damping on initial shoreline contact to preserve inertia.
        // Only apply strong damping once penetration is clearly "on land".
        var deepContact = penetration > 0.25f;
        var collisionDamping = isEscapeThrottle ? 0.99f : (deepContact ? 0.88f : 0.95f);
        slave.RigidBody.Velocity *= collisionDamping;
        slave.RigidBody.AngularVelocity *= collisionDamping;

        // Logger.Debug($"Land Collision detected. Boat adjusted position: {slave.RigidBody.Position}, boat penetration depth: {penetration}");
    }

    /// <summary>
    /// Stops the physics engine from running its update loop
    /// </summary>
    public void Stop()
    {
        ThreadRunning = false;
    }

    public void Dispose() => _physWorld?.Dispose();

    /// <summary>
    /// Helper function to check water bodies
    /// </summary>
    /// <param name="area"></param>
    /// <returns></returns>
    internal bool CustomWater(ref JVector area)
    {
        return SimulationWorld?.IsWater(new Vector3(area.X, area.Z, area.Y), out _) ?? area.Y <= (SimulationWorld?.Template.OceanLevel ?? DefaultWaterLevel);
    }

    /// <summary>
    /// Enqueues an NPC body to be added in the next physics step.
    /// </summary>
    private void EnqueueAddBody(RigidBody body)
    {
        if (body == null) return;
        _pendingActions.Enqueue(() =>
        {
            _bodies.Add(body);
        });
    }

    /// <summary>
    /// Enqueues an NPC body to be removed in the next physics step.
    /// </summary>
    private void EnqueueRemoveBody(RigidBody body)
    {
        if (body == null) return;
        _pendingActions.Enqueue(() =>
        {
            _bodies.Remove(body);
        });
    }

    /// <summary>
    /// Gets game angle Roll from physics engine JMatrix
    /// </summary>
    /// <param name="orientation"></param>
    /// <returns></returns>
    internal static float GetRollAngle(JMatrix orientation)
    {
        var yawPitchRoll = GetYawPitchRollFromJMatrix(orientation);
        return yawPitchRoll.Item2; // Roll angle in radians
    }

    /// <summary>
    /// Gets angle YPR from physics engine JMatrix
    /// </summary>
    /// <param name="mat"></param>
    /// <returns></returns>
    internal static (float, float, float) GetYawPitchRollFromJMatrix(JMatrix mat)
    {
        return MathUtil.GetYawPitchRollFromQuat(JMatrixToQuaternion(mat));
    }

    /// <summary>
    /// Convert JMatrix to game Quaternion 
    /// </summary>
    /// <param name="matrix"></param>
    /// <returns></returns>
    internal static Quaternion JMatrixToQuaternion(JMatrix matrix)
    {
        var jq = JQuaternion.CreateFromMatrix(matrix);

        return new Quaternion
        {
            X = jq.X,
            Y = jq.Y,
            Z = jq.Z,
            W = jq.W
        };
    }

    /// <summary>
    /// Updates heightmap data with the data from the provided WorldCell
    /// </summary>
    /// <param name="cell"></param>
    public void UpdateHeightMapFromCellBody(WorldCell cell)
    {
        if (WorldHeightMapTester == null)
        {
            return;
        }

        // Copy over cell's data
        for (var inX = 0; inX < WorldManager.CELL_HMAP_RESOLUTION; inX++)
        {
            for (var inY = 0; inY < WorldManager.CELL_HMAP_RESOLUTION; inY++)
            {
                var x = cell.CellX * WorldManager.CELL_HMAP_RESOLUTION + inX;
                var y = cell.CellY * WorldManager.CELL_HMAP_RESOLUTION + inY;
                WorldHeightMapTester.Heightmap.RawHeights[x, y] = cell.GetHeightMapDataInCell(inX, inY);
            }
        }
        Logger.Trace($"Post-Loaded {SimulationWorld} Cell {cell.CellX}, {cell.CellY}");
    }
}
