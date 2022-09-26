using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailListPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly int _total;
        private readonly MailHeader _mails;
        private readonly byte _mailBoxListKind;

        public SCMailListPacket(bool isSent, int total, MailHeader mails, byte mailBoxListKind)
            : base(SCOffsets.SCMailListPacket, 5)
        {
            _isSent = isSent;
            _total = total;
            _mails = mails;
            _mailBoxListKind = mailBoxListKind;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);
            stream.Write(_total);
            stream.Write(_mails);
            stream.Write(_mailBoxListKind);

            return stream;
        }
    }
}
