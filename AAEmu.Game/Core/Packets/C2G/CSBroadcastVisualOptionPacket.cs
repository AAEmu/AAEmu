using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBroadcastVisualOptionPacket : GamePacket
    {
        public CSBroadcastVisualOptionPacket() : base(0x119, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            DbLoggerCategory.Database.Connection.ActiveChar.VisualOptions.Read(stream);

            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                new SCUnitVisualOptionsPacket(DbLoggerCategory.Database.Connection.ActiveChar.ObjId, DbLoggerCategory.Database.Connection.ActiveChar.VisualOptions), true);
        }
    }
}
