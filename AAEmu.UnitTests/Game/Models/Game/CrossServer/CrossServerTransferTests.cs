using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.CrossServer;

namespace AAEmu.UnitTests.Game.Models.Game.CrossServer;

/// <summary>
/// Acceptance for GF-S15 (cross-server departure and re-entry): a departure journals and parks
/// the character exactly once, a completed transfer settles, a failure restores the character
/// fully, double departures and re-entry-without-departure are refused definitively, a restart
/// mid-transfer recovers deterministically, and the journal survives a persistence round trip.
/// Evidence for the wire half lives in <c>CrossServerDepartureWireTests</c>.
/// </summary>
public class CrossServerTransferTests
{
    private const ulong CharacterId = 7;
    private const uint AccountId = 4242;
    private static readonly DateTime ParkedAt = new(2026, 9, 23, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>Content-backed peer lookup, stood in for by a fixed peer key (tests own their rows).</summary>
    private sealed class StubDirectory(string peerKey) : ICrossServerDirectory
    {
        public string ResolvePeerKey(byte ownServerId) => peerKey;
    }

    private static InMemoryCrossServerTransferStore StoreWithCharacter(long money = 500, long money2 = 25, long aaPoint = 9, long[] items = null)
    {
        var store = new InMemoryCrossServerTransferStore();
        store.AddCharacter(CharacterId, AccountId, money, money2, aaPoint, items ?? [10, 20, 30]);
        return store;
    }

    private static CrossServerTransferManager Manager(InMemoryCrossServerTransferStore store, ICrossServerDirectory directory = null) =>
        new(store, sourceServerId: 1, directory ?? new StubDirectory("peer-2"));

    [Test]
    public async Task Departure_JournalsAndParksTheCharacterExactlyOnce()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);

        // targetServerKey null = what the pinned wire sends: 0x1C7 has no body, so the
        // destination comes from content through the directory.
        var first = manager.RequestDeparture(CharacterId, AccountId, targetServerKey: null, ParkedAt);

        await Assert.That(first.Outcome).IsEqualTo(CrossServerTransferOutcome.Granted);
        await Assert.That(first.TargetServerKey).IsEqualTo("peer-2");
        await Assert.That(store.ParkMarkerWrites).IsEqualTo(1);

        var journal = store.Get(CharacterId);
        await Assert.That(journal).IsNotNull();
        await Assert.That(journal.State).IsEqualTo(CrossServerTransferState.Parked);
        await Assert.That(journal.AccountId).IsEqualTo(AccountId);
        await Assert.That(journal.SourceServerKey).IsEqualTo("1");
        await Assert.That(journal.TargetServerKey).IsEqualTo("peer-2");
        await Assert.That(journal.CreatedUtc).IsEqualTo(ParkedAt);
        await Assert.That(journal.UpdatedUtc).IsEqualTo(ParkedAt);
        await Assert.That(journal.Snapshot.Money).IsEqualTo(500);
        await Assert.That(journal.Snapshot.ItemCount).IsEqualTo(3);
        await Assert.That(store.Characters[CharacterId].TransferRequestTime).IsEqualTo(ParkedAt);

        // A second departure must not touch anything.
        var second = manager.RequestDeparture(CharacterId, AccountId, targetServerKey: null, ParkedAt.AddMinutes(1));

