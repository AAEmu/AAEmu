namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// One hero agreement between two nations (the diplomacy overlay on a system_faction_relations
/// pair), and the shape of a history row. Faction ids are stored ascending, the way the client
/// keys them.
/// </summary>
public sealed class FactionDiplomacyAgreement
{
    public uint Faction1 { get; set; }
    public uint Faction2 { get; set; }
    public RelationState State { get; set; }
    public RelationState NextState { get; set; }
    public DateTime UpdateTime { get; set; }
    public DateTime ChangeTime { get; set; }
    public uint UpdaterId { get; set; }
    public string UpdaterName { get; set; } = string.Empty;
    public uint ConfirmerId { get; set; }
    public string ConfirmerName { get; set; } = string.Empty;

    public bool Involves(uint factionId) => Faction1 == factionId || Faction2 == factionId;

    public FactionDiplomacyAgreement Clone() => (FactionDiplomacyAgreement)MemberwiseClone();
}

/// <summary>
/// A diplomacy counter. <c>OtherId</c> 0 is the character's own daily agreement count; a non-zero
/// <c>OtherId</c> is a requester the character (a hero) has denied, with the denial count. This is
/// the client's count map key order (x2game-dev.dll 0x391c61b0: own = (me, 0), deny = (hero, me)).
/// </summary>
public readonly record struct FactionDiplomacyCount(uint CharacterId, uint OtherId, uint Count, DateTime UpdatedAt);

/// <summary>A request one hero sent another that has not been answered yet. Memory only.</summary>
public sealed class FactionDiplomacyProposal
{
    public uint RequesterId { get; init; }
    public string RequesterName { get; init; } = string.Empty;
    public uint RequesterNation { get; init; }
    public uint TargetId { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public uint TargetNation { get; init; }
    public DateTime CreatedAt { get; init; }
}
