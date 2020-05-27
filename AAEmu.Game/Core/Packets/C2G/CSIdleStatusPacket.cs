using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSIdleStatusPacket : GamePacket
    {
        public CSIdleStatusPacket() : base(0x132, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            DbLoggerCategory.Database.Connection.ActiveChar.IdleStatus = stream.ReadBoolean();
            _log.Debug("IdleStatus: BcId {0}, {1}", DbLoggerCategory.Database.Connection.ActiveChar.ObjId, DbLoggerCategory.Database.Connection.ActiveChar.IdleStatus);
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                new SCUnitIdleStatusPacket(DbLoggerCategory.Database.Connection.ActiveChar.ObjId, DbLoggerCategory.Database.Connection.ActiveChar.IdleStatus), true);
        }
    }
}
