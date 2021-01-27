using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailListEndPacket : GamePacket
    {
        private readonly byte _mailBoxListKind;
        private readonly CountUnreadMail _countUnread;

        public SCMailListEndPacket(byte mailBoxListKind, CountUnreadMail countUnread)
            : base(SCOffsets.SCMailListEndPacket, 5)
        {
            _mailBoxListKind = mailBoxListKind;
            _countUnread = countUnread;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailBoxListKind); // mailBoxListKind
            stream.Write(_countUnread);     // CountUnreadMail
            
            return stream;
        }
    }
}
