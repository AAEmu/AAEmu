using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeavedChatChannelPacket : GamePacket
    {
        private long _chat;

        public SCLeavedChatChannelPacket(long chat) : base(0x0c5, 1)
        {
            _chat = chat;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_chat);
            return stream;
        }
    }
}
