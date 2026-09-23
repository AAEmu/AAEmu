namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// Outcome of a cross-server transfer operation. Refusals are terminal answers, not errors to
/// retry: every non-<see cref="Granted"/> value means the state machine did not move.
/// </summary>
public enum CrossServerTransferOutcome : byte
{
    /// <summary>The requested transition happened exactly once.</summary>
    Granted,

    /// <summary>No destination: the pinned wire carries none and content has no unambiguous peer.</summary>
    RefusedMissingTarget,

    /// <summary>No characters row for the id, so nothing could be snapshotted or parked.</summary>
    RefusedCharacterMissing,

    /// <summary>A departure is already parked for this character (double departure).</summary>
    RefusedDoubleDeparture,

    /// <summary>A settled transfer is waiting to be re-entered before another departure may start.</summary>
    RefusedAwaitingReentry,

    /// <summary>Complete/fail targeted a journal that is not Parked (already settled or rolled back).</summary>
    RefusedNotParked,

    /// <summary>Re-entry without a departure journal for this character.</summary>
    RefusedNoDeparture,

    /// <summary>Re-entry attempted while the departure is still parked (transfer never completed).</summary>
    RefusedTransferIncomplete,

    /// <summary>The journal was already consumed by an earlier re-entry.</summary>
    RefusedAlreadyReentered,

    /// <summary>The stored snapshot no longer matches the character; nothing was written (no partial state).</summary>
    RefusedIntegrityViolation,
}
