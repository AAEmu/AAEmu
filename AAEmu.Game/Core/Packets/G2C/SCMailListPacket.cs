using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailListPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly MailHeader[] _mails;
        private readonly byte _mailBoxListKind;

        public SCMailListPacket(bool isSent, MailHeader[] mails, byte mailBoxListKind) : base(SCOffsets.SCMailListPacket, 5)
        {
            _isSent = isSent;
            _mails = mails;
            _mailBoxListKind = mailBoxListKind;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);          // isSent
            stream.Write(_mails.Length);    // total
            foreach (var mail in _mails)    // TODO There is no loop in x2game
            {
                stream.Write(mail);         // Mail
            }

            stream.Write(_mailBoxListKind); // mailBoxListKind

            return stream;
        }
    }
}
