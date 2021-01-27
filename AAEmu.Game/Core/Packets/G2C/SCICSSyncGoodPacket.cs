using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSSyncGoodPacket : GamePacket
    {
        private readonly int _cashShopId;
        private readonly int _remainCount;
        
        public SCICSSyncGoodPacket(int cashShopId, int remainCount) : base(SCOffsets.SCICSSyncGoodPacket, 5)
        {
            _cashShopId = cashShopId;
            _remainCount = remainCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_cashShopId);
            stream.Write(_remainCount);
            return stream;
        }
    }
}
