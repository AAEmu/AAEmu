using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSpamReportedPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly bool _isUnreadMailCountModified;
        private readonly CountUnreadMail _count;
        
        public SCSpamReportedPacket(long mailId, bool isUnreadMailCountModified, CountUnreadMail count) : base(SCOffsets.SCSpamReportedPacket, 5)
        {
            _mailId = mailId;
            _isUnreadMailCountModified = isUnreadMailCountModified;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            stream.Write(_isUnreadMailCountModified);
            stream.Write(_count);
            return stream;
        }
    }
}
