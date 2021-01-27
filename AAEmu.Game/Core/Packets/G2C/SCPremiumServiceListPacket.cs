using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPremiumServiceListPacket : GamePacket
    {
        private readonly bool _isEnd;
        private readonly byte _size;
        private readonly PremiumDetail _detail;
        private readonly int _exchangeRatio;
        
        public SCPremiumServiceListPacket(bool isEnd, byte size, PremiumDetail detail, int exchangeRatio) : base(SCOffsets.SCPremiumServiceListPacket, 5)
        {
            _isEnd = isEnd;
            _size = size;
            _detail = detail;
            _exchangeRatio = exchangeRatio;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isEnd);
            stream.Write(_size);
            stream.Write(_detail);
            stream.Write(_exchangeRatio);
            return stream;
        }
    }
}
