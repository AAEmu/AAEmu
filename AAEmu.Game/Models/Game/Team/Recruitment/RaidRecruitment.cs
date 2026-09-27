using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Team.Recruitment;

/// <summary>A raid_recruit_types row (4 rows: dungeon, raid, influence_war, etc).</summary>
public sealed record RaidRecruitType(int Id, string Name, bool Visible, string IconKey);

/// <summary>
/// A raid_recruit_sub_types row (16 rows). Level and GearScore are the floors the post dialog starts the
/// limit fields from and refuses to go under (client dialog.lua, Satisfy).
/// </summary>
public sealed record RaidRecruitSubType(int Id, string Name, int TypeId, int Level, string Comment, int GearScore);

/// <summary>A raid_recruit_time_and_expenses row: the fee for a departure MinHour..MaxHour hours away.</summary>
public sealed record RaidRecruitExpenseBand(int Id, int MinHour, int MaxHour, long Expense);

/// <summary>Everything the board reads from content, so the rules can be tested with a hand-built set.</summary>
public sealed record RaidRecruitContent(
    IReadOnlyDictionary<int, RaidRecruitType> Types,
    IReadOnlyDictionary<int, RaidRecruitSubType> SubTypes,
    IReadOnlyList<int> Headcounts,
    IReadOnlyList<RaidRecruitExpenseBand> ExpenseBands,
    long MaxGearScoreToRecruit)
{
    public static readonly RaidRecruitContent Empty = new(
        new Dictionary<int, RaidRecruitType>(), new Dictionary<int, RaidRecruitSubType>(), [], [], 0);
}

/// <summary>
/// The nine values X2Team:RaidRecruitAdd(type, subType, headcount, limitLevel, autoJoin, msg, hour, minute,
/// limitGearPoint) carries; everything else in the wire record is the client's own copy of server state.
/// </summary>
public readonly record struct RaidRecruitPostRequest(
    int TypeId,
    int SubTypeId,
    uint Headcount,
    uint LimitLevel,
    bool AutoJoin,
    string Message,
    uint Hour,
    uint Minute,
    uint LimitGearPoint);

/// <summary>
/// One recruitment record as the client's shared record serializer lays it out
/// (432 bytes per entry; CSRaidRecruitAdd, SCRaidRecruitAdd, SCRaidApplicantAdd and each SCRaidRecruitList
/// row all delegate to it). The i32 at +0x9c has no name and no reader, so it is not modelled.
/// </summary>
public readonly record struct RaidRecruitRecord(
    ulong OwnerId,
    string OwnerName,
    byte OwnerLevel,
    byte OwnerAbility1,
    byte OwnerAbility2,
    byte OwnerAbility3,
    int OwnerExpeditionId,
    int TypeId,
    int SubTypeId,
    uint Headcount,
    uint LimitLevel,
    uint LimitGearPoint,
    bool AutoJoin,
    string Message,
    uint Hour,
    uint Minute,
    int ApplicantCount,
    int MemberCount,
    int LeadershipPoint,
    int GearPoint,
    long CreateTime,
    long ExpireTime,
    long AddExpireTime);

/// <summary>One SCRaidApplicantList row, serializer (160 bytes per entry).</summary>
public readonly record struct RaidApplicantRecord(
    ulong CharacterId,
    string Name,
    byte Level,
    byte Ability1,
    byte Ability2,
    byte Ability3,
    uint Role,
    int GearPoint);

public enum RaidApplicantState
{
    /// <summary>Applied, waiting for the recruiter.</summary>
    Pending,

    /// <summary>The recruiter approved; SCRaidApplicantAccept is out and the applicant's reply decides.</summary>
    Accepted
}

public sealed class RaidApplicant(Character character, uint role, long createTime)
{
    public Character Character { get; } = character;
    public uint CharacterId => Character.Id;
    public uint Role { get; } = role;
    public long CreateTime { get; } = createTime;
    public RaidApplicantState State { get; set; } = RaidApplicantState.Pending;

    public RaidApplicantRecord ToRecord() => new(
        Character.Id,
        Character.Name ?? string.Empty,
        Character.Level,
        (byte)Character.Ability1,
        (byte)Character.Ability2,
        (byte)Character.Ability3,
        Role,
        Character.GearScore);
}

/// <summary>
/// A live recruitment post. Keyed by the poster's character id on the wire (the client's RaidRecruitDetail,
/// RaidApplicantAdd and RaidApplicantDel all pass ownerId) and held only in memory: it is deleted when the
/// poster logs off or returns to character select (ui_texts 8858), so nothing survives a restart.
/// </summary>
public sealed class RaidRecruitment
{
    public required Character Owner { get; init; }
    public uint OwnerId => Owner.Id;

    /// <summary>
    /// The team the post recruits for; 0 while the poster is solo, set when the board seats their first
    /// applicant or when they join a team the post can recruit for.
    /// </summary>
    public uint TeamId { get; set; }

    public int TypeId { get; init; }
    public int SubTypeId { get; init; }
    public uint Headcount { get; init; }
    public uint LimitLevel { get; init; }
    public uint LimitGearPoint { get; init; }
    public bool AutoJoin { get; set; }
    public string Message { get; init; } = string.Empty;
    public uint Hour { get; init; }
    public uint Minute { get; init; }
    public long CreateTime { get; init; }
    public long ExpireTime { get; init; }

    /// <summary>In application order; SCRaidApplicantList sends the first hundred.</summary>
    public List<RaidApplicant> Applicants { get; } = [];

    public RaidApplicant FindApplicant(uint characterId)
    {
        foreach (var applicant in Applicants)
            if (applicant.CharacterId == characterId)
                return applicant;
        return null;
    }

    public int AcceptedPendingCount
    {
        get
        {
            var count = 0;
            foreach (var applicant in Applicants)
                if (applicant.State == RaidApplicantState.Accepted)
                    count++;
            return count;
        }
    }

    public RaidRecruitRecord ToRecord(int memberCount) => new(
        OwnerId,
        Owner.Name ?? string.Empty,
        Owner.Level,
        (byte)Owner.Ability1,
        (byte)Owner.Ability2,
        (byte)Owner.Ability3,
        (int)(Owner.Expedition?.Id ?? 0),
        TypeId,
        SubTypeId,
        Headcount,
        LimitLevel,
        LimitGearPoint,
        AutoJoin,
        Message,
        Hour,
        Minute,
        Applicants.Count,
        memberCount,
        Owner.LeadershipPoint,
        Owner.GearScore,
        CreateTime,
        ExpireTime,
        // addExpireTime is named by the serializer but never read by the client's list or detail builders;
        // its meaning is unknown, so it stays 0.
        0);
}
