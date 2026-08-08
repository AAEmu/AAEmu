using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes a batched Marketplace listing.</summary>
public class SCICSGoodListPacket : GamePacket
{
    private readonly byte _mainTab;
    private readonly byte _subTab;
    private readonly IReadOnlyList<IcsItem> _items;

    public SCICSGoodListPacket(byte mainTab, byte subTab, IReadOnlyList<IcsItem> items)
        : base(SCOffsets.SCICSGoodListPacket, 1)
    {
        _mainTab = mainTab;
        _subTab = subTab;
        _items = items;
    }

    // Compatibility ctor used by older call sites (single item / pageEnd) — packs as 1-count batch.
    public SCICSGoodListPacket(bool pageEnd, ushort totalPage, byte mainTab, byte subTab, IcsItem item)
        : this(mainTab, subTab, item is null ? Array.Empty<IcsItem>() : [item])
    {
        _ = pageEnd;
        _ = totalPage;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = (ushort)Math.Min(_items.Count, 50);
        var body = new PacketStream();
        body.Write(count);
        for (var i = 0; i < count; i++)
            WriteEntry(body, _items[i]);
        var bytes = body.GetBytes();
        Logger.Info("ICSGoods body len={0} hex={1}", bytes.Length, BitConverter.ToString(bytes).Replace("-", ""));
        stream.Write(bytes, false);
        return stream;
    }

    private void WriteEntry(PacketStream stream, IcsItem item)
    {
        var sku = item.FirstSku;
        stream.Write(item.ShopId); // cashShopId u32
        stream.Write(item.Name ?? string.Empty); // casnName (u16 len + utf8)
        // Marketplace tab identifiers are one-based.
        stream.Write(_mainTab);
        stream.Write(_subTab);
        stream.Write(item.LevelMin);
        stream.Write(item.LevelMax);
        // "type" i32 — display item id for icon/mesh
        stream.Write((int)(item.DisplayItemId != 0 ? item.DisplayItemId : sku?.ItemId ?? 0));
        // Zero is reserved for hidden entries; normal entries use a positive display mode.
        stream.Write(ResolveDisplayMode(item));
        stream.Write((byte)item.LimitedType); // limitType
        stream.Write(item.LimitedStockMax); // buyCount u16
        stream.Write((byte)item.BuyRestrictType); // buyType
        stream.Write(item.BuyRestrictId); // buyId u32
        stream.Write(DateToUnix(item.SaleStart)); // sdate i64
        stream.Write(DateToUnix(item.SaleEnd)); // edate i64
        stream.Write((byte)(sku?.Currency ?? CashShopCurrencyType.Credits)); // currency/type
        stream.Write(sku?.Price ?? 0u); // price
        // A negative remaining count represents unlimited stock.
        stream.Write(item.Remaining);
        stream.Write(sku?.BonusItemId ?? 0u); // bonusType
        stream.Write(sku?.BonusItemCount ?? 0u); // bonusConut
        stream.Write((byte)item.ShopButtons); // cmdUi
        stream.Write(0u); // payItemType
        stream.Write(sku?.DiscountPrice ?? 0u); // disPrice
    }

    /// <summary>
    /// Resolves hidden, normal, and sale display modes.
    /// </summary>
    private static byte ResolveDisplayMode(IcsItem item)
    {
        if (item.IsHidden)
            return 0;
        if (item.IsSale)
            return 2;
        return 1;
    }

    private static long DateToUnix(DateTime dt)
    {
        if (dt == DateTime.MinValue || dt.Year < 1971)
            return 0;
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeSeconds();
    }
}
