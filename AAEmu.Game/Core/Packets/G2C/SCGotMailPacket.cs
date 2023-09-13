using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGotMailPacket : GamePacket
    {
        private readonly MailHeader _mail;
        private readonly CountUnreadMail _count;
        private readonly bool _hasBody;
        private readonly MailBody _body;
        private readonly bool _isCancel;

        public SCGotMailPacket(MailHeader mail, CountUnreadMail count, bool isCancel, MailBody body = null) : base(SCOffsets.SCGotMailPacket, 1)
        {
            _mail = mail;
            _count = count;
            _isCancel = isCancel;
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
            stream.Write(_isCancel);
            return stream;
        }
    }
}
