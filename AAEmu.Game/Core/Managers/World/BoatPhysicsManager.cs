using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Physics.Forces;
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
        private Thread thread;
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        private CollisionSystem _collisionSystem;
        private Jitter.World _physWorld;
        private Dictionary<uint, RigidBody> _rigidBodies;
        private Buoyancy _buoyancy;

        public void Initialize()
        {
            _collisionSystem = new CollisionSystemSAP();
            _physWorld = new Jitter.World(_collisionSystem);
            _buoyancy = new Buoyancy(_physWorld);
            // _buoyancy.UseOwnFluidArea(DefineFluidArea());
            _buoyancy.FluidBox = new JBBox(new JVector(0, 0, 0), new JVector(100000, 99, 100000));
            _rigidBodies = new Dictionary<uint, RigidBody>();
            
            thread = new Thread(PhysicsThread);
            thread.Start();
        }

        public void PhysicsThread()
        {
            while (Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep(50);
                _physWorld.Step(1 / 20.0f, false);

                foreach (var slave in SlaveManager.Instance.GetActiveSlavesByKinds(new[] {SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat}))
                {
                    if (slave.SpawnTime.AddSeconds(8) > DateTime.Now)
                        continue;
                    
                    if (!_rigidBodies.ContainsKey(slave.TlId))
                    {
                        var rigidBody = new RigidBody(new BoxShape(4.0f, 8.0f, 7.0f));
                        rigidBody.Position = new JVector(slave.Position.X, slave.Position.Z, slave.Position.Y);
                        
                        _rigidBodies.Add(slave.TlId, rigidBody);
                        _buoyancy.Add(rigidBody, 3);
                        _physWorld.AddBody(rigidBody);
                    }

                    var slaveRigidBody = _rigidBodies[slave.TlId];
                    
                    var xDelt = slaveRigidBody.Position.X - slave.Position.X;
                    var yDelt = slaveRigidBody.Position.Z - slave.Position.Y;
                    var zDelt = slaveRigidBody.Position.Y - slave.Position.Z;
                    slave.Move(xDelt, yDelt, zDelt);
                    BoatPhysicsTick(slave, slaveRigidBody); 
                }
            }
        }

        public void BoatPhysicsTick(Slave slave, RigidBody rigidBody)
        {
            var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
            moveType.UseSlaveBase(slave);
            var velAccel = 2.0f; //per s
            var maxVelForward = 12.9f; //per s
            var maxVelBackward = -5.0f;

            if (slave.Bounded == null)
            {
                slave.ThrottleRequest = 0;
                slave.SteeringRequest = 0;
            }
            
            ComputeThrottle(slave);
            ComputeSteering(slave);
            
            //                                 1/127
            slave.Speed += (slave.Throttle * 0.00787401575f) * (velAccel / 20f);
            slave.Speed = Math.Min(slave.Speed, maxVelForward);
            slave.Speed = Math.Max(slave.Speed, maxVelBackward);
            
            slave.RotSpeed += (slave.Steering * 0.00787401575f) * (velAccel / 20f);
            slave.RotSpeed = Math.Min(slave.RotSpeed, 1);
            slave.RotSpeed = Math.Max(slave.RotSpeed, -1);
            
            if (slave.Steering == 0)
                slave.RotSpeed -= (slave.RotSpeed / 20);

            if (slave.Throttle == 0) // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
                slave.Speed -= (slave.Speed / 45);
            
            slave.Position.RotationZ = MathUtil.ConvertDegreeToDirection(slave.RotationDegrees);

            var slaveRotRad = (slave.RotationDegrees + 90) * (Math.PI / 180.0f);
            
            // var (newX, newY) = MathUtil.AddDistanceToFrontDeg(-(slave.Speed / 20f), slave.Position.X, slave.Position.Y,
            //     slave.RotationDegrees - 90.0f );
            //
            // var diffX = newX - slave.Position.X;
            // var diffY = newY - slave.Position.Y;
            
            // slave.Move(diffX, diffY, 0);
            //
            // moveType.VelX = (short) (diffX * 21900);
            // moveType.VelY = (short) (diffY * 21900);
            
            rigidBody.AddForce(new JVector(slave.Throttle * 50 * (float)Math.Cos(slaveRotRad), 0.0f, slave.Throttle * 50 * (float)Math.Sin(slaveRotRad)));
            
            slave.RotationDegrees -= slave.RotSpeed;
            // TODO : Find a better way
            if (slave.RotationDegrees < -180.0f)
                slave.RotationDegrees = 180.0f;
            if (slave.RotationDegrees > 180.0f)
                slave.RotationDegrees = -180.0f;
            
            var yaw = (float)(slave.RotationDegrees * (Math.PI / 180));
            
            var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationFromDegrees(0.0003f, -0.002f, yaw);
            moveType.RotationX = rotX;
            moveType.RotationY = rotY;
            moveType.RotationZ = rotZ;
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
        }
        
        public void ComputeThrottle(Slave slave)
        {
            int throttleAccel = 6;
            if (slave.ThrottleRequest > slave.Throttle)
            {
                slave.Throttle = (sbyte) Math.Min(sbyte.MaxValue, slave.Throttle + throttleAccel);

            }
            else if (slave.ThrottleRequest < slave.Throttle && slave.ThrottleRequest != 0)
            {
                slave.Throttle = (sbyte) Math.Max(sbyte.MinValue, slave.Throttle - throttleAccel);
            }
            else
            {
                if (slave.Throttle > 0)
                {
                    slave.Throttle = (sbyte) Math.Max(slave.Throttle - throttleAccel, 0);
                }
                else if (slave.Throttle < 0)
                {
                    slave.Throttle = (sbyte) Math.Min(slave.Throttle + throttleAccel, 0);
                }
            }
        }
        
        public void ComputeSteering(Slave slave)
        {
            int steeringAccel = 6;
            if (slave.SteeringRequest > slave.Steering)
            {
                slave.Steering = (sbyte) Math.Min(sbyte.MaxValue, slave.Steering + steeringAccel);

            }
            else if (slave.SteeringRequest < slave.Steering && slave.SteeringRequest != 0)
            {
                slave.Steering = (sbyte) Math.Max(sbyte.MinValue, slave.Steering - steeringAccel);
            }
            else
            {
                if (slave.Steering > 0)
                {
                    slave.Steering = (sbyte) Math.Max(slave.Steering - steeringAccel, 0);
                }
                else if (slave.Steering < 0)
                {
                    slave.Steering = (sbyte) Math.Min(slave.Steering + steeringAccel, 0);
                }
            }
        }
    }
}
