using System.Globalization;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.CrossServer;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The cross-server departure / transfer / rollback / re-entry state machine.
/// <para>
/// Sequence and its invariants:
/// <list type="number">
/// <item><b>Departure</b> freezes the character state and parks it with the journal row in one
/// atomic step (exactly once — a second departure is refused while a journal is live).</item>
/// <item><b>Completed transfer</b> settles the journal Parked → Transferred, once.</item>
/// <item><b>Failure</b> restores the snapshot and marks the journal RolledBack, atomically: the
/// character's money comes back and its items are proven untouched, or nothing is written.</item>
/// <item><b>Re-entry</b> is only legal from a settled transfer and consumes it exactly once;
/// re-entry with no departure is refused.</item>
/// <item><b>Restart</b> closes every journal still Parked without writing the snapshot back.
/// A departure only parks the character; they keep playing, so restoring departure-time money
/// would duplicate gold spent since then and erase gold earned since then.</item>
/// </list>
/// </para>
/// </summary>
public class CrossServerTransferManager(
    ICrossServerTransferStore store,
    byte sourceServerId,
    ICrossServerDirectory directory) : Singleton<CrossServerTransferManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ICrossServerTransferStore Store { get; } = store;

    /// <summary>This server's key as stored in the journal (game-server id as text, not a tuning value).</summary>
    public string SourceServerKey { get; } = sourceServerId.ToString(CultureInfo.InvariantCulture);

    private ICrossServerDirectory Directory { get; } = directory;

    /// <summary>
    /// Journals and parks the character for a move to another server.
    /// <paramref name="targetServerKey"/> may be empty, in which case the destination comes from
    /// content via <see cref="ICrossServerDirectory"/>; the pinned wire carries no target field.
    /// <paramref name="parkedAtUtc"/> is the park timestamp the caller must also mirror onto the
    /// live character so a later save cannot erase the marker the store just wrote.
    /// </summary>
    public CrossServerDepartureResult RequestDeparture(
        ulong characterId, uint accountId, string targetServerKey, DateTime parkedAtUtc,
        long? liveMoney = null, long? liveMoney2 = null, long? liveAaPoint = null)
    {
        var target = ResolveTarget(targetServerKey);
        if (string.IsNullOrWhiteSpace(target))
        {
            Logger.Error(
                "Cross-server departure refused for character {0}: no destination (requested '{1}').",
                characterId, targetServerKey ?? string.Empty);
            return new CrossServerDepartureResult(CrossServerTransferOutcome.RefusedMissingTarget, string.Empty, parkedAtUtc);
        }

        var snapshot = Store.CaptureSnapshot(characterId);
        if (snapshot != null && liveMoney != null && liveMoney2 != null && liveAaPoint != null)
        {
            snapshot = snapshot with
            {
                Money = liveMoney.Value,
                Money2 = liveMoney2.Value,
                AaPoint = liveAaPoint.Value,
            };
        }

        if (snapshot == null || snapshot.CharacterId != characterId)
        {
            Logger.Error("Cross-server departure refused for character {0}: no characters row to snapshot.", characterId);
            return new CrossServerDepartureResult(CrossServerTransferOutcome.RefusedCharacterMissing, target, parkedAtUtc);
        }

        var journal = new CrossServerTransferJournal(
            characterId,
            accountId,
            SourceServerKey,
            target,
            CrossServerTransferState.Parked,
            snapshot,
            parkedAtUtc,
            parkedAtUtc);

        if (!Store.TryParkAndJournal(journal))
        {
            var existing = Store.Get(characterId);
            var outcome = existing?.State switch
            {
                CrossServerTransferState.Parked => CrossServerTransferOutcome.RefusedDoubleDeparture,
                CrossServerTransferState.Transferred => CrossServerTransferOutcome.RefusedAwaitingReentry,
                null => CrossServerTransferOutcome.RefusedCharacterMissing,
                _ => CrossServerTransferOutcome.RefusedDoubleDeparture,
            };

            Logger.Error(
                "Cross-server departure refused for character {0}: {1} (journal state {2}).",
                characterId, outcome, existing?.State.ToString() ?? "absent");
            return new CrossServerDepartureResult(outcome, target, parkedAtUtc);
        }

        Logger.Info(
            "Character {0} departed to server {1}; journal parked at {2:u}.",
            characterId, target, parkedAtUtc);
        return new CrossServerDepartureResult(CrossServerTransferOutcome.Granted, target, parkedAtUtc);
    }

    /// <summary>Settles a completed outbound transfer: Parked → Transferred, exactly once.</summary>
    public CrossServerTransferOutcome CompleteTransfer(ulong characterId)
    {
        var journal = Store.Get(characterId);
        if (journal == null)
            return Refuse(CrossServerTransferOutcome.RefusedNoDeparture, characterId, "complete");

        if (journal.State != CrossServerTransferState.Parked)
            return Refuse(CrossServerTransferOutcome.RefusedNotParked, characterId, $"complete from {journal.State}");

        if (!Store.TrySetState(characterId, CrossServerTransferState.Parked, CrossServerTransferState.Transferred))
            return Refuse(CrossServerTransferOutcome.RefusedNotParked, characterId, "complete (journal moved concurrently)");

        Logger.Info("Character {0} transfer settled at server {1}.", characterId, journal.TargetServerKey);
        return CrossServerTransferOutcome.Granted;
    }

    /// <summary>
    /// Failed transfer: restores the parked snapshot and marks the journal RolledBack in one
    /// atomic step. Returns <see cref="CrossServerTransferOutcome.RefusedIntegrityViolation"/>
    /// when the live items no longer match the snapshot — nothing is written in that case.
    /// </summary>
    public CrossServerTransferOutcome FailTransfer(ulong characterId)
    {
        var journal = Store.Get(characterId);
        if (journal == null)
            return Refuse(CrossServerTransferOutcome.RefusedNoDeparture, characterId, "fail");

        if (journal.State != CrossServerTransferState.Parked)
            return Refuse(CrossServerTransferOutcome.RefusedNotParked, characterId, $"fail from {journal.State}");

        if (!Store.TryRestore(characterId, journal.Snapshot, CrossServerTransferState.Parked, CrossServerTransferState.RolledBack))
            return Refuse(CrossServerTransferOutcome.RefusedIntegrityViolation, characterId, "fail (snapshot no longer matches)");

        Logger.Info("Character {0} transfer failed and was rolled back; state restored from the departure snapshot.", characterId);
        return CrossServerTransferOutcome.Granted;
    }

    /// <summary>
    /// Accepts a character back from the peer server. Only a settled (Transferred) journal can be
    /// re-entered, and the journal is consumed exactly once: a second attempt has no departure to
    /// claim or finds it already consumed, and is refused.
    /// </summary>
    public CrossServerTransferOutcome Reenter(ulong characterId)
    {
        var journal = Store.Get(characterId);
        if (journal == null)
            return Refuse(CrossServerTransferOutcome.RefusedNoDeparture, characterId, "re-enter");

        switch (journal.State)
        {
            case CrossServerTransferState.Parked:
                return Refuse(CrossServerTransferOutcome.RefusedTransferIncomplete, characterId, "re-enter (departure never settled)");

            case CrossServerTransferState.Transferred:
                if (!Store.TryRestore(characterId, journal.Snapshot, CrossServerTransferState.Transferred, CrossServerTransferState.Reentered))
                    return Refuse(CrossServerTransferOutcome.RefusedIntegrityViolation, characterId, "re-enter (snapshot no longer matches)");

                Logger.Info("Character {0} re-entered from server {1}; journal consumed.", characterId, journal.SourceServerKey);
                return CrossServerTransferOutcome.Granted;

            case CrossServerTransferState.RolledBack:
                return Refuse(CrossServerTransferOutcome.RefusedNoDeparture, characterId, "re-enter (departure was rolled back)");

            default:
                return Refuse(CrossServerTransferOutcome.RefusedAlreadyReentered, characterId, "re-enter");
        }
    }

    /// <summary>
    /// Closes journals that are still Parked after a restart, without writing their snapshots
    /// back onto the character. The character never left this process, so the live wallet is
    /// the one to keep. Running it twice is a no-op.
    /// Returns how many journals were closed.
    /// </summary>
    public int RecoverInterruptedTransfers()
    {
        var interrupted = Store.LoadAll()
            .Where(journal => journal.State == CrossServerTransferState.Parked)
            .OrderBy(journal => journal.CharacterId)
            .ToList();

        var recovered = 0;
        foreach (var journal in interrupted)
        {
            if (Store.TryAbandonParked(journal.CharacterId))
            {
                recovered++;
                Logger.Warn(
                    "Closed parked cross-server departure for character {0} on restart without restoring the snapshot.",
                    journal.CharacterId);
            }
            else
            {
                Logger.Error(
                    "Cross-server recovery could not close the parked journal for character {0}.",
                    journal.CharacterId);
            }
        }

        return recovered;
    }

    private string ResolveTarget(string requestedTargetKey)
    {
        if (!string.IsNullOrWhiteSpace(requestedTargetKey))
            return requestedTargetKey;

        return Directory.ResolvePeerKey(SourceServerId);
    }

    private byte SourceServerId { get; } = sourceServerId;

    private static CrossServerTransferOutcome Refuse(CrossServerTransferOutcome outcome, ulong characterId, string operation)
    {
        Logger.Error("Cross-server transfer refused for character {0} during {1}: {2}.", characterId, operation, outcome);
        return outcome;
    }
}
