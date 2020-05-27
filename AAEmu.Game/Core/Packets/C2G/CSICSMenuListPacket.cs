using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSMenuListPacket : GamePacket
    {
        public CSICSMenuListPacket() : base(0x11b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("ICSMenuList");
            
            DbLoggerCategory.Database.Connection.SendPacket(new SCICSMenuListPacket(1));
        }
    }
}
