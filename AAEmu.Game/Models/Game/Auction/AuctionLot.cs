using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Items;

using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.Auction;

[JsonObject(MemberSerialization.OptIn)]
public class AuctionLot : PacketMarshaler
{
    [JsonProperty]
    public ulong Id { get; set; }

    [JsonProperty]
    public AuctionDuration Duration { get; set; }

    [JsonProperty]
    public Item Item { get; set; }

    [JsonProperty]
    public DateTime EndTime { get; set; }

    [JsonProperty]
    public ulong TimeLeft => (ulong)Math.Max(0, EndTime.Subtract(DateTime.UtcNow).TotalSeconds);

    [JsonProperty]
    public byte WorldId { get; set; }

    [JsonProperty]
    public uint ClientId { get; set; }

    [JsonProperty]
    public string ClientName { get; set; } = string.Empty;

    [JsonProperty]
    public long StartMoney { get; set; }

    [JsonProperty]
    public long DirectMoney { get; set; }

    [JsonProperty]
    public DateTime PostDate { get; set; }

    [JsonProperty]
    public ulong Asked { get; set; }

    [JsonProperty]
    public int ChargePercent { get; set; }

    [JsonProperty]
    public int DepositPercent { get; set; }

    [JsonProperty]
    public byte ServiceKind { get; set; }

    [JsonProperty]
    public byte BidWorldId { get; set; } = AuctionHouseRules.UnsetWorldId;

    [JsonProperty]
    public uint BidderId { get; set; }

    [JsonProperty]
    public string BidderName { get; set; } = string.Empty;

    [JsonProperty]
    public long BidMoney { get; set; }

    [JsonProperty]
    public long ExtraMoney { get; set; }

    [JsonProperty]
    public int MinStack { get; set; } = 1;

    [JsonProperty]
    public int MaxStack { get; set; } = 1;

    [JsonIgnore]
    public bool IsDirty { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt64();
        Duration = (AuctionDuration)stream.ReadByte();
        Item = new Item(0);
        Item.Read(stream);
        WorldId = stream.ReadByte();
        ClientId = (uint)stream.ReadUInt64();
        ClientName = stream.ReadString();
        StartMoney = stream.ReadInt64();
        DirectMoney = stream.ReadInt64();
        Asked = stream.ReadUInt64();
        ChargePercent = stream.ReadInt32();
        DepositPercent = stream.ReadInt32();
        ServiceKind = stream.ReadByte();
        BidWorldId = stream.ReadByte();
        BidderId = (uint)stream.ReadUInt64();
        BidderName = stream.ReadString();
        BidMoney = stream.ReadInt64();
        ExtraMoney = stream.ReadInt64();
        MinStack = stream.ReadInt32();
        MaxStack = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write((byte)Duration);
        if (Item == null)
            stream.Write(0u);
        else
            stream.Write(Item);
        stream.Write(WorldId);
        stream.Write((ulong)ClientId);
        stream.Write(ClientName ?? string.Empty);
        stream.Write(StartMoney);
        stream.Write(DirectMoney);
        stream.Write(Asked != 0 ? Asked : (ulong)Helpers.UnixTime(PostDate));
        stream.Write(ChargePercent);
        stream.Write(DepositPercent);
        stream.Write(ServiceKind);
        stream.Write(BidWorldId);
        stream.Write((ulong)BidderId);
        stream.Write(BidderName ?? string.Empty);
        stream.Write(BidMoney);
        stream.Write(ExtraMoney);
        stream.Write(MinStack);
        stream.Write(MaxStack);
        return stream;
    }
}
