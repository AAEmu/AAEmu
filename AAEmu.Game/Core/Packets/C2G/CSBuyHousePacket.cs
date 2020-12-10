using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
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
            var moneyAmount = stream.ReadUInt32();

            HousingManager.Instance.BuyHouse(tl, moneyAmount, Connection.ActiveChar);
        }
    }
}
