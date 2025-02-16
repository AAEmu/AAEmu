using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Auction;

public class AuctionItem
{
    public ulong Id { get; set; }
    public byte Duration { get; set; }
    public uint ItemId { get; set; }
    public ulong ObjectId { get; set; }
    public byte Grade { get; set; }
    public ItemFlag Flags { get; set; }
    public uint StackSize { get; set; }
    public byte DetailType { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime EndTime { get; set; }
    public uint LifespanMins { get; set; }
    public uint MadeUnitId { get; set; }
    public byte WorldId { get; set; }
    public DateTime UnsecureDateTime { get; set; }
    public DateTime UnpackDateTime { get; set; }
    public DateTime ChargeUseSkillTime { get; set; }
    public byte WorldId2 { get; set; }
    public uint ClientId { get; set; }
    public string ClientName { get; set; }
    public int StartMoney { get; set; }
    public int DirectMoney { get; set; }
    public ulong TimeLeft
    {
        get => (ulong)EndTime.Subtract(DateTime.UtcNow).TotalSeconds;
        set => throw new NotImplementedException();
    } //seconds
    public byte BidWorldId { get; set; }
    public int ChargePercent { get; set; }
    public uint BidderId { get; set; }
    public string BidderName { get; set; }
    public int BidMoney { get; set; }
    public uint Extra { get; set; }
    public uint MinStack { get; set; }
    public uint MaxStack { get; set; }
    public bool IsDirty { get; set; }

    public void Read(PacketStream stream)
    {
        Id = stream.ReadUInt64();
        Duration = stream.ReadByte();
        ItemId = stream.ReadUInt32();
        ObjectId = stream.ReadUInt64(); // item id
        Grade = stream.ReadByte();
        Flags = (ItemFlag)stream.ReadByte();
        StackSize = stream.ReadUInt32();
        DetailType = stream.ReadByte();
        // TODO если DetailType > 0, то надо добавить ReadDetails()
        CreationTime = stream.ReadDateTime();
        LifespanMins = stream.ReadUInt32();
        MadeUnitId = stream.ReadUInt32();
        WorldId = stream.ReadByte();
        UnsecureDateTime = stream.ReadDateTime();
        UnpackDateTime = stream.ReadDateTime();
        ChargeUseSkillTime = stream.ReadDateTime(); // added in 5+

        WorldId2 = stream.ReadByte();
        ClientId = stream.ReadUInt32();
        ClientName = stream.ReadString();
        StartMoney = stream.ReadInt32();
        DirectMoney = stream.ReadInt32();
        TimeLeft = stream.ReadUInt64(); // asked
        ChargePercent = stream.ReadInt32(); // added in 5+
        BidWorldId = stream.ReadByte();
        BidderId = stream.ReadUInt32();
        BidderName = stream.ReadString();
        BidMoney = stream.ReadInt32();
        Extra = stream.ReadUInt32();
        MinStack = stream.ReadUInt32(); // added in 5+
        MaxStack = stream.ReadUInt32(); // added in 5+
    }

    public PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(Duration);
        stream.Write(ItemId);
        stream.Write(ObjectId); // item id
        stream.Write(Grade);
        stream.Write((byte)Flags);
        stream.Write(StackSize);
        stream.Write(DetailType);
        // TODO если DetailType > 0, то надо добавить WriteDetails()
        stream.Write(DateTime.UtcNow); // creationTime
        stream.Write(LifespanMins);
        stream.Write(MadeUnitId);
        stream.Write(WorldId);
        stream.Write(DateTime.UtcNow); // unsecureDateTime
        stream.Write(DateTime.UtcNow); // unpackDateTime
        stream.Write(DateTime.UtcNow); // ChargeUseSkillTime added 5+
        stream.Write(WorldId2);
        stream.Write(ClientId);
        stream.Write(ClientName);
        stream.Write(StartMoney);
        stream.Write(DirectMoney);
        var Random = new Random();
        var offsett = TimeLeft + (ulong)Random.Next(0, 10);
        stream.Write(offsett);
        stream.Write(ChargePercent); // added in 5+
        stream.Write(BidWorldId);
        stream.Write(BidderId);
        stream.Write(BidderName);
        stream.Write(BidMoney);
        stream.Write(Extra);
        stream.Write(MinStack); // added in 5+
        stream.Write(MaxStack); // added in 5+
        return stream;

    }
}
