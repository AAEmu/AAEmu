using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodsDetailPacket : GamePacket
{
    private readonly bool _pageEnd;
    //private readonly CashShopItemDetail _itemDetail;

    public SCICSGoodsDetailPacket(bool pageEnd/*, CashShopItemDetail itemDetail*/) : base(SCOffsets.SCICSGoodListPacket, 5)
    //public SCICSGoodsDetailPacket(bool pageEnd, CashShopItemDetail itemDetail) : base(SCOffsets.SCICSGoodListPacket, 5)
    {
        _pageEnd = pageEnd;
        //_itemDetail = itemDetail;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pageEnd);
        //stream.Write(_itemDetail);
        return stream;
    }
}