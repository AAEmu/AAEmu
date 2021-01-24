using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
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
        private Thread thread;
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        private CollisionSystem _collisionSystem;
        private Jitter.World _physWorld;
        private Buoyancy _buoyancy;
        private uint _tickCount = 0;
        private bool ThreadRunning = true;

        public void Initialize()
        {
            _collisionSystem = new CollisionSystemSAP();
            _physWorld = new Jitter.World(_collisionSystem);
            _buoyancy = new Buoyancy(_physWorld);
            // _buoyancy.UseOwnFluidArea(DefineFluidArea());
            _buoyancy.FluidBox = new JBBox(new JVector(0, 0, 0), new JVector(100000, 100, 100000));
            
            thread = new Thread(PhysicsThread);
            thread.Start();
        }

        public void PhysicsThread()
        {
            while (ThreadRunning && Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep(1000 / 60);
                _physWorld.Step(1 / 60.0f, false);
                _tickCount++;

                foreach (var slave in SlaveManager.Instance.GetActiveSlavesByKinds(new[] {SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat}))
                {
                    if (slave.SpawnTime.AddSeconds(8) > DateTime.Now)
                        continue;

                    var slaveRigidBody = slave.RigidBody;
                    if (slaveRigidBody == null)
                        continue;
                    
                    var xDelt = slaveRigidBody.Position.X - slave.Position.X;
                    var yDelt = slaveRigidBody.Position.Z - slave.Position.Y;
                    var zDelt = slaveRigidBody.Position.Y - slave.Position.Z;
                    
                    if (_tickCount % 6 == 0)
                    {
                        slave.Move(slaveRigidBody.Position.X, slaveRigidBody.Position.Z, slaveRigidBody.Position.Y);
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
            
            var rigidBody = new RigidBody(new BoxShape(shipModel.MassBoxSizeX, shipModel.MassBoxSizeZ, shipModel.MassBoxSizeY))
            {
                Position = new JVector(slave.Position.X - (shipModel.MassBoxSizeX / 2.0f), slave.Position.Z - (shipModel.MassBoxSizeZ / 2.0f), slave.Position.Y - (shipModel.MassBoxSizeY / 2.0f)),
                // Mass = shipModel.Mass
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
            slave.RigidBody.IsActive = true;
            
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


            var ypr = PhysicsUtil.GetYawPitchRollFromMatrix(rigidBody.Orientation);
            var slaveRotRad = ypr.Item1 + (90 * (Math.PI/ 180.0f));
            
            rigidBody.AddForce(new JVector(slave.Throttle * rigidBody.Mass * (float)Math.Cos(slaveRotRad), 0.0f, slave.Throttle * rigidBody.Mass * (float)Math.Sin(slaveRotRad)));
            rigidBody.AddTorque(new JVector(0, -slave.Steering * (rigidBody.Mass * 2), 0));

            var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationFromDegrees(ypr.Item3, ypr.Item2, ypr.Item1);
            moveType.RotationX = rotX;
            moveType.RotationY = rotY;
            moveType.RotationZ = rotZ;
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
            // _log.Debug("Island: {0}", slave.RigidBody.CollisionIsland.Bodies.Count);
        }

        internal void Stop()
        {
            ThreadRunning = false;
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
