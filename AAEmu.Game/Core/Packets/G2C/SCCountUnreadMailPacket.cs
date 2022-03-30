using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCountUnreadMailPacket : GamePacket
    {
        private readonly CountUnreadMail _countUnread;

        public SCCountUnreadMailPacket(CountUnreadMail countUnread) : base(SCOffsets.SCCountUnreadMailPacket, 5)
        {
            _countUnread = countUnread;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_countUnread);
            return stream;
        }
    }
}
