using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSGoodDetailPacket : GamePacket
    {
        private readonly bool _pageEnd;
        private readonly CashShopItemDetail _itemDetail;
        
        public SCICSGoodDetailPacket(bool pageEnd, CashShopItemDetail itemDetail) : base(SCOffsets.SCICSGoodDetailPacket, 5)
        {
            _pageEnd = pageEnd;
            _itemDetail = itemDetail;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_pageEnd);
            stream.Write(_itemDetail);
            return stream;
        }
    }
}
