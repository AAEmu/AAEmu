using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatBubblePacket : GamePacket
    {
        private readonly uint _bc;
        private readonly byte _kind1;
        private readonly byte _kind2;
        private readonly uint _type;
        private readonly string _text;

        public SCChatBubblePacket(uint bc, byte kind1, byte kind2, uint type, string text) : base(SCOffsets.SCChatBubblePacket, 1)
        {
            _bc = bc;
            _kind1 = kind1;
            _kind2 = kind2;
            _type = type;
            _text = text;
            _text = text;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_bc);
            stream.Write(_kind1);
            stream.Write(_kind2);
            stream.Write(_type);
            stream.Write(_text);

            return stream;
        }
    }
}

