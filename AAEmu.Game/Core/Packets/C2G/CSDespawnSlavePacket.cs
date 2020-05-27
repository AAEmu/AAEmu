using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDespawnSlavePacket : GamePacket
    {
        public CSDespawnSlavePacket() : base(0x02f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slaveObjId = stream.ReadBc();

            //_log.Debug("DespawnSlave, SlaveObjId: {0}", slaveObjId);
            SlaveManager.Instance.Delete(DbLoggerCategory.Database.Connection.ActiveChar, slaveObjId);
        }
    }
}
