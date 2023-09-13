using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailStatusUpdatedPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly long _mailId;
        private readonly MailStatus _status;

        public SCMailStatusUpdatedPacket(bool isSent, long mailId, MailStatus status) : base(SCOffsets.SCMailStatusUpdatedPacket, 1)
        {
            _isSent = isSent;
            _mailId = mailId;
            _status = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);
            stream.Write(_mailId);
            stream.Write((byte)_status);
            return stream;
        }
    }
}
