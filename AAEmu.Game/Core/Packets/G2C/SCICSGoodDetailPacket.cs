using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodDetailPacket(IReadOnlyList<IcsSku> details) : GamePacket(SCOffsets.SCICSGoodDetailPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)details.Count);

        foreach (var sku in details)
        {
            stream.Write(sku.ShopId);           // u32 cashShopId
            stream.Write(sku.Sku);              // u32 cashUniqId (SKU)
            stream.Write(sku.ItemId);           // i32 type (item)
            stream.Write(sku.ItemCount);        // u32 itemCount
            stream.Write(sku.IsDefault);        // u8 defaultFlag
            stream.Write(sku.EventType);        // u8 eventType
            stream.Write(sku.EventEndDate);     // i64 eventDate
            stream.Write((byte)sku.Currency);   // u8 priceType
            stream.Write(sku.Price);            // u32 price
            stream.Write(sku.DiscountPrice);    // u32 disPrice
            stream.Write(sku.BonusItemId);      // u32 bonusType
            stream.Write(sku.BonusItemCount);   // u32 bonusCount
            stream.Write(0u);                   // u32 payItemType (unknown -> 0)
        }

        return stream;
    }
}
