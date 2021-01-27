using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeMoneyPutupPacket : GamePacket
    {
        private readonly int _moneyAmount;

        public SCTradeMoneyPutupPacket(int moneyAmount) : base(SCOffsets.SCTradeMoneyPutupPacket, 5)
        {
            _moneyAmount = moneyAmount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_moneyAmount);
            return stream;
        }
    }
}
