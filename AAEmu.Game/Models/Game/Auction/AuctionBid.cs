using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionBid : PacketMarshaler
{
    public ulong LotId { get; set; }
    public byte WorldId { get; set; }
    public uint BidderId { get; set; }
    public string BidderName { get; set; }
    public int Money { get; set; }
    public uint StackSize { get; set; }

    public override void Read(PacketStream stream)
    {
        LotId = stream.ReadUInt64();
        WorldId = stream.ReadByte();
        BidderId = (uint)stream.ReadUInt64();
        BidderName = stream.ReadString();
        Money = (int)stream.ReadUInt64();
        StackSize = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 widens the bidder id and the money to u64 and reads a stack size after them; 1.2 sent
        // both as u32 and omitted the stack, so every field past the bidder id was misaligned.
        stream.Write(LotId);
        stream.Write(WorldId);
        stream.Write((ulong)BidderId);
        stream.Write(BidderName);
        stream.Write((ulong)Money);
        stream.Write(StackSize);
        return stream;
    }
}
