using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemSocketingLunastoneResultPacket : GamePacket
    {
        private readonly bool _result;
        private readonly ulong _itemId;
        private readonly uint _type;

        public SCItemSocketingLunastoneResultPacket(bool result, ulong itemId, uint type) : base(SCOffsets.SCItemSocketingLunastoneResultPacket, 5)
        {
            _result = result;
            _itemId = itemId;
            _type = type;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            stream.Write(_itemId);
            stream.Write(_type);
            return stream;
        }
    }
}
