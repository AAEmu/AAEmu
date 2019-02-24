using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPutupTradeMoneyPacket : GamePacket
    {
        public CSPutupTradeMoneyPacket() : base(0x0ee, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var moneyAmount = stream.ReadInt32();

            _log.Warn("PutupTradeMoney, MoneyAmount: {0}", moneyAmount);
        }
    }
}
