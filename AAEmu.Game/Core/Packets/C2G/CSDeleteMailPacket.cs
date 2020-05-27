using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeleteMailPacket : GamePacket
    {
        public CSDeleteMailPacket() : base(0x0a1, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            var isSent = stream.ReadBoolean();

            DbLoggerCategory.Database.Connection.ActiveChar.Mails.DeleteMail(mailId, isSent);
        }
    }
}
