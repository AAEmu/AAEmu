using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

/// <summary>
/// One day of sold-price history. The sold-record search always writes fourteen of these.
/// </summary>
public class AuctionSoldRecord : PacketMarshaler
{
    public int Day { get; set; }
    public uint ItemTemplateId { get; set; }
    public byte Grade { get; set; }
    public long MinPrice { get; set; }
    public long MaxPrice { get; set; }
    public long AveragePrice { get; set; }
    public long LastPrice { get; set; }
    public int Volume { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ItemTemplateId);
        stream.Write(Day);
        stream.Write(MinPrice);
        stream.Write(MaxPrice);
        stream.Write(AveragePrice);
        stream.Write(Volume);
        stream.Write(Grade);
        stream.Write(LastPrice);
        return stream;
    }

    public override void Read(PacketStream stream)
    {
        ItemTemplateId = stream.ReadUInt32();
        Day = stream.ReadInt32();
        MinPrice = stream.ReadInt64();
        MaxPrice = stream.ReadInt64();
        AveragePrice = stream.ReadInt64();
        Volume = stream.ReadInt32();
        Grade = stream.ReadByte();
        LastPrice = stream.ReadInt64();
    }
}
