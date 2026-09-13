namespace AAEmu.Game.Models.Game.Expeditions.Recruitment;

using AAEmu.Game.Models.StaticValues;

public sealed record ExpeditionRecruitment(
    uint ExpeditionId,
    short InterestMask,
    string Introduction,
    DateTime RegisteredAt,
    DateTime ExpiresAt);

public sealed record ExpeditionRecruitmentApplication(
    uint ExpeditionId,
    uint CharacterId,
    string Memo,
    DateTime RegisteredAt);

public sealed record ExpeditionJoinCandidate(
    uint CharacterId,
    uint AccountId,
    string Name,
    byte Level,
    byte HeirLevel,
    FactionsEnum FactionId,
    byte Ability1,
    byte Ability2,
    byte Ability3,
    int ExpeditionId,
    long ExpeditionRejoinUntil);

public sealed record ExpeditionRecruitmentQuery(
    uint CharacterId,
    ushort Page,
    int MinimumLevel,
    int MaximumLevel,
    string ExpeditionName,
    sbyte SortType,
    short InterestMask,
    bool MineOnly);

public sealed record ExpeditionRecruitmentPage(int Total, int PageSize, IReadOnlyList<ExpeditionRecruitment> Items);

public enum ExpeditionRecruitmentResult
{
    Success,
    InvalidRequest,
    NotAuthorized,
    NotFound,
    AlreadyExists,
    ApplicationLimit,
    AlreadyMember,
    MemberLimit,
    WrongFaction,
    NotEnoughMoney,
    PersistenceFailed
}
