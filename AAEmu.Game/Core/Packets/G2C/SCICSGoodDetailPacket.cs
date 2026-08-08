using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes batched Marketplace SKU details.</summary>
public class SCICSGoodDetailPacket : GamePacket
{
    private readonly IReadOnlyList<IcsSku> _skus;

    public SCICSGoodDetailPacket(IReadOnlyList<IcsSku> skus) : base(SCOffsets.SCICSGoodDetailPacket, 1)
    {
        _skus = skus;
    }

    // Compatibility: one sku + pageEnd flag (pageEnd ignored — batch end is implicit).
    public SCICSGoodDetailPacket(bool pageEnd, IcsSku itemDetail)
        : this(itemDetail is null ? Array.Empty<IcsSku>() : [itemDetail])
    {
        _ = pageEnd;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = (ushort)Math.Min(_skus.Count, 1170);
        var body = new PacketStream();
        body.Write(count);
        for (var i = 0; i < count; i++)
            WriteEntry(body, _skus[i]);
        var bytes = body.GetBytes();
        Logger.Info("ICSDetails body len={0} hex={1}", bytes.Length, BitConverter.ToString(bytes).Replace("-", ""));
        stream.Write(bytes, false);
        return stream;
    }

    private static void WriteEntry(PacketStream stream, IcsSku sku)
    {
        stream.Write(sku.ShopId); // cashShopId
        stream.Write(sku.Sku); // cashUniqId
        stream.Write((int)sku.ItemId); // type (item id)
        stream.Write(sku.ItemCount);
        stream.Write(sku.IsDefault); // defaultFlag u8
        stream.Write(sku.EventType);
        stream.Write(DateToUnix(sku.EventEndDate)); // eventDate i64
        stream.Write((byte)sku.Currency); // priceType
        stream.Write(sku.Price);
        stream.Write(sku.DiscountPrice);
        stream.Write(sku.BonusItemId);
        stream.Write(sku.BonusItemCount);
        stream.Write(0u); // payItemType
    }

    private static long DateToUnix(DateTime dt)
    {
        if (dt == DateTime.MinValue || dt.Year < 1971)
            return 0;
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeSeconds();
    }
}
