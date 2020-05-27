using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListMailPacket : GamePacket
    {
        public CSListMailPacket() : base(0x09a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            DbLoggerCategory.Database.Connection.ActiveChar.Mails.OpenMailbox();
        }
    }
}
