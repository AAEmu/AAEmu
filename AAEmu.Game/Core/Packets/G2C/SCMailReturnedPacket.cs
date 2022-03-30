using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailReturnedPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly MailHeader _mail;
        private readonly CountUnreadMail _countUnread;

        public SCMailReturnedPacket(long mailId, MailHeader mail, CountUnreadMail countUnread)
            : base(SCOffsets.SCMailReturnedPacket, 5)
        {
            _mailId = mailId;
            _mail = mail;
            _countUnread = countUnread;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);      // type
            stream.Write(_mail);        // Mail
            stream.Write(_countUnread); // CountUnreadMail

            return stream;
        }
    }
}
