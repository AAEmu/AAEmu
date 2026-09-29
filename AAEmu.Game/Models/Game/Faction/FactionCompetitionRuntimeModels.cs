namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// What a competition's <c>point_reset_id</c> means when the competition ends. The names come from
/// <c>enum_faction_competition_reset_state_kinds</c> and are matched exactly; an unrecognised
/// shipped name is refused rather than treated as one of these, so a new shipped kind cannot be
/// silently mapped onto the wrong reset behaviour.
/// </summary>
public enum FactionCompetitionResetStateKind
{
    /// <summary>Every faction's score is reset, and the winner still has to reach <c>req_point</c>.</summary>
    All,

    /// <summary>Only the winning faction's score is reset; the losers keep theirs.</summary>
    WinnerOnly,

    /// <summary>Every faction's score is reset, and a faction wins on score alone without <c>req_point</c>.</summary>
    AllIgnoreRequiredPoints
}

/// <summary>How one faction's score in one competition changed.</summary>
public readonly record struct FactionCompetitionScoreApplication(
    uint CompetitionId,
    uint FactionId,
    long PreviousScore,
    long Score,
    int RequestedDelta,
    int AppliedDelta);

/// <summary>
/// The outcome of resolving a competition: which faction won, if any, and which factions the
/// competition's reset state clears.
/// </summary>
/// <param name="WinnerFactionId">
/// The winning faction, or <c>null</c> when the competition has no winner: nobody reached
/// <c>req_point</c>, or the leading factions tied. A tie is not resolved by a secondary rule,
/// because no shipped column states one.
/// </param>
/// <param name="ResetFactionIds">The factions whose score the reset state clears, in a stable order.</param>
public readonly record struct FactionCompetitionResolution(
    uint CompetitionId,
    uint? WinnerFactionId,
    IReadOnlyList<uint> ResetFactionIds,
    FactionCompetitionResetStateKind ResetState);
