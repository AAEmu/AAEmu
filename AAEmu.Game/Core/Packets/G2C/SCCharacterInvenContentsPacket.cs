using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterInvenContentsPacket : GamePacket
    {
        private readonly SlotType _type;
        private readonly byte _numChunks;
        private readonly byte _startChunkIdx;
        private readonly Item[] _items;

        public SCCharacterInvenContentsPacket(SlotType type, byte numChunks, byte startChunkIdx, Item[] items)
            : base(SCOffsets.SCCharacterInvenContentsPacket, 5)
        {
            _type = type;
            _numChunks = numChunks;
            _startChunkIdx = startChunkIdx;
            _items = items;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _type);
            stream.Write(_numChunks);
            stream.Write(_startChunkIdx);
            foreach (var item in _items)
            {
                if (item == null)
                    stream.Write(0);
                else
                    stream.Write(item);
            }

            return stream;
        }
    }
}
