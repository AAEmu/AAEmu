using System;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.World
{
    public class BoatPhysicsManager : Singleton<BoatPhysicsManager>
    {
        private Thread thread;

        public void Initialize()
        {
            thread = new Thread(PhysicsThread);
            thread.Start();
        }
        
        public void PhysicsThread()
        {
            while (Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep(50);

                foreach (var slave in SlaveManager.Instance.GetActiveSlavesByKinds(new[] {SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat}))
                {
                    BoatPhysicsTick(slave);
                }
            }
        }

        public void BoatPhysicsTick(Slave slave)
        {
            var velAccel = 2.0f; //per s
            var maxVelForward = 12.9f; //per s
            var maxVelBackward = -5.0f;
            
            // TODO : We'll need to compute the ship's center of mass, based on the table ship_models

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
                slave.RotSpeed -= (slave.RotSpeed / 10);
            
            if (slave.Throttle == 0) // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
                slave.Speed -= (slave.Speed / 10);

            
            
            slave.Position.RotationZ = MathUtil.ConvertDegreeToDirection(-slave.RotationDegrees);
            slave.RotationZ = (short) (slave.Position.RotationZ * 1024);
            var (newX, newY) = MathUtil.AddDistanceToFront(-(slave.Speed / 20f), slave.Position.X, slave.Position.Y,
                slave.Position.RotationZ);
            
            var diffX = newX - slave.Position.X;
            var diffY = newY - slave.Position.Y;
            
            slave.Position.X = newX;
            slave.Position.Y = newY;
            
            slave.RotationDegrees -= slave.RotSpeed;
            slave.RotationDegrees %= 360f;
            
            slave.VelX = (short) (diffX * 21900);
            slave.VelY = (short) (diffY * 21900);

            slave.AngVelZ = slave.RotSpeed;

            var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
            moveType.UseSlaveBase(slave);
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
        }

        public void ComputeThrottle(Slave slave)
        {
            int throttleAccel = 6;
            if (slave.RequestThrottle > slave.Throttle)
            {
                slave.Throttle = (sbyte) Math.Min(sbyte.MaxValue, slave.Throttle + throttleAccel);
                
            }
            else if (slave.RequestThrottle < slave.Throttle && slave.RequestThrottle != 0)
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
            if (slave.RequestSteering > slave.Steering)
            {
                slave.Steering = (sbyte) Math.Min(sbyte.MaxValue, slave.Steering + steeringAccel);
                
            }
            else if (slave.RequestSteering < slave.Steering && slave.RequestSteering != 0)
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
