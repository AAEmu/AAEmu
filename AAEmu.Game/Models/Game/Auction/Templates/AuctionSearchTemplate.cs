using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction.Templates;

public class AuctionSearch : PacketMarshaler
{
    public string Keyword { get; set; } = string.Empty;
    public bool ExactMatch { get; set; }
    public byte Grade { get; set; }
    public byte CategoryA { get; set; }
    public byte CategoryB { get; set; }
    public byte CategoryC { get; set; }
    public int Page { get; set; }
    public ulong ClientId { get; set; }
    public int Filter { get; set; }
    public int ItemListCount { get; set; }
    public byte WorldId { get; set; }
    public sbyte MinItemLevel { get; set; }
    public sbyte MaxItemLevel { get; set; }
    public long MinPrice { get; set; }
    public long MaxPrice { get; set; }
    public AuctionSearchSortKind SortKind { get; set; }
    public AuctionSearchSortOrder SortOrder { get; set; }
    public List<uint> ItemTemplateIds { get; set; } = [];

    public override void Read(PacketStream stream)
    {
        Keyword = stream.ReadString();
        ExactMatch = stream.ReadBoolean();
        Grade = stream.ReadByte();
        CategoryA = stream.ReadByte();
        CategoryB = stream.ReadByte();
        CategoryC = stream.ReadByte();
        Page = stream.ReadInt32();
        ClientId = stream.ReadUInt64();
        Filter = stream.ReadInt32();
        ItemListCount = stream.ReadInt32();
        WorldId = stream.ReadByte();
        MinItemLevel = stream.ReadSByte();
        MaxItemLevel = stream.ReadSByte();
        MinPrice = stream.ReadInt64();
        MaxPrice = stream.ReadInt64();
        SortKind = (AuctionSearchSortKind)stream.ReadByte();
        SortOrder = (AuctionSearchSortOrder)stream.ReadByte();
    }

    public void ReadItemTemplateIds(PacketStream stream)
    {
        var count = stream.ReadInt32();
        if (count > AuctionHouseRules.MultilingualItemIdLimit)
            count = AuctionHouseRules.MultilingualItemIdLimit;
        ItemListCount = count;
        ItemTemplateIds = new List<uint>(count);
        for (var i = 0; i < count; i++)
            ItemTemplateIds.Add(stream.ReadUInt32());
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Keyword ?? string.Empty);
        stream.Write(ExactMatch);
        stream.Write(Grade);
        stream.Write(CategoryA);
        stream.Write(CategoryB);
        stream.Write(CategoryC);
        stream.Write(Page);
        stream.Write(ClientId);
        stream.Write(Filter);
        stream.Write(ItemListCount);
        stream.Write(WorldId);
        stream.Write(MinItemLevel);
        stream.Write(MaxItemLevel);
        stream.Write(MinPrice);
        stream.Write(MaxPrice);
        stream.Write((byte)SortKind);
        stream.Write((byte)SortOrder);
        return stream;
    }
}
