namespace AAEmu.Game.Models.Game.Items.Loots;

/// <summary>
/// A typed NPC description used by the content-backed drop-rule matcher groundwork.
/// Values are loaded from the NPC row; no rule semantics are inferred from names.
/// </summary>
public sealed record DropRuleSubject(
    uint NpcId,
    int? Level,
    int? NpcTendencyId,
    int? NpcGradeId,
    int? NpcKindId,
    int? NpcNicknameId,
    int? HeirLevel,
    string? Name,
    string? Comment1,
    string? Comment2,
    string? Comment3,
    bool? Aggression);

public sealed record DropRuleMembership(uint Id, uint LootPackId);

/// <summary>
/// Content metadata for one drop rule. This type is deliberately not a runtime loot selector:
/// the shipped table has no pack-level weight and the ownership/semantics still need review.
/// </summary>
public sealed class DropRuleDefinition
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public uint MatcherId { get; init; }
    /// <summary>Loaded for diagnostics only; the runtime meaning of for_batch is not inferred.</summary>
    public bool ForBatch { get; init; }
    public DropRuleMatcher Matcher { get; init; } = DropRuleMatcher.Invalid;
    public IReadOnlyList<DropRuleMembership> Memberships { get; init; } = [];
    public string? InvalidReason { get; init; }

    public bool IsMetadataValid => InvalidReason is null && Matcher.IsValid && Memberships.Count > 0;
    public bool Matches(DropRuleSubject subject) => Matcher.IsValid && Matcher.Matches(subject);
}

public sealed record DropRuleDiagnostics(
    int RuleCount,
    int MembershipCount,
    int OrphanMembershipCount,
    int MissingLootPackMembershipCount,
    IReadOnlyList<uint> MissingLootPackIds,
    int InvalidRuleCount,
    int ForBatchRuleCount,
    int NpcSubjectCount);
