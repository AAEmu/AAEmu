using System;
using System.Numerics;
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
            
            var (newX, newY) = MathUtil.AddDistanceToFrontDeg(-(slave.Speed / 20f), slave.Position.X, slave.Position.Y,
                slave.RotationDegrees - 90.0f );

            var diffX = newX - slave.Position.X;
            var diffY = newY - slave.Position.Y;
            
            slave.Move(diffX, diffY, 0);
            
            moveType.VelX = (short) (diffX * 21900);
            moveType.VelY = (short) (diffY * 21900);
            
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
