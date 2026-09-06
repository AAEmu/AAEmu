using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionBid : PacketMarshaler
{
    public ulong LotId { get; set; }
    public byte WorldId { get; set; }
    public ulong BidderId { get; set; }
    public string BidderName { get; set; } = string.Empty;
    public long Money { get; set; }
    public int StackSize { get; set; }

    public override void Read(PacketStream stream)
    {
        LotId = stream.ReadUInt64();
        WorldId = stream.ReadByte();
        BidderId = stream.ReadUInt64();
        BidderName = stream.ReadString();
        Money = stream.ReadInt64();
        StackSize = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(LotId);
        stream.Write(WorldId);
        stream.Write(BidderId);
        stream.Write(BidderName ?? string.Empty);
        stream.Write(Money);
        stream.Write(StackSize);
        return stream;
    }
}
