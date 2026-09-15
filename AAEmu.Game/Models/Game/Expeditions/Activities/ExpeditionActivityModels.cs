using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Expeditions.Activities;

public enum ExpeditionHistoryPage : sbyte
{
    Management = 1,
    Shop = 2,
    War = 3,
    Instance = 4
}

public sealed class ExpeditionManagementHistory(
    string MemberName,
    int Type,
    ulong Amount,
    DateTime UsedAt,
    uint DetailId,
    int DetailValue) : PacketMarshaler
{
    public string MemberName { get; } = MemberName;
    public int Type { get; } = Type;
    public ulong Amount { get; } = Amount;
    public DateTime UsedAt { get; } = UsedAt;
    public uint DetailId { get; } = DetailId;
    public int DetailValue { get; } = DetailValue;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(MemberName ?? string.Empty);
        stream.Write(Type);
        stream.Write(Amount);
        stream.Write(UsedAt);
        if (Type is 1 or 3)
        {
            stream.Write(DetailId);
            stream.Write(DetailValue);
        }
        return stream;
    }
}

public sealed class ExpeditionShopHistory(
    string MemberName,
    int Type,
    int Stack,
    ulong Amount,
    DateTime PurchasedAt) : PacketMarshaler
{
    public string MemberName { get; } = MemberName;
    public int Type { get; } = Type;
    public int Stack { get; } = Stack;
    public ulong Amount { get; } = Amount;
    public DateTime PurchasedAt { get; } = PurchasedAt;

    public override PacketStream Write(PacketStream stream) => stream
        .Write(MemberName ?? string.Empty)
        .Write(Type)
        .Write(Stack)
        .Write(Amount)
        .Write(PurchasedAt);
}

public sealed class ExpeditionWarHistory(
    int Type,
    string DeclarerName,
    int DefendantType,
    string DefendantName,
    DateTime DeclaredAt,
    uint DeclarerKills,
    uint DefendantKills) : PacketMarshaler
{
    public int Type { get; } = Type;
    public string DeclarerName { get; } = DeclarerName;
    public int DefendantType { get; } = DefendantType;
    public string DefendantName { get; } = DefendantName;
    public DateTime DeclaredAt { get; } = DeclaredAt;
    public uint DeclarerKills { get; } = DeclarerKills;
    public uint DefendantKills { get; } = DefendantKills;

    public override PacketStream Write(PacketStream stream) => stream
        .Write(Type)
        .Write(DeclarerName ?? string.Empty)
        .Write(DefendantType)
        .Write(DefendantName ?? string.Empty)
        .Write(DeclaredAt)
        .Write(DeclarerKills)
        .Write(DefendantKills);
}

public enum ExpeditionInstanceMemberStatus : byte
{
    Started = 0,
    Finished = 1
}

public enum ExpeditionInstancePlayResult : byte
{
    Lose = 1,
    Draw = 2,
    Win = 3
}

public sealed class ExpeditionInstanceHistoryMember(
    ulong HistoryId,
    ulong CharacterId,
    ExpeditionInstanceMemberStatus Status) : PacketMarshaler
{
    public ulong HistoryId { get; internal set; } = HistoryId;
    public ulong CharacterId { get; } = CharacterId;
    public ExpeditionInstanceMemberStatus Status { get; } = Status;

    public override PacketStream Write(PacketStream stream) => stream
        .Write(HistoryId)
        .Write(CharacterId)
        .Write((byte)Status);
}

public sealed class ExpeditionInstanceHistory : PacketMarshaler
{
    public ulong HistoryId { get; internal set; }
    /// <summary>
    /// The content-authored instance_rank_details.id serialized as the first native history
    /// <c>type</c>. The mapping from instances.id is loaded from instance_rank_details.
    /// </summary>
    public uint InstanceRankDetailId { get; init; }
    /// <summary>The content-authored instances.id used by the client to resolve the display name.</summary>
    public uint InstanceId { get; init; }
    public uint Score { get; init; }
    public ExpeditionInstancePlayResult PlayResult { get; init; }
    public DateTime RecordedAt { get; init; }
    public IReadOnlyList<ExpeditionInstanceHistoryMember> Members { get; internal set; } = [];

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(HistoryId)
            .Write(InstanceRankDetailId)
            .Write(InstanceId)
            .Write(Score)
            .Write((byte)PlayResult)
            .Write(RecordedAt)
            .Write((byte)Math.Min(50, Members.Count));
        foreach (var member in Members.Take(50))
            stream.Write(member);
        return stream;
    }
}

public sealed class ExpeditionInstanceRating(
    uint InstanceRankDetailId,
    uint Wins,
    uint Losses,
    uint Draws,
    uint Rating,
    uint BestRating) : PacketMarshaler
{
    public override PacketStream Write(PacketStream stream) => stream
        .Write(InstanceRankDetailId)
        .Write(Wins)
        .Write(Losses)
        .Write(Draws)
        .Write(Rating)
        .Write(BestRating);
}

public sealed class ExpeditionPortalPoint : PacketMarshaler
{
    public uint Id { get; set; }
    public uint ExpeditionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float ZRot { get; set; }

    public override PacketStream Write(PacketStream stream) => stream
        .Write(Id)
        .Write(Name ?? string.Empty)
        .Write(ZoneId)
        .Write(Helpers.ConvertLongX(X))
        .Write(Helpers.ConvertLongY(Y))
        .Write(Z)
        .Write(ZRot);
}
