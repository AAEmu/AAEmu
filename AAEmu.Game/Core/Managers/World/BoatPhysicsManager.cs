using System;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
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

namespace AAEmu.Game.Core.Managers.World
{
    public class BoatPhysicsManager : Singleton<BoatPhysicsManager>
    {
        private Thread _thread;
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private CollisionSystem _collisionSystem;
        private Jitter.World _physWorld;
        private Buoyancy _buoyancy;
        private uint _tickCount ;
        private bool ThreadRunning { get; set; } = true;

        public void Initialize()
        {
            _collisionSystem = new CollisionSystemSAP();
            _physWorld = new Jitter.World(_collisionSystem);
            _buoyancy = new Buoyancy(_physWorld);
            // _buoyancy.UseOwnFluidArea(DefineFluidArea());
            _buoyancy.FluidBox = new JBBox(new JVector(0, 0, 0), new JVector(100000, 100, 100000));

            _thread = new Thread(PhysicsThread);
            _thread.Start();
        }

        public void PhysicsThread()
        {
            while (ThreadRunning && Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep(1000 / 60);
                _physWorld.Step(1 / 60.0f, false);
                _tickCount++;

                foreach (var slave in SlaveManager.Instance.GetActiveSlavesByKinds(new[] { SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat }))
                {
                    if (slave.SpawnTime.AddSeconds(slave.Template.PortalTime) > DateTime.UtcNow)
                        continue;

                    var slaveRigidBody = slave.RigidBody;
                    if (slaveRigidBody == null)
                        continue;
                    
                    var xDelt = slaveRigidBody.Position.X - slave.Transform.World.Position.X;
                    var yDelt = slaveRigidBody.Position.Z - slave.Transform.World.Position.Y;
                    var zDelt = slaveRigidBody.Position.Y - slave.Transform.World.Position.Z;
                    
                    slave.Transform.Local.Translate(xDelt,yDelt,zDelt); 
                    var rot = JQuaternion.CreateFromMatrix(slaveRigidBody.Orientation);
                    slave.Transform.Local.ApplyFromQuaternion(rot.X, rot.Z, rot.Y, rot.W);
                    // slave.Transform.Local.Rotation = new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
                    
                    if (_tickCount % 6 == 0)
                    {
                        //var rpy = slave.Transform.Local.ToRollPitchYaw();
                        //slave.SetPosition(slaveRigidBody.Position.X, slaveRigidBody.Position.Z, slaveRigidBody.Position.Y, rpy.X, rpy.Y, rpy.Z);
                        //slave.Move(slaveRigidBody.Position.X, slaveRigidBody.Position.Z, slaveRigidBody.Position.Y);
                        _physWorld.CollisionSystem.Detect(true);
                        BoatPhysicsTick(slave, slaveRigidBody);
                    }
                }
            }
        }

        public void AddShip(Slave slave)
        {
            var shipModel = ModelManager.Instance.GetShipModel(slave.ModelId);
            if (shipModel == null)
                return;

            var slaveBox = new BoxShape(shipModel.MassBoxSizeX, shipModel.MassBoxSizeZ, shipModel.MassBoxSizeY);
            var slaveMaterial = new Material();
            // TODO: Add the center of mass settings into JitterPhysics somehow

            var rigidBody = new RigidBody(slaveBox,slaveMaterial)
            {
                Position = new JVector(slave.Transform.World.Position.X, slave.Transform.World.Position.Z, slave.Transform.World.Position.Y),
                // Mass = shipModel.Mass, // Using the actually defined mass of the DB doesn't really work
                Orientation = JMatrix.CreateRotationY(slave.Transform.World.Rotation.Z)
            };

            _buoyancy.Add(rigidBody, 3);
            _physWorld.AddBody(rigidBody);
            slave.RigidBody = rigidBody;
        }

        public void RemoveShip(Slave slave)
        {
            if (slave.RigidBody == null) return;
            _buoyancy.Remove(slave.RigidBody);
            _physWorld.RemoveBody(slave.RigidBody);
        }

        public void BoatPhysicsTick(Slave slave, RigidBody rigidBody)
        {
            var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
            moveType.UseSlaveBase(slave);
            var shipModel = ModelManager.Instance.GetShipModel(slave.Template.ModelId);

            var velAccel = shipModel.Accel; // 2.0f; //per s
            var rotAccel = shipModel.TurnAccel; // 0.5f; //per s
            var maxVelForward = shipModel.Velocity ; // 12.9f //per s
            var maxVelBackward = -shipModel.ReverseVelocity ; // -5.0f

            // If no driver, then no steering
            if (!slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver))
            {
                slave.ThrottleRequest = 0;
                slave.SteeringRequest = 0;
            }

            ComputeThrottle(slave, (int)Math.Ceiling(shipModel.Velocity / shipModel.Accel));
            ComputeSteering(slave);//, (int)Math.Ceiling(shipModel.Velocity / shipModel.TurnAccel));
            slave.RigidBody.IsActive = true;
            
            // Provide minimum speed of 1 when Throttle is used
            if ((slave.Throttle > 0) && (slave.Speed < 1f))
                slave.Speed = 1f;
            if ((slave.Throttle < 0) && (slave.Speed > -1f))
                slave.Speed = -1f;
            
            // Convert sbyte throttle value to use as speed
            slave.Speed += (slave.Throttle * 0.00787401575f) * (velAccel / 10f);
            
