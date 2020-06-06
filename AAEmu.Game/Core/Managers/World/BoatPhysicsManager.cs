using System;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

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
                Thread.Sleep(500);

                foreach (var slave in SlaveManager.Instance.GetActiveSlavesByKinds(new[] {SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat}))
                {
                    BoatPhysicsTick(slave);
                }
            }
        }

        public void BoatPhysicsTick(Slave slave)
        {
            // var localSeaLevel = 100f; // TODO : Process
            // var totalMilliseconds = (uint) (DateTime.Now - slave.SpawnTime).TotalMilliseconds;
            
            // TODO : We'll need to compute the ship's center of mass, based on the table ship_models

            // var seaHeightDifferential = slave.Position.Z - localSeaLevel;
            // slave.Position.Z += (seaHeightDifferential / 10); // This should slowly bring it down/up to sea level
            // slave.VelZ = (short) ((short) seaHeightDifferential * 100);
            
            slave.Position.X += 0.25f;

            // slave.Steering = (sbyte) (255 * Math.Cos(totalMilliseconds / 1280f));
            
            var moveType = (ShipMoveType)MoveType.GetType(MoveTypeEnum.Ship);
            moveType.UseSlaveBase(slave);
            slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, moveType), false);
        }
        
    }
}
