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
        private readonly CountUnreadMail _count;
        
        public SCMailDeletedPacket(bool isSent, long mailId, bool isUnreadMailCountModified, CountUnreadMail count)
            : base(SCOffsets.SCMailDeletedPacket, 5)
        {
            _isSent = isSent;
            _mailId = mailId;
            _isUnreadMailCountModified = isUnreadMailCountModified;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);
            stream.Write(_mailId);
            stream.Write(_isUnreadMailCountModified);
            stream.Write(_count);
            return stream;
        }
    }
}
