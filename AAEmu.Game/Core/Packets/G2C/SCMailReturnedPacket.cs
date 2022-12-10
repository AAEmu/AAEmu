using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailReturnedPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly MailHeader _mail;
        
        public SCMailReturnedPacket(long mailId, MailHeader mail) : base(SCOffsets.SCMailReturnedPacket, 1)
        {
            _mailId = mailId;
            _mail = mail;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            stream.Write(_mail);
            return stream;
        }
    }
}
