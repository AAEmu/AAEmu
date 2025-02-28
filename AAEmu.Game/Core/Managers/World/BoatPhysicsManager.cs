using System;
using System.Numerics;
using System.Threading;

using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Physics.Forces;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;

using Jitter.Collision;
using Jitter.Collision.Shapes;
using Jitter.Dynamics;
using Jitter.LinearMath;

using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Core.Managers.World
{
    // ReSharper disable HollowTypeName
    public class BoatPhysicsManager
    {
        private float TargetPhysicsTps { get; set; } = 100f;
        internal Thread _thread;
        private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

        internal CollisionSystem _collisionSystem;
        internal Jitter.World _physWorld;
        internal Buoyancy _buoyancy;
        public bool ThreadRunning { get; set; }
        public InstanceWorld SimulationWorld { get; set; }
        private readonly object _slaveListLock = new();
        private Random _random = new();
        private float _waterLevel = 100f; // Default water level

        public void Initialize()
        {
            _collisionSystem = new CollisionSystemSAP();
            _physWorld = new Jitter.World(_collisionSystem);
            _buoyancy = new Buoyancy(_physWorld);
            _buoyancy.UseOwnFluidArea(CustomWater);

            // Add terrain shape based on height map
            if (SimulationWorld.Name != "main_world") { return; }
            try
            {
                var hmap = WorldManager.Instance.GetWorld(0).HeightMaps;
                var heightMaxCoefficient = WorldManager.Instance.GetWorld(0).HeightMaxCoefficient;
                var dx = hmap.GetLength(0);
                var dz = hmap.GetLength(1);
                var hmapTerrain = new float[dx, dz];
                for (var x = 0; x < dx; x++)
                    for (var y = 0; y < dz; y++)
                        hmapTerrain[x, y] = (float)(hmap[x, y] / heightMaxCoefficient);
                var terrain = new TerrainShape(hmapTerrain, 2.0f, 2.0f);
                var body = new RigidBody(terrain) { IsStatic = true };
                _physWorld.AddBody(body);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            Logger.Info("BoatPhysicsManager initialized.");
        }

        public bool CustomWater(ref JVector area)
        {
            return SimulationWorld?.IsWater(new Vector3(area.X, area.Z, area.Y)) ?? area.Y <= _waterLevel;
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
                Logger.Debug($"PhysicsThread Start: {Thread.CurrentThread.Name}");
                var simulatedSlaveTypeList = new[]
                {
                    SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip,
                    SlaveKind.MerchantShip, SlaveKind.Speedboat
                };

                var lastTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var fixedStep = TimeSpan.FromSeconds(1f / TargetPhysicsTps);
                const int MaxSteps = 4;
                var accumulatedTime = TimeSpan.Zero;

                while (ThreadRunning)
                {
                    Thread.Sleep(fixedStep);

                    var currentTick = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    var timeSinceLastTick = currentTick - lastTick;
                    accumulatedTime += timeSinceLastTick;
                    var steps = 0;

                    // Potentially step multiple times to catch up if we were running behind.
                    while (accumulatedTime > fixedStep)
                    {
                        _physWorld.Step((float)fixedStep.TotalSeconds, false);
                        accumulatedTime -= fixedStep;

                        if (++steps >= MaxSteps)
                        {
                            break;
                        }
                    }

                    lastTick = currentTick;

                    lock (_slaveListLock)
                    {
                        var slaveList = SlaveManager.Instance.GetActiveSlavesByKinds(simulatedSlaveTypeList, SimulationWorld.Id);
                        if (slaveList == null) continue;

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
                            {
                                Logger.Debug($"Skip {slave.Name}");
                                continue;
                            }

                            SyncTransformWithRigidBody(slave);
                            BoatPhysicsTick(slave, slave.RigidBody);
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

        private void SyncTransformWithRigidBody(Slave slave)
        {
            var slaveRigidBody = slave.RigidBody;
            var xDelta = slaveRigidBody.Position.X - slave.Transform.World.Position.X;
            var yDelta = slaveRigidBody.Position.Z - slave.Transform.World.Position.Y;
            var zDelta = slaveRigidBody.Position.Y - slave.Transform.World.Position.Z;
            if (zDelta < -7)
            {
                slaveRigidBody.Position = slaveRigidBody.Position with { Y = slave.Transform.World.Position.Z };
                zDelta = 0;
                Logger.Info($"SyncTransformWithRigidBody {slave.Name} -> {SimulationWorld.Name}, _waterLevel={_waterLevel}, slave.Position.Z={slave.Transform.World.Position.Z}");
            }

            slave.Transform.Local.Translate(xDelta, yDelta, zDelta);
            var rotation = JQuaternion.CreateFromMatrix(slaveRigidBody.Orientation);
            slave.Transform.Local.ApplyFromQuaternion(rotation.X, rotation.Z, rotation.Y, rotation.W);
        }

        public void AddShip(Slave slave)
        {
            var shipModel = ModelManager.Instance.GetShipModel(slave.ModelId);
            if (shipModel == null || shipModel.Mass <= 0)
            {
                Logger.Error($"Invalid ship model for slave {slave.Name}");
                return;
            }

            var slaveMaterial = new Material();

            // New version: Keel & Tube
            var transformedShapes = new CompoundShape.TransformedShape[2];
            // Tube
            var slaveBox = new BoxShape(shipModel.MassBoxSizeX, shipModel.MassBoxSizeZ, shipModel.MassBoxSizeY);
            transformedShapes[0] = new CompoundShape.TransformedShape(slaveBox, JMatrix.Identity, JVector.Zero);

            // Keel
            transformedShapes[1] = new CompoundShape.TransformedShape(new BoxShape(shipModel.MassBoxSizeX, shipModel.MassBoxSizeZ, 1f), JMatrix.Identity, JVector.Zero);

            // Compound & rigidBody
            var compoundShape = new CompoundShape(transformedShapes);
            var rigidBody = new RigidBody(compoundShape, slaveMaterial);

            rigidBody.Position = slave.Transform.World.Position.VectorToJVector();
            rigidBody.Orientation = JMatrix.CreateRotationY(slave.Transform.World.Rotation.Z); // yaw y-axis in Jitter, z-axis in Archeage
            rigidBody.Mass = shipModel.Mass;
            rigidBody.IsActive = true;
            rigidBody.IsStatic = false;

            _buoyancy.Add(rigidBody, 3);
            _physWorld.AddBody(rigidBody);
            slave.RigidBody = rigidBody;
        }

        public void RemoveShip(Slave slave)
        {
            if (slave.RigidBody == null) return;

            var rigidBody = slave.RigidBody;
            rigidBody.IsActive = false;
            _buoyancy.Remove(rigidBody);
            _physWorld.RemoveBody(rigidBody);
            slave.RigidBody = null;
            Logger.Debug($"RemoveShip {slave.Name} <- {SimulationWorld.Name}");
        }

        public void BoatPhysicsTick(Slave slave, RigidBody rigidBody)
        {
            var shipModel = ModelManager.Instance.GetShipModel(slave.Template.ModelId);
            if (shipModel == null) return;

            rigidBody.IsActive = true;
            rigidBody.IsStatic = false;

            // Calculate submerged depth and buoyancy force
            var submergedDepth = Math.Max(0, _waterLevel - rigidBody.Position.Y);
            var isOnWater = submergedDepth > 0;
            var isOnLand = !isOnWater && submergedDepth <= 0;

            if (isOnWater)
            {
                // Apply buoyancy and drag forces
                var buoyancyForce = new JVector(0, submergedDepth * shipModel.Mass * shipModel.WaterDensity * 9.81f, 0);
                rigidBody.AddForce(buoyancyForce);

                var dragForce = new JVector(-rigidBody.LinearVelocity.X * shipModel.WaterResistance,
                    -rigidBody.LinearVelocity.Y * shipModel.WaterResistance,
                    -rigidBody.LinearVelocity.Z * shipModel.WaterResistance);
                rigidBody.AddForce(dragForce);
            }
            else if (isOnLand)
            {
                // Apply ground friction and stop the ship
                const float GroundFriction = 0.4f; // Sand: around 0.4
                var frictionForce = new JVector(-rigidBody.LinearVelocity.X * GroundFriction,
                    0,
                    -rigidBody.LinearVelocity.Z * GroundFriction);
                rigidBody.AddForce(frictionForce);

                // Gradually reduce speed
                const float CollisionDamping = 0.5f;
                rigidBody.LinearVelocity *= CollisionDamping;
                rigidBody.AngularVelocity *= CollisionDamping;

                // Stop the ship and apply roll
                if (rigidBody.LinearVelocity.Length() < 0.01f)
                {
                    rigidBody.LinearVelocity = JVector.Zero;
                    rigidBody.AngularVelocity = JVector.Zero;

                    // Apply roll to the ship
                    var rollAngle = GetRollAngle(rigidBody.Orientation);
                    if (Math.Abs(rollAngle) < 0.1f)
                    {
                        var correctionTorque = new JVector(0, 0, -rollAngle * rigidBody.Mass * 0.1f);
                        rigidBody.AddTorque(correctionTorque);
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

            ApplyCollisions(slave, rigidBody, shipModel);
            ApplyForceAndTorque(slave, rigidBody, shipModel);
            SendUpdatedMovementData(slave, rigidBody);
        }

        public static float GetRollAngle(JMatrix orientation)
        {
            var yawPitchRoll = GetYawPitchRollFromJMatrix(orientation);
            return yawPitchRoll.Item2; // Roll angle in radians
        }

        public static (float, float, float) GetYawPitchRollFromJMatrix(JMatrix mat)
        {
            return MathUtil.GetYawPitchRollFromQuat(JMatrixToQuaternion(mat));
        }

        public static Quaternion JMatrixToQuaternion(JMatrix matrix)
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

        public void Stop()
        {
            ThreadRunning = false;
        }

        private void ApplyCollisions(Slave slave, RigidBody rigidBody, ShipModel shipModel)
        {
            var floor = WorldManager.Instance.GetHeight(slave.Transform);
            var boxSize = rigidBody.Shape.BoundingBox.Max - rigidBody.Shape.BoundingBox.Min;
            var boatBottom = rigidBody.Position.Y;
            //Logger.Debug($"Slave: {slave.Name}, floor: {floor:F1}, boatBottom: {boatBottom:F1}, boxSize: {boxSize}");

            if (boatBottom < floor)
            {
                var penetration = floor - boatBottom;
                rigidBody.Position += new JVector(0, penetration, 0);
                var collisionForce = new JVector(0, shipModel.Mass * 9.81f, 0);
                rigidBody.AddForce(collisionForce);

                // Gradually reduce speed
                var collisionDamping = 0.5f;
                rigidBody.LinearVelocity *= collisionDamping;
                rigidBody.AngularVelocity *= collisionDamping;

                Logger.Debug($"Collision detected. Boat adjusted position: {rigidBody.Position}");
            }
        }

        private void ApplyForceAndTorque(Slave slave, RigidBody rigidBody, ShipModel shipModel)
        {
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
            var turnSpeed = slave.TurnSpeed == 0 ? 10f : slave.TurnSpeed;
            slave.RotSpeed += steeringFloatVal * (turnSpeed / 100f) * (shipModel.TurnAccel / 360f);
            // Clamp to Steer Velocity
            slave.RotSpeed = Math.Min(slave.RotSpeed, shipModel.SteerVel);
            slave.RotSpeed = Math.Max(slave.RotSpeed, -shipModel.SteerVel);

            // Slow down turning if no steering active
            const float AngularDamping = 0.9f; // Damping of angular velocity
            if (slave.Steering == 0)
            {
                slave.RotSpeed *= AngularDamping;
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
            var tubeVolume = shipModel.MassCenterX * shipModel.MassCenterY * shipModel.MassCenterZ * MathF.PI;
            var solidVolume = MathF.Abs(rigidBody.Mass - tubeVolume);

            // Get current rotation of the ship
            var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(rigidBody.Orientation);
            var slaveRotRad = rpy.Item1 + 1.57f; // 90

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
        }

        private void SendUpdatedMovementData(Slave slave, RigidBody rigidBody)
        {
            var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
            moveType.UseSlaveBase(slave);

            // Get current rotation of the ship
            var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(rigidBody.Orientation);
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

            // Do not allow the body to flip
            slave.RigidBody.Orientation = JMatrix.CreateFromYawPitchRoll(rpy.Item1, 0, 0); // TODO: Fix me with proper physics

            // Apply new Location/Rotation to GameObject
            slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
            var jRot = JQuaternion.CreateFromMatrix(rigidBody.Orientation);
            slave.Transform.Local.ApplyFromQuaternion(jRot.X, jRot.Z, jRot.Y, jRot.W);

            // Send the packet
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);

            // Update all to main Slave and it's children
            slave.Transform.FinalizeTransform();
        }
    }
}