            // Clamp speed between min and max Velocity
            slave.Speed = Math.Min(slave.Speed, maxVelForward);
            slave.Speed = Math.Max(slave.Speed, maxVelBackward);
            
            slave.RotSpeed += (slave.Steering * 0.00787401575f) * (rotAccel / 100f);
            slave.RotSpeed = Math.Min(slave.RotSpeed, 1f);
            slave.RotSpeed = Math.Max(slave.RotSpeed, -1f);

            if (slave.Steering == 0)
            {
                slave.RotSpeed -= (slave.RotSpeed / 20);
                if (Math.Abs(slave.RotSpeed) <= 0.01)
                    slave.RotSpeed = 0;
            }

            if (slave.Throttle == 0) // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
            {
                slave.Speed -= (slave.Speed / 20f);
                if (Math.Abs(slave.Speed) < 0.01)
                    slave.Speed = 0;
            }

            // _log.Debug("Slave: {0}, speed: {1}, rotSpeed: {2}", slave.ObjId, slave.Speed, slave.RotSpeed);
            
            // Calculate some stuff for later
            var boxSize = rigidBody.Shape.BoundingBox.Max - rigidBody.Shape.BoundingBox.Min;
            var tubeVolume = shipModel.TubeLength * shipModel.TubeRadius * MathF.PI;
            var solidVolume = MathF.Abs(rigidBody.Mass - tubeVolume);

            var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(rigidBody.Orientation);
            var slaveRotRad = rpy.Item1 + (90 * (MathF.PI/ 180.0f));

            var forceThrottle = (float)slave.Speed * 17.25f; // don't know what this number is, but it's perfect for all ships
            rigidBody.AddForce(new JVector(forceThrottle * rigidBody.Mass * MathF.Cos(slaveRotRad), 0.0f, forceThrottle * rigidBody.Mass * MathF.Sin(slaveRotRad)));
            
            // Make sure the steering is reversed when going backwards.
            var steer = (float)slave.Steering * shipModel.SteerVel;
            if (forceThrottle < 0)
                steer *= -1;
            
            // Calculate Steering Force based on bounding box 
            var steerForce = -steer * (solidVolume * boxSize.X * boxSize.Y / 172.5f * 2f); // Totally random value, but it feels right
            rigidBody.AddTorque(new JVector(0, steerForce, 0));
            
            /*
            if ((slave.Steering != 0) || (slave.Throttle != 0))
                _log.Debug($"Request: {slave.SteeringRequest}, Steering: {slave.Steering}, steer: {steer}, vol: {solidVolume} mass: {rigidBody.Mass}, force: {steerForce}, torque: {rigidBody.Torque}");
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
            // It doesn't actually do this server-side, as wel only modify the packet sent to the players
            // If center of mass is positive rather than negative, we need to ignore it here to prevent the boat from floating
            moveType.Z += (shipModel.MassCenterZ < 0f ? (shipModel.MassCenterZ / 2f) : 0f) - shipModel.KeelHeight;

            // Apply new Location/Rotation to GameObject
            slave.Transform.Local.SetPosition(rigidBody.Position.X, rigidBody.Position.Z, rigidBody.Position.Y);
            var jRot = JQuaternion.CreateFromMatrix(rigidBody.Orientation);
            slave.Transform.Local.ApplyFromQuaternion(jRot.X, jRot.Z, jRot.Y, jRot.W);
            
            // Send the packet
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
            // _log.Debug("Island: {0}", slave.RigidBody.CollisionIsland.Bodies.Count);
            
            // Update all to main Slave and it's children 
            slave.Transform.FinalizeTransform();
        }

        internal void Stop()
        {
            ThreadRunning = false;
        }

        public void ComputeThrottle(Slave slave, int throttleAccel = 6)
        {
            if (slave.ThrottleRequest > slave.Throttle)
            {
                slave.Throttle = (sbyte)Math.Min(sbyte.MaxValue, slave.Throttle + throttleAccel);

            }
            else if (slave.ThrottleRequest < slave.Throttle && slave.ThrottleRequest != 0)
            {
                slave.Throttle = (sbyte)Math.Max(sbyte.MinValue, slave.Throttle - throttleAccel);
            }
            else
            {
                if (slave.Throttle > 0)
                {
                    slave.Throttle = (sbyte)Math.Max(slave.Throttle - throttleAccel, 0);
                }
                else if (slave.Throttle < 0)
                {
                    slave.Throttle = (sbyte)Math.Min(slave.Throttle + throttleAccel, 0);
                }
            }
        }

        public void ComputeSteering(Slave slave, int steeringAccel = 6)
        {
            if (slave.SteeringRequest > slave.Steering)
            {
                slave.Steering = (sbyte)Math.Min(sbyte.MaxValue, slave.Steering + steeringAccel);
            }
            else if (slave.SteeringRequest < slave.Steering && slave.SteeringRequest != 0)
            {
                slave.Steering = (sbyte)Math.Max(sbyte.MinValue, slave.Steering - steeringAccel);
            }
            else
            {
                if (slave.Steering > 0)
                {
                    slave.Steering = (sbyte)Math.Max(slave.Steering - steeringAccel, 0);
                }
                else if (slave.Steering < 0)
                {
                    slave.Steering = (sbyte)Math.Min(slave.Steering + steeringAccel, 0);
                }
            }
        }
    }
}
