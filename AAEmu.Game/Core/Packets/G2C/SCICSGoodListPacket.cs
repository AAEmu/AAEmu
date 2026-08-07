using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSGoodListPacket(IReadOnlyList<IcsMenu> entries) : GamePacket(SCOffsets.SCICSGoodListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)entries.Count);

        foreach (var entry in entries)
        {
            var item = entry.ShopItem;
            var sku = item.FirstSku;

            stream.Write(item.ShopId);                  // u32 cashShopId
            stream.Write(item.Name ?? "");              // wstring casnName
            stream.Write(entry.MainTab);                // u8 mainTab
            stream.Write(entry.SubTab);                 // u8 subTab
            stream.Write(item.LevelMin);                // u8 levelMin
            stream.Write(item.LevelMax);                // u8 levelMax
            stream.Write(item.DisplayItemId);           // i32 type (display item)
            stream.Write((byte)0);                      // u8 displayMode (unknown -> 0)
            stream.Write((byte)item.LimitedType);       // u8 limitType
            stream.Write(item.LimitedStockMax);         // u16 buyCount
            stream.Write((byte)item.BuyRestrictType);   // u8 buyType
            stream.Write(item.BuyRestrictId);           // u32 buyId
            stream.Write(item.SaleStart);               // i64 sdate
            stream.Write(item.SaleEnd);                 // i64 edate
            stream.Write((byte)(sku?.Currency ?? CashShopCurrencyType.Credits)); // u8 currency
            stream.Write(sku?.Price ?? 0u);             // u32 price
            stream.Write(item.Remaining);               // i32 remain
            stream.Write(sku?.BonusItemId ?? 0u);       // u32 bonusType
            stream.Write(sku?.BonusItemCount ?? 0u);    // u32 bonusCount
            stream.Write((byte)item.ShopButtons);       // u8 cmdUi
            stream.Write(0u);                           // u32 payItemType (unknown -> 0)
            stream.Write(sku?.DiscountPrice ?? 0u);     // u32 disPrice
        }

        return stream;
    }
}
