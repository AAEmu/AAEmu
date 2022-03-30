using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailDeletedPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly long _mailId;
        private readonly bool _isUnreadMailCountModified;
        private readonly CountUnreadMail _countUnread;

        public SCMailDeletedPacket(bool isSent, long mailId, bool isUnreadMailCountModified, CountUnreadMail countUnread)
            : base(SCOffsets.SCMailDeletedPacket, 5)
        {
            _isSent = isSent;
            _mailId = mailId;
            _isUnreadMailCountModified = isUnreadMailCountModified;
            _countUnread = countUnread;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);                    // isSent
            stream.Write(_mailId);                    // type
            stream.Write(_isUnreadMailCountModified); // isUnreadMailCountModified
            stream.Write(_countUnread);               // CountUnreadMail

            return stream;
        }
    }
}
