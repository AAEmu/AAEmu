using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailBodyPacket : GamePacket
    {
        private readonly bool _isPrepare;
        private readonly bool _isSent;
        private readonly MailBody _body;
        private readonly bool _isOpenDateModified;
        private readonly CountUnreadMail _count;
        //private readonly ulong _mailID;

        public SCMailBodyPacket(bool isPrepare, bool isSent, MailBody body,bool isOpenDateModified, CountUnreadMail count) : base(SCOffsets.SCMailBodyPacket, 1)
        {
            _isPrepare = isPrepare;
            _isSent = isSent;
            _body = body;
            _isOpenDateModified = isOpenDateModified;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isPrepare);
            stream.Write(_isSent);
            stream.Write(_body);
            stream.Write(_isOpenDateModified);
            stream.Write(_count);
            return stream;
        }
    }
}
