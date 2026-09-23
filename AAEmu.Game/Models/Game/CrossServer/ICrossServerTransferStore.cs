namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// Durable side of the cross-server transfer state machine. Every mutator is atomic: either the
/// journal row and the character row move together, or neither does. Nothing here retries or
/// falls back — a <see langword="false"/> means the precondition in the caller's journal did not
/// hold, and the caller reports that refusal instead of guessing.
/// </summary>
public interface ICrossServerTransferStore
{
    /// <summary>Freezes the character's transfer-relevant state. <see langword="null"/> when there is no such character.</summary>
    CrossServerCharacterSnapshot CaptureSnapshot(ulong characterId);

    /// <summary>
    /// Parks the character and writes the journal row in one atomic step.
    /// Returns <see langword="false"/> when a live (Parked / Transferred) journal already exists
    /// for the character — the double-departure refusal — or when the character row is gone.
    /// Terminal rows (RolledBack / Reentered) are replaced, so a character may depart again after
    /// a rollback or a completed round trip.
    /// </summary>
    bool TryParkAndJournal(CrossServerTransferJournal journal);

    /// <summary>The journal row for a character, or <see langword="null"/> when it has never departed (or was consumed).</summary>
    CrossServerTransferJournal Get(ulong characterId);

    /// <summary>Compare-and-set a journal state. <see langword="false"/> when the row is not in <paramref name="expected"/>.</summary>
    bool TrySetState(ulong characterId, CrossServerTransferState expected, CrossServerTransferState next);

    /// <summary>
    /// Restores the snapshot onto the character and moves the journal from
    /// <paramref name="expected"/> to <paramref name="next"/>, atomically.
    /// Returns <see langword="false"/> (writing nothing at all) when the row is not in
    /// <paramref name="expected"/> or the live character no longer matches the snapshot's item
    /// witness — a mismatch is reported, never papered over with a partial restore.
    /// </summary>
    bool TryRestore(ulong characterId, CrossServerCharacterSnapshot snapshot, CrossServerTransferState expected, CrossServerTransferState next);

    /// <summary>
    /// Closes a Parked journal and clears the character's park marker without writing the
    /// snapshot's money back. <see langword="false"/> when the journal is not Parked.
    /// </summary>
    bool TryAbandonParked(ulong characterId);

    /// <summary>Every journal row, ordered by character id so recovery is deterministic.</summary>
    IReadOnlyList<CrossServerTransferJournal> LoadAll();
}
