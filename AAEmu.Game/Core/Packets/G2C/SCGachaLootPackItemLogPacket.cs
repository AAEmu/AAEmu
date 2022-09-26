using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGachaLootPackItemLogPacket : GamePacket
    {
        private readonly byte _lootLogCount;
        private readonly uint _id;
        private readonly byte _type;
        private readonly int _stack;

        public SCGachaLootPackItemLogPacket(byte lootLogCount, uint id, byte type, int stack) : base(SCOffsets.SCGachaLootPackItemLogPacket, 5)
        {
            _lootLogCount = lootLogCount;
            _id = id;
            _type = type;
            _stack = stack;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_lootLogCount);
            for (var i = 0; i < _lootLogCount; i++)
            {
                stream.Write(_id);
                stream.Write(_type);
                stream.Write(_stack);
            }
            return stream;
        }
    }
}
