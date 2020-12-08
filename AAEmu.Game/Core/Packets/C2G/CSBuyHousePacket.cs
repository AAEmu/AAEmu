using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyHousePacket : GamePacket
    {
        public CSBuyHousePacket() : base(CSOffsets.CSBuyHousePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var moneyAmount = stream.ReadInt32();

            _log.Debug("BuyHouse, Tl: {0}, MoneyAmount: {1}", tl, moneyAmount);
        }
    }
}
