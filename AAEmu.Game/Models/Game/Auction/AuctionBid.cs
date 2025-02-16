using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionBid : PacketMarshaler
{
    public ulong LotId { get; set; }
    public byte WorldId { get; set; }
    public uint BidderId { get; set; }
    public string BidderName { get; set; }
    public int Money { get; set; }
    public int StackSize { get; set; }

    public override void Read(PacketStream stream)
    {
        LotId = stream.ReadUInt64();
        WorldId = stream.ReadByte();
        BidderId = stream.ReadUInt32();
        BidderName = stream.ReadString();
        Money = stream.ReadInt32();
        StackSize = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(LotId);
        stream.Write(WorldId);
        stream.Write(BidderId);
        stream.Write(BidderName);
        stream.Write(Money);
        stream.Write(StackSize);
        return stream;
    }
}
