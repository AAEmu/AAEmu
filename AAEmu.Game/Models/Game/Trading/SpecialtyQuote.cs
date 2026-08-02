using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// </summary>
public sealed class SpecialtyQuote : PacketMarshaler, IEquatable<SpecialtyQuote>
{
    public uint ItemId { get; set; }
    public ulong Refund { get; set; }
    public ulong NoEventRefund { get; set; }
    public uint Ratio { get; set; }
    public uint Stock { get; set; }
    public bool CanProduce { get; set; }
    public ShopCurrencyType Currency { get; set; }
    public sbyte Type { get; set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadUInt32();
        Refund = stream.ReadUInt64();
        NoEventRefund = stream.ReadUInt64();
        Ratio = stream.ReadUInt32();
        Stock = stream.ReadUInt32();
        CanProduce = stream.ReadBoolean();
        Currency = (ShopCurrencyType)stream.ReadByte();
        Type = stream.ReadSByte();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ItemId);
        stream.Write(Refund);
        stream.Write(NoEventRefund);
        stream.Write(Ratio);
        stream.Write(Stock);
        stream.Write(CanProduce);
        stream.Write((byte)Currency);
        stream.Write(Type);
        return stream;
    }

    public bool Equals(SpecialtyQuote other)
    {
        return other != null &&
               ItemId == other.ItemId &&
               Refund == other.Refund &&
               NoEventRefund == other.NoEventRefund &&
               Ratio == other.Ratio &&
               Stock == other.Stock &&
               CanProduce == other.CanProduce &&
               Currency == other.Currency &&
               Type == other.Type;
    }

    public override bool Equals(object obj) => Equals(obj as SpecialtyQuote);

    public override int GetHashCode()
    {
        return HashCode.Combine(ItemId, Refund, NoEventRefund, Ratio, Stock, CanProduce, Currency, Type);
    }
}
