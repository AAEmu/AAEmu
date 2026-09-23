namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// One <c>character_resident_state</c> row: what one character has contributed to (and paid into)
/// one zone group's resident balance. Written at every settlement, read by the townhall packets.
/// </summary>
public sealed record CharacterResidentState(
    uint OwnerId,
    ushort ZoneGroupId,
    uint ServicePoint,
    ulong Charge,
    DateTime UpdatedAt);

/// <summary>
/// One <c>local_development_state</c> row: the last development level and the last doodad/board
/// func-group phases applied for a zone group. Phase 0 of either column means "nothing applied
/// yet"; the content phases themselves are always &gt; 0 (func-group ids).
/// </summary>
public sealed record LocalDevelopmentState(
    ushort ZoneGroupId,
    uint DevelopmentLevel,
    uint DoodadPhase,
    uint BoardPhase,
    DateTime UpdatedAt);

/// <summary>Outcome of one resident settlement attempt.</summary>
public enum ResidentSettleStatus
{
    /// <summary>Nothing was written: the request itself was invalid or carried unresolved fields.</summary>
    Refused,

    /// <summary>The row was written and the development state machine ran.</summary>
    Settled,

    /// <summary>
    /// The row was written, but the zone group has no <c>local_developments</c> row, so no phase
    /// was evaluated. Loud skip — never a fallback.
    /// </summary>
    SettledDevelopmentSkipped,
}
