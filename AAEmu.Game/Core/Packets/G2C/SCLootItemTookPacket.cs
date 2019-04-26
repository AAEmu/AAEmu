using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCLootItemTookPacket : GamePacket
    {
        private readonly uint _id;
        private readonly ulong _iId;
        private readonly int _count;

        public SCLootItemTookPacket(uint id, ulong iId, int count) : base(SCOffsets.SCLootItemTookPacket,1)
        {
            _id = id;
            _iId = iId;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_iId);
            stream.Write(_count);
            return stream;
        }
    }
}
