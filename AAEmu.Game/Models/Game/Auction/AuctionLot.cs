using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.Auction;

[JsonObject(MemberSerialization.OptIn)]
public class AuctionLot : PacketMarshaler
{
    [JsonProperty]
    public ulong Id { get; set; }

    [JsonProperty]
    public AuctionDuration Duration { get; set; } // 0 is 6 hours, 1 is 12 hours, 2 is 24 hours, 3 is 48 hours

    [JsonProperty]
    public Item Item { get; set; }

    [JsonProperty]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Seconds left
    /// </summary>
    [JsonProperty]
    public ulong TimeLeft { get => (ulong)EndTime.Subtract(DateTime.UtcNow).TotalSeconds; }

    [JsonProperty]
    public byte WorldId { get; set; }

    [JsonProperty]
    public uint ClientId { get; set; }

    [JsonProperty]
    public string ClientName { get; set; }

    [JsonProperty]
    public int StartMoney { get; set; }

    [JsonProperty]
    public int DirectMoney { get; set; }

    [JsonProperty]
    public DateTime PostDate { get; set; }

    // [JsonProperty]
    // public int ChargePercent { get; set; } // added in 3+

    [JsonProperty]
    public byte BidWorldId { get; set; }

    [JsonProperty]
    public uint BidderId { get; set; }

    [JsonProperty]
    public string BidderName { get; set; }

    [JsonProperty]
    public int BidMoney { get; set; }

    [JsonProperty]
    public int Extra { get; set; }

    // [JsonProperty]
    // public int MinStack { get; set; } // added in 3+

    // [JsonProperty]
    // public int MaxStack { get; set; } // added in 3+

    [JsonIgnore]
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
        //ChargePercent = stream.ReadInt32();
        BidWorldId = stream.ReadByte();
        BidderId = stream.ReadUInt32();
        BidderName = stream.ReadString();
        BidMoney = stream.ReadInt32();
        Extra = stream.ReadInt32();
        //MinStack = stream.ReadInt32();
        //MaxStack = stream.ReadInt32();
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
        //stream.Write(ChargePercent);
        stream.Write(BidWorldId);
        stream.Write(BidderId);
        stream.Write(BidderName);
        stream.Write(BidMoney);
        stream.Write(Extra);
        //stream.Write(MinStack);
        //stream.Write(MaxStack);
        return stream;
    }
}
