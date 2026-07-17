using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodDetailPacket(bool pageEnd, IcsSku itemDetail) : GamePacket(SCOffsets.SCICSGoodDetailPacket, 5)
{
    // private readonly CashShopItemDetail _itemDetail;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(pageEnd);

        // stream.Write(_itemDetail); // replaced by new code
        stream.Write(itemDetail.ShopId);
        stream.Write(itemDetail.Sku);
        stream.Write(itemDetail.ItemId);
        stream.Write(itemDetail.ItemCount);
        stream.Write(itemDetail.SelectType);
        stream.Write(itemDetail.IsDefault);
        stream.Write(itemDetail.EventType);
        stream.Write(itemDetail.EventEndDate);
        stream.Write((byte)itemDetail.Currency);
        stream.Write(itemDetail.Price);
        stream.Write(itemDetail.DiscountPrice);
        stream.Write(itemDetail.BonusItemId);
        stream.Write(itemDetail.BonusItemCount);

        return stream;
    }
}
