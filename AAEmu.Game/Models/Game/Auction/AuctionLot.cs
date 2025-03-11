using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionLot : PacketMarshaler
{
    public ulong Id { get; set; }
    public AuctionDuration Duration { get; set; } // 8 is 6 hours, 9 is 12 hours, 10 is 24 hours, 11 is 48 hours
    public Item Item { get; set; }
    public DateTime EndTime { get; set; }
    public ulong TimeLeft { get => (ulong)EndTime.Subtract(DateTime.UtcNow).TotalSeconds; } // seconds left
    public byte WorldId { get; set; }
    public uint ClientId { get; set; }
    public string ClientName { get; set; }
    public int StartMoney { get; set; }
    public int DirectMoney { get; set; }
    public DateTime PostDate { get; set; }
    public int ChargePercent { get; set; }
    public byte BidWorldId { get; set; }
    public uint BidderId { get; set; }
    public string BidderName { get; set; }
    public int BidMoney { get; set; }
    public int Extra { get; set; }
    public int MinStack { get; set; }
    public int MaxStack { get; set; }
    public bool IsDirty { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt64();
        Duration = (AuctionDuration)stream.ReadByte();

        Item = new Item();
        Item.Read(stream);

        WorldId = stream.ReadByte();
        ClientId = stream.ReadUInt32();
        ClientName = stream.ReadString();
        StartMoney = stream.ReadInt32();
        DirectMoney = stream.ReadInt32();
        PostDate = DateTime.FromBinary(stream.ReadInt64());
        ChargePercent = stream.ReadInt32();
        BidWorldId = stream.ReadByte();
        BidderId = stream.ReadUInt32();
        BidderName = stream.ReadString();
        BidMoney = stream.ReadInt32();
        Extra = stream.ReadInt32();
        MinStack = stream.ReadInt32();
        MaxStack = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write((byte)Duration);

        stream.Write(Item);
        
        stream.Write(WorldId);
        stream.Write(ClientId);
        stream.Write(ClientName);
        stream.Write(StartMoney);
        stream.Write(DirectMoney);
        stream.Write(PostDate);
        stream.Write(ChargePercent);
        stream.Write(BidWorldId);
        stream.Write(BidderId);
        stream.Write(BidderName);
        stream.Write(BidMoney);
        stream.Write(Extra);
        stream.Write(MinStack);
        stream.Write(MaxStack);
        return stream;
    }
}
