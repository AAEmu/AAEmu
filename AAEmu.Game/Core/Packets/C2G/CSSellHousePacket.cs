using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSellHousePacket : GamePacket
    {
        public CSSellHousePacket() : base(CSOffsets.CSSellHousePacket, 1)
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
