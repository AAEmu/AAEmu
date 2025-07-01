using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
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
                        var x = (cellX * WorldManager.CELL_HMAP_RESOLUTION) + inX;
                        var y = (cellY * WorldManager.CELL_HMAP_RESOLUTION) + inY;
                        hmapTerrain[x, y] = cell.GetHeightMapDataInCell(x % WorldManager.CELL_HMAP_RESOLUTION,
                            y % WorldManager.CELL_HMAP_RESOLUTION);
                    }
                }

                if (AppConfiguration.Instance.World.PreLoadTerrain)
                    Logger.Debug($"Loading {SimulationWorld} heightmap data {(cellCount / cellCountMax * 100f):F0}%");
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

        Logger.Info($"{SimulationWorld.Template.Name} initialized Terrain.");
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
                            var underPos = slave.Transform.World.Position + ((Vector3.UnitZ * (slave.ShipController?.ShipModel.MassBoxSizeZ ?? 1f) / -2f) * slave.Scale);
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
                                // Update Controls
                                boat.ApplyForceAndTorque(slave, physicsTotalDelta);
                                SendUpdatedMovementData(slave, slave.RigidBody);
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
        var rot = JQuaternion.CreateRotationY(slave.Transform.World.Rotation.Z);
        //                                     Width                   Length                  Height
        // var dimensions = new JVector(shipModel.MassBoxSizeX, shipModel.MassBoxSizeY, shipModel.MassBoxSizeZ);
        var ctrl = new ShipController(_physWorld, shipModel, waterLevel: DefaultWaterLevel);

        ctrl.Build(initialPosition: pos, initialOrientation: rot);

        _shipControllers[slave.Id] = ctrl;
        slave.RigidBody = ctrl.Hull;
        slave.RigidBody.Tag = slave;
        slave.ShipController = ctrl;

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
            }
        }

        // Check if the ship has a driver
        var hasDriver = slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver);
        if (hasDriver)
        {
            // If there is a driver, we update the control
            // Smooth throttle and steering inputs
            const float SmoothingFactor = 0.1f;
            slave.Throttle = (sbyte)(slave.Throttle + (slave.ThrottleRequest - slave.Throttle) * SmoothingFactor);
            slave.Steering = (sbyte)(slave.Steering + (slave.SteeringRequest - slave.Steering) * SmoothingFactor);
        }
        else
        {
            // If there is no driver, we reset the control
            slave.ThrottleRequest = 0;
            slave.SteeringRequest = 0;
            slave.Throttle = 0;
            slave.Steering = 0;
        }
    }

    /// <summary>
    /// Update ship's movement data and broadcasts it 
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="rigidBody"></param>
    private void SendUpdatedMovementData(Slave slave, RigidBody rigidBody)
    {
        var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
        moveType.UseSlaveBase(slave);

        // Get current rotation of the ship
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(rigidBody.Orientation));
        // Insert new Rotation data into MoveType
        var (rotZ, rotY, rotX) = MathUtil.GetSlaveRotationFromDegrees(rpy.Item1, rpy.Item2, rpy.Item3);
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

        var boatBottom = slave.RigidBody.Position.Y;
        //Logger.Debug($"Slave: {slave.Name}, floor: {floor:F1}, boatBottom: {boatBottom:F1}, boxSize: {boxSize}");

        if (slave.CachedWaterSurface >= slave.CachedFloorLevel)
        {
            return;
        }

        var penetration = slave.CachedFloorLevel - boatBottom;
        slave.RigidBody.Position += new JVector(0, penetration, 0); // Move the boat upwards to put the center level with the floor
        var collisionForce = _physWorld.Gravity * -1f;
        slave.RigidBody.AddForce(collisionForce);

        // Gradually reduce speed
        var collisionDamping = 0.9f;
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

        return new Quaternion()
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
                var x = (cell.CellX * WorldManager.CELL_HMAP_RESOLUTION) + inX;
                var y = (cell.CellY * WorldManager.CELL_HMAP_RESOLUTION) + inY;
                WorldHeightMapTester.Heightmap.RawHeights[x, y] = cell.GetHeightMapDataInCell(inX, inY);
            }
        }
        Logger.Trace($"Post-Loaded {SimulationWorld} Cell {cell.CellX}, {cell.CellY}");
    }
}
