using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNoticeMessagePacket : GamePacket
    {
        public SCNoticeMessagePacket() : base(SCOffsets.SCNoticeMessagePacket, 1)
        {

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)1); // noticeType
            stream.Write("1"); // colorStr
            stream.Write(20); // visibleTime
            stream.Write("test"); // message

            return stream;
        }
    }
}
