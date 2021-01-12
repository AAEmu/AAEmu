using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCountUnreadMailPacket : GamePacket
    {
        private readonly CountUnreadMail _count;

        public SCCountUnreadMailPacket(CountUnreadMail count) : base(SCOffsets.SCCountUnreadMailPacket, 5)
        {
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);
            return stream;
        }
    }
}
