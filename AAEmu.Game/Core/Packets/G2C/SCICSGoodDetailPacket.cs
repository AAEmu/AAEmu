using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodDetailPacket : GamePacket
{
    private readonly bool _pageEnd;
    // private readonly CashShopItemDetail _itemDetail;
    private readonly IcsSku _sku;

    public SCICSGoodDetailPacket(bool pageEnd, IcsSku itemDetail) : base(SCOffsets.SCICSGoodDetailPacket, 5)
    {
        _pageEnd = pageEnd;
        _sku = itemDetail;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pageEnd);

        // stream.Write(_itemDetail); // replaced by new code
        stream.Write(_sku.ShopId); // cashShopId
        stream.Write(_sku.Sku);    // cashUniqId
        stream.Write(_sku.ItemId); // ItemTemplateId
        stream.Write(_sku.ItemCount);  // itemCount
        stream.Write(_sku.SelectType); // selectType
        stream.Write(_sku.IsDefault);  // defaultFlag
        stream.Write(_sku.EventType);  // eventType
        stream.Write(_sku.EventEndDate); // eventDate
        stream.Write((byte)_sku.Currency); // priceType
        stream.Write(_sku.Price);          // price
        stream.Write(_sku.DiscountPrice);  // disPrice
        stream.Write(_sku.BonusItemId);    // bonusType
        stream.Write(_sku.BonusItemCount); // bonusCount
        stream.Write(_sku.PayItemType);    // payItemType add in 3+

        return stream;
    }
}
