using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionSearch : PacketMarshaler
{
    public string Keyword { get; set; }
    public bool ExactMatch { get; set; }
    public byte Grade { get; set; }
    public byte CategoryA { get; set; }
    public byte CategoryB { get; set; }
    public byte CategoryC { get; set; }
    public uint ClientId { get; set; }
    public int Page { get; set; }
    public int Filter { get; set; }
    public byte WorldId { get; set; }
    public byte MinItemLevel { get; set; }
    public byte MaxItemLevel { get; set; }
    public uint MinMoneyAmount { get; set; }
    public uint MaxMoneyAmount { get; set; }
    public AuctionSearchSortKind SortKind { get; set; }
    public AuctionSearchSortOrder SortOrder { get; set; }


    public override void Read(PacketStream stream)
    {
        Keyword = stream.ReadString();
        ExactMatch = stream.ReadBoolean();
        Grade = stream.ReadByte();
        CategoryA = stream.ReadByte();
        CategoryB = stream.ReadByte();
        CategoryC = stream.ReadByte();
        Page = stream.ReadInt32();
        ClientId = stream.ReadUInt32();
        Filter = stream.ReadInt32();
        WorldId = stream.ReadByte();
        MinItemLevel = stream.ReadByte();
        MaxItemLevel = stream.ReadByte();
        MinMoneyAmount = stream.ReadUInt32(); // moneyAmount
        MaxMoneyAmount = stream.ReadUInt32(); // moneyAmount
        SortKind = (AuctionSearchSortKind)stream.ReadByte();
        SortOrder = (AuctionSearchSortOrder)stream.ReadByte();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Keyword);
        stream.Write(ExactMatch);
        stream.Write(Grade);
        stream.Write(CategoryA);
        stream.Write(CategoryB);
        stream.Write(CategoryC);
        stream.Write(Page);
        stream.Write(ClientId);
        stream.Write(Filter);
        stream.Write(WorldId);
        stream.Write(MinItemLevel);
        stream.Write(MaxItemLevel);
        stream.Write(MinMoneyAmount); // moneyAmount
        stream.Write(MaxMoneyAmount); // moneyAmount
        stream.Write((byte)SortKind);
        stream.Write((byte)SortOrder);
        return stream;
    }
}