        await Assert.That(second.Outcome).IsEqualTo(CrossServerTransferOutcome.RefusedDoubleDeparture);
        await Assert.That(store.ParkMarkerWrites).IsEqualTo(1);
        await Assert.That(store.Characters[CharacterId].TransferRequestTime).IsEqualTo(ParkedAt);
        await Assert.That(store.Get(CharacterId).UpdatedUtc).IsEqualTo(ParkedAt);
    }

    [Test]
    public async Task CompletedTransfer_SettlesExactlyOnce()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);
        manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);

        await Assert.That(manager.CompleteTransfer(CharacterId)).IsEqualTo(CrossServerTransferOutcome.Granted);
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Transferred);

        await Assert.That(manager.CompleteTransfer(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedNotParked);
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Transferred);

        // Settled means settled: the park marker stays, nothing is written twice.
        await Assert.That(store.ParkMarkerWrites).IsEqualTo(1);
        await Assert.That(store.RestoreWrites).IsEqualTo(0);
    }

    [Test]
    public async Task FailureRollback_RestoresMoneyAndLeavesItemsIntact()
    {
        var store = StoreWithCharacter(money: 500, money2: 25, aaPoint: 9, items: [10, 20, 30]);
        var manager = Manager(store);
        manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);

        // A money operation that raced the parked character drifts the wallet after the snapshot.
        var row = store.Characters[CharacterId];
        row.Money = 250;
        var itemsBefore = row.ItemIds.ToList();

        await Assert.That(manager.FailTransfer(CharacterId)).IsEqualTo(CrossServerTransferOutcome.Granted);

        await Assert.That(row.Money).IsEqualTo(500);
        await Assert.That(row.Money2).IsEqualTo(25);
        await Assert.That(row.AaPoint).IsEqualTo(9);
        await Assert.That(row.ItemIds).IsEquivalentTo(itemsBefore);
        await Assert.That(row.TransferRequestTime).IsEqualTo(default(DateTime)); // park marker cleared
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.RolledBack);
        await Assert.That(store.RestoreWrites).IsEqualTo(1);

        // The rollback itself is exactly once: a second failure finds nothing parked.
        await Assert.That(manager.FailTransfer(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedNotParked);
        await Assert.That(row.Money).IsEqualTo(500);
        await Assert.That(store.RestoreWrites).IsEqualTo(1);
    }

    [Test]
    public async Task FailureRollback_ItemDriftIsRefusedInsteadOfPartiallyRestored()
    {
        var store = StoreWithCharacter(money: 500, items: [10, 20, 30]);
        var manager = Manager(store);
        manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);

        var row = store.Characters[CharacterId];
        row.ItemIds.Remove(10); // the inventory the snapshot witnessed is gone
        row.Money = 250;

        await Assert.That(manager.FailTransfer(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedIntegrityViolation);

        // Nothing was written: no wallet restore over an inventory that no longer matches.
        await Assert.That(row.Money).IsEqualTo(250);
        await Assert.That(store.RestoreWrites).IsEqualTo(0);
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Parked);
    }

    [Test]
    public async Task DoubleDeparture_AndDepartureIntoSettledTransfer_AreRefusedDefinitively()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);

        await Assert.That(manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt).Outcome)
            .IsEqualTo(CrossServerTransferOutcome.Granted);
        await Assert.That(manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt.AddMinutes(1)).Outcome)
            .IsEqualTo(CrossServerTransferOutcome.RefusedDoubleDeparture);

        manager.CompleteTransfer(CharacterId);
        await Assert.That(manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt.AddMinutes(2)).Outcome)
            .IsEqualTo(CrossServerTransferOutcome.RefusedAwaitingReentry);
        await Assert.That(store.ParkMarkerWrites).IsEqualTo(1);

        // After a rollback the terminal journal is history and a fresh departure is legal again.
        var other = StoreWithCharacter();
        var otherManager = Manager(other);
        otherManager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);
        otherManager.FailTransfer(CharacterId);
        await Assert.That(otherManager.RequestDeparture(CharacterId, AccountId, null, ParkedAt.AddMinutes(5)).Outcome)
            .IsEqualTo(CrossServerTransferOutcome.Granted);
        await Assert.That(other.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Parked);
    }

    [Test]
    public async Task ReentryWithoutDeparture_IsRefusedDefinitively()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);

        // Never departed: there is no journal to claim.
        await Assert.That(manager.Reenter(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedNoDeparture);
        await Assert.That(store.RestoreWrites).IsEqualTo(0);

        // Departed but never settled: the transfer is still in flight here.
        manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);
        await Assert.That(manager.Reenter(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedTransferIncomplete);
        await Assert.That(store.RestoreWrites).IsEqualTo(0);

        // Rolled back means the departure no longer exists to re-enter.
        manager.FailTransfer(CharacterId);
        await Assert.That(manager.Reenter(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedNoDeparture);
    }

    [Test]
    public async Task Reentry_AfterCompletedTransfer_ConsumesExactlyOnce()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);
        manager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);
        manager.CompleteTransfer(CharacterId);

        await Assert.That(manager.Reenter(CharacterId)).IsEqualTo(CrossServerTransferOutcome.Granted);
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Reentered);
        await Assert.That(store.Characters[CharacterId].TransferRequestTime).IsEqualTo(default(DateTime));
        await Assert.That(store.RestoreWrites).IsEqualTo(1);

        await Assert.That(manager.Reenter(CharacterId)).IsEqualTo(CrossServerTransferOutcome.RefusedAlreadyReentered);
        await Assert.That(store.RestoreWrites).IsEqualTo(1);
    }

    [Test]
    public async Task RestartMidTransfer_RecoversDeterministically()
    {
        var store = StoreWithCharacter(money: 500, items: [10, 20, 30]);
        Manager(store).RequestDeparture(CharacterId, AccountId, null, ParkedAt);
        var row = store.Characters[CharacterId];
        row.Money = 123; // whatever was in flight when the process died

        // A new manager over the same persisted store = a restart.
        var restarted = Manager(store);
        await Assert.That(restarted.RecoverInterruptedTransfers()).IsEqualTo(1);
        await Assert.That(store.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.RolledBack);
        await Assert.That(row.Money).IsEqualTo(500);
        await Assert.That(row.TransferRequestTime).IsEqualTo(default(DateTime));

        // Recovery never runs twice: the second pass finds nothing parked.
        await Assert.That(restarted.RecoverInterruptedTransfers()).IsEqualTo(0);
        await Assert.That(store.RestoreWrites).IsEqualTo(1);

        // A settled transfer is not "interrupted": recovery must leave it alone.
        var settledStore = StoreWithCharacter();
        var settledManager = Manager(settledStore);
        settledManager.RequestDeparture(CharacterId, AccountId, null, ParkedAt);
        settledManager.CompleteTransfer(CharacterId);
        await Assert.That(settledManager.RecoverInterruptedTransfers()).IsEqualTo(0);
        await Assert.That(settledStore.Get(CharacterId).State).IsEqualTo(CrossServerTransferState.Transferred);
    }

    [Test]
    public async Task JournalAndSnapshot_RoundTripThroughPersistence()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store);
        manager.RequestDeparture(CharacterId, AccountId, "peer-9", ParkedAt);

        var persisted = store.Get(CharacterId);
        await Assert.That(persisted).IsEqualTo(new CrossServerTransferJournal(
            CharacterId,
            AccountId,
            "1",
            "peer-9",
            CrossServerTransferState.Parked,
            persisted.Snapshot,
            ParkedAt,
            ParkedAt));

        // The snapshot column is JSON: it must survive serialize -> deserialize byte-for-byte.
        var restored = CrossServerCharacterSnapshot.FromJson(persisted.Snapshot.ToJson());
        await Assert.That(restored).IsEqualTo(persisted.Snapshot);
        await Assert.That(restored.TransferRequestUtc).IsEqualTo(default(DateTime));
    }

    [Test]
    public async Task MalformedSnapshot_FailsLoudInsteadOfRestoringDefaults()
    {
        await Assert.That(() => CrossServerCharacterSnapshot.FromJson("")).Throws<InvalidDataException>();
        await Assert.That(() => CrossServerCharacterSnapshot.FromJson("   ")).Throws<InvalidDataException>();
        await Assert.That(() => CrossServerCharacterSnapshot.FromJson("{ not json")).Throws<InvalidDataException>();
    }

    [Test]
    public async Task DepartureWithoutContentPeer_IsRefusedAndChangesNothing()
    {
        var store = StoreWithCharacter();
        var manager = Manager(store, new StubDirectory(null)); // content names no single peer

        var result = manager.RequestDeparture(CharacterId, AccountId, targetServerKey: null, ParkedAt);

        await Assert.That(result.Outcome).IsEqualTo(CrossServerTransferOutcome.RefusedMissingTarget);
        await Assert.That(store.ParkMarkerWrites).IsEqualTo(0);
        await Assert.That(store.Get(CharacterId)).IsNull();
        await Assert.That(store.Characters[CharacterId].TransferRequestTime).IsEqualTo(default(DateTime));
    }
}
