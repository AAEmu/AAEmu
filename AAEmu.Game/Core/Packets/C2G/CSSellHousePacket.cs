using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellHousePacket : GamePacket
    {
        public CSSellHousePacket() : base(0x05e, 1) //TODO 1.0 opcode: 0x05c
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var moneyAmount = stream.ReadInt32();
            var sellTo = stream.ReadString();
            
            _log.Debug("SellHouse, Tl: {0}, MoneyAmount: {1}, SellTo: {2}", tl, moneyAmount, sellTo);
        }
    }
}
