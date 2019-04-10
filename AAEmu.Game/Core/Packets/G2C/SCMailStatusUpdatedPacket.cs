using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailStatusUpdatedPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly long _mailId;
        private readonly byte _status;

        public SCMailStatusUpdatedPacket(bool isSent, long mailId, byte status) : base(SCOffsets.SCMailStatusUpdatedPacket, 1)
        {
            _isSent = isSent;
            _mailId = mailId;
            _status = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent);
            stream.Write(_mailId);
            stream.Write(_status);
            return stream;
        }
    }
}
