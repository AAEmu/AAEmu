using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSActiveWeaponChangedPacket : GamePacket
    {
        public CSActiveWeaponChangedPacket() : base(0x08c, 1) //TODO 1.0 opcode: 0x08b
        {
        }

        public override void Read(PacketStream stream)
        {
            var activeWeapon = stream.ReadByte();
            DbLoggerCategory.Database.Connection.ActiveChar.ActiveWeapon = activeWeapon;
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCActiveWeaponChangedPacket(DbLoggerCategory.Database.Connection.ActiveChar.ObjId, activeWeapon), true);
        }
    }
}
