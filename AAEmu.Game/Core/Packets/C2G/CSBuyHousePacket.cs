using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyHousePacket : GamePacket
    {
        public CSBuyHousePacket() : base(0x060, 1) //TODO 1.0 opcode: 0x05e
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
