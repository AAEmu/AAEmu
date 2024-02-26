using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodDetailPacket : GamePacket
{
    private readonly bool _pageEnd;
    // private readonly CashShopItemDetail _itemDetail;
    private readonly IcsSku _sku;

    public SCICSGoodDetailPacket(bool pageEnd, IcsSku itemDetail) : base(SCOffsets.SCICSGoodDetailPacket, 1)
    {
        _pageEnd = pageEnd;
        _sku = itemDetail;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pageEnd);

        // stream.Write(_itemDetail); // replaced by new code
        stream.Write(_sku.ShopId);
        stream.Write(_sku.Sku);
        stream.Write(_sku.ItemId);
        stream.Write(_sku.ItemCount);
        stream.Write(_sku.SelectType);
        stream.Write(_sku.IsDefault);
        stream.Write(_sku.EventType);
        stream.Write(_sku.EventEndDate);
        stream.Write((byte)_sku.Currency);
        stream.Write(_sku.Price);
        stream.Write(_sku.DiscountPrice);
        stream.Write(_sku.BonusItemId);
        stream.Write(_sku.BonusItemCount);

        return stream;
    }
}
