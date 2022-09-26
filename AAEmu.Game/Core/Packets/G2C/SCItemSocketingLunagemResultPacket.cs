using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemSocketingLunagemResultPacket : GamePacket
    {
        private readonly byte _result;
        private readonly ulong _itemId;
        private readonly uint _type;
        private readonly bool _install;

        public SCItemSocketingLunagemResultPacket(byte result, ulong itemId, uint type, bool install) : base(SCOffsets.SCItemSocketingLunagemResultPacket, 5)
        {
            _result = result;
            _itemId = itemId;
            _type = type;
            _install = install;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            stream.Write(_itemId);
            stream.Write(_type);
            stream.Write(_install);
            return stream;
        }
    }
}
