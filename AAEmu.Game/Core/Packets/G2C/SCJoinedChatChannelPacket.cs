using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCJoinedChatChannelPacket : GamePacket
    {
        private readonly ChatType _type;

        public SCJoinedChatChannelPacket(ChatType type) : base(0x0c4, 1)
        {
            _type = type;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((short) _type); // chat long
            stream.Write((short) 0);
            stream.Write(0);
            // -------------
            stream.Write(""); // name
            return stream;
        }
    }
}