using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBindSlavePacket : GamePacket
    {
        public CSBindSlavePacket() : base(0x031, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();

            //_log.Debug("BindSlave, Tl: {0}", tlId);
            SlaveManager.Instance.BindSlave(DbLoggerCategory.Database.Connection, tlId);
        }
    }
}
