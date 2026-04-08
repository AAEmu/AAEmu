using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Debug;
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
    private readonly ShipShoreInteraction _shipShore = new();
    private readonly ShipShipInteraction _shipShip = new();
    private readonly ShipDoodadInteraction _shipDoodad = new();
    private readonly ShipStaticBarrierInteraction _shipStaticBarriers = new();
    private readonly ShipCliffInteraction _shipCliff = new();

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
                    var shipsThisTick = new List<Slave>();
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

                            // Single dictionary lookup: ShipController matches _shipControllers[slave.Id] (set together in AddShip).
                            if (_shipControllers.TryGetValue(slave.Id, out _))
                            {
                                // Create floor/surface cache
                                slave.CreateWaterAndLandSurfaceCache();
                                // Sync transform
                                SyncTransformWithRigidBody(slave);
                                // Do physics tick
                                BoatPhysicsTick(slave, physicsTotalDelta);
                                _shipShore.ResolveTerrainContacts(slave, physicsTotalDelta, _physWorld);
                                shipsThisTick.Add(slave);
                            }
                        }
                        catch (Exception slaveException)
                        {
                            // Put a separate catch here to catch individual errors without it breaking all the physics in this world 
                            Logger.Error($"PhysicsThread Error on Slave {slave.Id} {slave.Name} ({slave.ObjId}): {slaveException.Message}\n{slaveException.StackTrace}");
                        }
                    }

                    foreach (var slave in shipsThisTick)
                    {
                        try
                        {
                            slave.ShipController?.ApplyForceAndTorque(slave, physicsTotalDelta);
                        }
                        catch (Exception slaveException)
                        {
                            Logger.Error($"PhysicsThread Error on Slave {slave.Id} {slave.Name} ({slave.ObjId}): {slaveException.Message}\n{slaveException.StackTrace}");
                        }
                    }

                    try
                    {
                        _shipShip.ResolveAllPairs(shipsThisTick, physicsTotalDelta);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PhysicsThread ship-ship resolve: {e.Message}\n{e.StackTrace}");
                    }

                    try
                    {
                        foreach (var slave in shipsThisTick)
                            slave.StaticObstacleHullDamageContactActive = false;

                        _shipDoodad.ResolveAll(SimulationWorld, shipsThisTick, physicsTotalDelta);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PhysicsThread ship-doodad resolve: {e.Message}\n{e.StackTrace}");
                    }

                    try
                    {
                        if (AppConfiguration.Instance.World.GeoDataMode && SimulationWorld.ShipStaticBarriers != null)
                        {
                            foreach (var slave in shipsThisTick)
                            {
                                if (slave.ParentWorld?.Id != SimulationWorld.Id || slave.RigidBody is null)
                                    continue;
                                var p = slave.Transform.World.Position;
                                var (cellX, cellY) = p.ToCellIndex();
                                ShipStaticBarrierBaiIngestor.EnsureCell(SimulationWorld, cellX, cellY);
                            }

                            _shipStaticBarriers.ResolveAll(SimulationWorld, shipsThisTick, physicsTotalDelta);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PhysicsThread ship-static-barrier resolve: {e.Message}\n{e.StackTrace}");
                    }

                    try
                    {
                        _shipCliff.ResolveAll(SimulationWorld, shipsThisTick, physicsTotalDelta);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"PhysicsThread ship-cliff resolve: {e.Message}\n{e.StackTrace}");
                    }

                    foreach (var slave in shipsThisTick)
                    {
                        try
                        {
                            slave.TickStaticObstacleHullDamage(physicsTotalDelta);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"PhysicsThread static-obstacle hull damage: {ex.Message}\n{ex.StackTrace}");
                        }
                    }

                    foreach (var slave in shipsThisTick)
                    {
                        try
                        {
                            ShipTuningDebug.TickShip(slave);
                        }
                        catch (Exception ex)
                        {
                            // Debug-only; must never affect physics loop — log for diagnostics only.
                            Logger.Debug(ex, $"ShipTuningDebug.TickShip failed for {slave.Name} ({slave.ObjId})");
                        }
                    }

                    foreach (var slave in shipsThisTick)
                    {
                        try
                        {
                            SendUpdatedMovementData(slave, slave.RigidBody, physicsTotalDelta);
                        }
                        catch (Exception slaveException)
                        {
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
        var ctrl = new ShipController(_physWorld, shipModel);

        ctrl.Build(initialPosition: pos, initialOrientation: rot);

        _shipControllers[slave.Id] = ctrl;
        slave.RigidBody = ctrl.Hull;
        slave.RigidBody.Tag = slave;
        slave.ShipController = ctrl;

        // During PortalTime the physics thread skips ship processing (including transform sync),
        // so ensure the initial server-side Transform matches the physics spawn position.
        SyncTransformWithRigidBody(slave);
        slave.Transform.FinalizeTransform();
        ctrl.Replication.Reset();
        slave.WavePitchPhase = 0f;
        slave.ShipHullCollisionDamageCooldownByOtherShipId.Clear();
        slave.StaticObstacleHullDamageContactActive = false;
        slave.StaticObstacleHullDamageSecondsAccumulator = 0f;
        slave.StaticObstacleHullDamageNoContactSeconds = 0f;

        EnqueueAddBody(slave.RigidBody);
        _buoyancy.AddForRectangularParallelepiped(slave.RigidBody, 3);

        Logger.Debug($"AddShip {slave.Name} -> {SimulationWorld.Template.Name}");
    }

    /// <summary>
    /// Removes a ship from the physics engine.
    /// Jitter2 <see cref="Jitter2.World"/> is not thread-safe: all <c>_physWorld</c> / <c>_buoyancy</c> mutations
    /// run on the physics thread (same queue as <see cref="_pendingActions"/>, processed before <c>Step</c>).
    /// </summary>
    /// <param name="slave"></param>
    public void RemoveShip(Slave slave)
    {
        if (slave.RigidBody == null)
            return;

        var rigidBody = slave.RigidBody;
        var slaveId = slave.Id;
        var slaveRef = slave;

        void RemoveFromPhysicsThread()
        {
            // Second queued removal (same body): first lambda already nulled RigidBody.
            if (slaveRef.RigidBody != rigidBody)
                return;

            rigidBody.SetActivationState(false);
            _physWorld.Remove(rigidBody);
            _buoyancy.Remove(rigidBody);
            _bodies.Remove(rigidBody);
            _shipControllers.Remove(slaveId, out _);

            ShipTuningDebug.DespawnAll(slaveId);

            slaveRef.RigidBody = null;
            slaveRef.ShipController = null;
        }

        if (!ThreadRunning)
        {
            RemoveFromPhysicsThread();
        }
        else
        {
            _pendingActions.Enqueue(RemoveFromPhysicsThread);
        }

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

        _shipShore.ApplyOnLandPhysics(slave, deltaTime);

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
    /// Waterline length = max(X,Y), beam = min(X,Y), height = Z, mass from <c>ship_models</c> (same convention as bank).
    /// </summary>
    private static bool TryGetShipMassBoxWaterlineExtents(ShipModelV1 model, float scale,
        out float length, out float beam, out float height, out float mass)
    {
        if (model == null)
        {
            length = beam = height = mass = 0f;
            return false;
        }

        var s = MathF.Max(scale, 0.01f);
        var hx = model.MassBoxSizeX * s;
        var hy = model.MassBoxSizeY * s;
        length = MathF.Max(MathF.Max(hx, hy), 0.25f);
        beam = MathF.Max(MathF.Min(hx, hy), 0.25f);
        height = MathF.Max(model.MassBoxSizeZ * s, 0.15f);
        mass = MathF.Max(model.Mass, 10f);
        return true;
    }

    /// <summary>
    /// Visual wave pitch amplitude (rad) and angular frequency from hull length/mass; matches bank reference scale.
    /// Longer/heavier → smaller amp, slightly slower cycle; short/light dinghies stay closer to legacy ±3° / 0.06 Hz.
    /// </summary>
    private static void GetVisualWavePitchModelFactors(ShipModelV1 model, float scale, out float maxAmpRad, out float omega)
    {
        const float baseDeg = 3f;
        const float baseHz = 0.06f;
        if (!TryGetShipMassBoxWaterlineExtents(model, scale, out var length, out _, out _, out var mass))
        {
            maxAmpRad = baseDeg.DegToRad();
            omega = 2f * MathF.PI * baseHz;
            return;
        }

        const float refLength = 14f;
        const float refMass = 85000f;

        var lenRatio = Math.Clamp(refLength / length, 0.35f, 2.5f);
        var massRatio = Math.Clamp(MathF.Sqrt(refMass / mass), 0.45f, 2.2f);
        var ampMul = MathF.Pow(lenRatio, 0.38f) * MathF.Pow(massRatio, 0.28f);
        var maxDeg = Math.Clamp(baseDeg * ampMul, 1.1f, 5.5f);
        maxAmpRad = maxDeg.DegToRad();

        var freqMul = MathF.Pow(Math.Clamp(length / refLength, 0.5f, 2.2f), -0.18f);
        var hz = Math.Clamp(baseHz * freqMul, 0.042f, 0.078f);
        omega = 2f * MathF.PI * hz;
    }

    /// <summary>
    /// Visual-only pitch oscillation on open water, summed with shore différent. Does not affect rigid body.
    /// </summary>
    private static float ComputeVisualWavePitchOnWater(Slave slave, RigidBody rigidBody, float dt)
    {
        var grounded = slave.CachedFloorLevel > slave.CachedWaterSurface || slave.GroundContactLatched;
        if (grounded)
            return 0f;

        var submerged = MathF.Max(0f, slave.CachedWaterSurface - rigidBody.Position.Y);
        const float submergedForFullAmp = 0.32f;
        var depthMul = Math.Clamp(submerged / submergedForFullAmp, 0f, 1f);
        if (depthMul <= 0f)
            return 0f;

        GetVisualWavePitchModelFactors(slave.ShipController?.ShipModel, slave.Scale, out var maxAmpRad, out var omega);
        slave.WavePitchPhase += omega * dt;
        // keep phase bounded
        if (slave.WavePitchPhase > MathF.PI * 4000f)
            slave.WavePitchPhase -= MathF.PI * 4000f;

        var phaseOff = (slave.ObjId & 511) * 0.211f;
        return MathF.Sin(slave.WavePitchPhase + phaseOff) * maxAmpRad * depthMul;
    }

    /// <summary>
    /// Max visual bank (degrees) for turn lean from <c>ship_models</c> mass box and mass.
    /// Horizontal footprint: <c>mass_box_size_x/y</c> are both in-plane; length = max(X,Y), beam = min(X,Y); height = Z.
    /// Reference constants coarsely fitted to former per-<see cref="SlaveKind"/> caps using averages from <c>compact.sqlite3</c>.
    /// </summary>
    private static float ComputeVisualMaxBankDegFromShipModel(ShipModelV1 model, float scale)
    {
        if (!TryGetShipMassBoxWaterlineExtents(model, scale, out var length, out var beam, out var height, out var mass))
            return 8f;

        const float refLength = 14f;
        const float refBeam = 1.5f;
        const float refHeight = 16f;
        const float refMass = 85000f;
        const float baseDeg = 9f;

        var lengthFactor = MathF.Pow(Math.Clamp(length / refLength, 0.35f, 2.8f), 0.22f);
        var beamFactor = MathF.Pow(Math.Clamp(refBeam / beam, 0.65f, 1.6f), 0.28f);
        var massFactor = MathF.Pow(Math.Clamp(refMass / mass, 0.2f, 4f), 0.18f);
        var heightFactor = MathF.Pow(Math.Clamp(refHeight / height, 0.5f, 2f), 0.12f);

        var deg = baseDeg * lengthFactor * beamFactor * massFactor * heightFactor;
        return Math.Clamp(deg, 5f, 14f);
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
        var maxBankDeg = ComputeVisualMaxBankDegFromShipModel(slave.ShipController?.ShipModel, slave.Scale);
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

        _shipShore.UpdateVisualGroundPitch(slave, rigidBody, deltaTime);

        var wavePitchRad = ComputeVisualWavePitchOnWater(slave, rigidBody, dt);

        // Replication smoothing for clients only; rigid body and transform stay physics-accurate below.
        // Horizontal (phys X,Z → packet X,Y) uses snappier lambdas; vertical / trim softer; bank softer still (turn lean jitter).
        const float repLambdaHorizFree = 22f;
        const float repLambdaHorizContact = 11f;
        const float repLambdaVertFree = 8f;
        const float repLambdaVertContact = 4f;
        const float repLambdaBankFree = 4f;
        const float repLambdaBankContact = 2f;
        var rep = slave.ShipController!.Replication;
        var repLambdaH = rep.ContactHoldTicks > 0 ? repLambdaHorizContact : repLambdaHorizFree;
        var repLambdaV = rep.ContactHoldTicks > 0 ? repLambdaVertContact : repLambdaVertFree;
        var repLambdaB = rep.ContactHoldTicks > 0 ? repLambdaBankContact : repLambdaBankFree;
        var repAlphaH = 1f - MathF.Exp(-repLambdaH * dt);
        var repAlphaV = 1f - MathF.Exp(-repLambdaV * dt);
        var repAlphaB = 1f - MathF.Exp(-repLambdaB * dt);

        var tgtX = rigidBody.Position.X;
        var tgtY = rigidBody.Position.Z;
        var tgtZ = rigidBody.Position.Y;
        var tgtVx = rigidBody.Velocity.X;
        var tgtVy = rigidBody.Velocity.Z;
        var tgtVz = rigidBody.Velocity.Y;

        if (!rep.Seeded)
        {
            rep.PosX = tgtX;
            rep.PosY = tgtY;
            rep.PosZ = tgtZ;
            rep.VelPx = tgtVx;
            rep.VelPy = tgtVy;
            rep.VelPz = tgtVz;
            rep.BankSmoothed = slave.BankAngle;
            rep.GroundPitchSmoothed = slave.GroundPitchAngle;
            rep.Seeded = true;
        }
        else
        {
            rep.PosX += (tgtX - rep.PosX) * repAlphaH;
            rep.PosY += (tgtY - rep.PosY) * repAlphaH;
            rep.PosZ += (tgtZ - rep.PosZ) * repAlphaV;
            rep.VelPx += (tgtVx - rep.VelPx) * repAlphaH;
            rep.VelPy += (tgtVy - rep.VelPy) * repAlphaH;
            rep.VelPz += (tgtVz - rep.VelPz) * repAlphaV;
            rep.BankSmoothed += (slave.BankAngle - rep.BankSmoothed) * repAlphaB;
            rep.GroundPitchSmoothed += (slave.GroundPitchAngle - rep.GroundPitchSmoothed) * repAlphaV;
        }

        var bankedRpy = (rpy.Item1, rpy.Item2 + rep.BankSmoothed, rpy.Item3 + rep.GroundPitchSmoothed + wavePitchRad);

        // Physics yaw + euler; bank uses softer λ than vertical/trim.
        var (rotZ, rotY, rotX) = MathUtil.GetSlaveRotationFromDegrees(bankedRpy.Item1, bankedRpy.Item2, bankedRpy.Item3);
        moveType.RotationX = rotX;
        moveType.RotationY = rotY;
        moveType.RotationZ = rotZ;

        moveType.X = rep.PosX;
        moveType.Y = rep.PosY;
        moveType.Z = rep.PosZ;

        moveType.AngVelX = rigidBody.AngularVelocity.X;
        moveType.AngVelY = rigidBody.AngularVelocity.Z;
        moveType.AngVelZ = rigidBody.AngularVelocity.Y;

        const int velMultiplier = 2048;
        moveType.VelX = (short)(rep.VelPx * velMultiplier);
        moveType.VelY = (short)(rep.VelPy * velMultiplier);
        moveType.VelZ = (short)(rep.VelPz * velMultiplier);

        // Do not allow the body to flip
        //slave.RigidBody.Orientation = JMatrix.CreateFromYawPitchRoll(rpy.Item1, 0, 0); // TODO: Fix me with proper physics

        // Apply new Location/Rotation to GameObject
        slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
        slave.Transform.Local.ApplyFromQuaternion(rigidBody.Orientation);
        slave.Transform.Local.SetRotation(
            slave.Transform.Local.Rotation.X,
            slave.Transform.Local.Rotation.Y + rep.BankSmoothed,
            slave.Transform.Local.Rotation.Z + rep.GroundPitchSmoothed + wavePitchRad);

        // Send the packet
        slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);

        // Update all to main Slave and it's children
        slave.Transform.FinalizeTransform();

        if (rep.ContactHoldTicks > 0)
            rep.ContactHoldTicks--;
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
