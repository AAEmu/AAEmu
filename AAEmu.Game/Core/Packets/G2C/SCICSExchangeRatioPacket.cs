using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSExchangeRatioPacket : GamePacket
    {
        private readonly int _exchangeRatio;

        public SCICSExchangeRatioPacket(int exchangeRatio) : base(SCOffsets.SCICSExchangeRatioPacket, 5)
        {
            _exchangeRatio = exchangeRatio;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_exchangeRatio);
            return stream;
        }
    }
}
