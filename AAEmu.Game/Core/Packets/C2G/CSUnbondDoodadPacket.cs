using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnbondDoodadPacket : GamePacket
    {
        public CSUnbondDoodadPacket() : base(0x0cd, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterObjId = stream.ReadBc();
            var doodadObjId = stream.ReadBc();

            if (DbLoggerCategory.Database.Connection.ActiveChar.ObjId != characterObjId ||
                DbLoggerCategory.Database.Connection.ActiveChar.Bonding == null || DbLoggerCategory.Database.Connection.ActiveChar.Bonding.ObjId != doodadObjId)
                return;

            DbLoggerCategory.Database.Connection.ActiveChar.Bonding.SetOwner(null);
            DbLoggerCategory.Database.Connection.ActiveChar.Bonding = null;
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                new SCUnbondDoodadPacket(DbLoggerCategory.Database.Connection.ActiveChar.ObjId, DbLoggerCategory.Database.Connection.ActiveChar.Id, doodadObjId), true);
        }
    }
}
