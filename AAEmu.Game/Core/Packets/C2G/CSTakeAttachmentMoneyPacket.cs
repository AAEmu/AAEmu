using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentMoneyPacket : GamePacket
    {
        public CSTakeAttachmentMoneyPacket() : base(0x09e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            DbLoggerCategory.Database.Connection.ActiveChar.Mails.GetAttached(mailId, true, false, true);
        }
    }
}
