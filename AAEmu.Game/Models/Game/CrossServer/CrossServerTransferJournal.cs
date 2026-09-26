namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// One row of <c>character_transfer_journals</c>: the single source of truth for whether a
/// character is mid cross-server transfer. The primary key on <c>character_id</c> is what makes
/// "exactly once" a database guarantee rather than a code convention.
/// </summary>
/// <param name="CharacterId">The parked character (journal primary key).</param>
/// <param name="AccountId">Owning account, for audit and reconnect checks.</param>
/// <param name="SourceServerKey">This server's key (game-server id as text).</param>
/// <param name="TargetServerKey">Peer server key resolved from content (<c>server_configs</c>).</param>
/// <param name="State">See <see cref="CrossServerTransferState"/>.</param>
/// <param name="Snapshot">Character state captured at departure, restored on rollback / re-entry.</param>
/// <param name="CreatedUtc">Departure park timestamp (park time, not wall clock).</param>
/// <param name="UpdatedUtc">Timestamp of the last accepted state transition.</param>
public sealed record CrossServerTransferJournal(
    ulong CharacterId,
    uint AccountId,
    string SourceServerKey,
    string TargetServerKey,
    CrossServerTransferState State,
    CrossServerCharacterSnapshot Snapshot,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
