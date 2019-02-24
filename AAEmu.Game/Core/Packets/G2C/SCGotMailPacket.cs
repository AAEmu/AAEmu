using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGotMailPacket : GamePacket
    {
        private readonly Mail _mail;
        private readonly CountUnreadMail _count;
        private readonly bool _hasBody;
        private readonly MailBody _body;

        public SCGotMailPacket(Mail mail, CountUnreadMail count, MailBody body = null) : base(0x112, 1)
        {
            _mail = mail;
            _count = count;
            _hasBody = body != null;
            _body = body;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mail);
            stream.Write(_count);
            stream.Write(_hasBody);
            if (_hasBody)
                stream.Write(_body);
            return stream;
        }
    }
}
