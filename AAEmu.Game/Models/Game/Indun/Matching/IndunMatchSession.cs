namespace AAEmu.Game.Models.Game.Indun.Matching;

public sealed class IndunMatchSession
{
    public required ulong MatchingKey { get; init; }
    public required uint CatalogId { get; init; }
    public required uint ZoneGroupId { get; init; }
    public required uint ZoneKey { get; init; }
    public required uint MaxPlayers { get; init; }
    public required MatchingInvitationType InvitationType { get; init; }
    public required uint CleanupTermMs { get; init; }
    public required List<IndunMatchApplicant> Members { get; init; }
    public IndunMatchPhase Phase { get; set; } = IndunMatchPhase.Inviting;
    public DateTime InviteOpenedAt { get; set; }

    /// <summary>The copy being built for this match, offered to its members once it is ready.</summary>
    public IPreparedIndunInstance Prepared { get; set; }
    public DateTime PreparingSince { get; set; }
}
