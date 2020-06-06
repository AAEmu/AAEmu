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
            //                                 1/127
            slave.Speed += (slave.Throttle * 0.00787401575f) * (velAccel / 20f);
            slave.Speed = Math.Min(slave.Speed, maxVelForward);
            slave.Speed = Math.Max(slave.Speed, maxVelBackward);

            if (slave.Throttle == 0) // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
                slave.Speed -= (slave.Speed / 10);

            var (newX, newY) = MathUtil.AddDistanceToFront(-(slave.Speed / 20f), slave.Position.X, slave.Position.Y,
                slave.Position.RotationZ);
            
            var diffX = newX - slave.Position.X;
            var diffY = newY - slave.Position.Y;
            slave.Position.X = newX;
            slave.Position.Y = newY;

            // This works when going staight Y, it displays the correct speed. Didn't add rotation yet so i don't know 
            slave.VelX = (short) (diffX * 21900);
            slave.VelY = (short) (diffY * 21900);

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
        
    }
}
