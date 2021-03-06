using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemSocketingLunagemResultPacket : GamePacket
    {
        private readonly byte _result;
        private readonly ulong _itemId;
        private readonly uint _type;
        //private readonly bool _install;
        private readonly byte _kind;
        private readonly bool _success;

        public SCItemSocketingLunagemResultPacket(byte result, ulong itemId, uint type, bool success) : base(SCOffsets.SCItemSocketingLunagemResultPacket, 5)
        {
            _result = result;
            _itemId = itemId;
            _type = type;
            _kind = 0;
            _success = success;
            //_install = install;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            stream.Write(_itemId);
            stream.Write(_type);
            //stream.Write(_install); // removed in 2.0
            stream.Write(_kind); // added in 2.0
            stream.Write(_success); // added in 2.0
            return stream;
        }
    }
}
