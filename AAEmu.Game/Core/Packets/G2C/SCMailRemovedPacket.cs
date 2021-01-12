using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailRemovedPacket : GamePacket
    {
        private readonly bool _isSent;
        private readonly long _mailId;

        public SCMailRemovedPacket(bool isSent, long mailId) : base(SCOffsets.SCMailRemovedPacket, 5)
        {
            _isSent = isSent;
            _mailId = mailId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isSent); // isSent
            stream.Write(_mailId); // type

            return stream;
        }
    }
}
