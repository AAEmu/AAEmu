using AAEmu.Game.Models.Game.Milestones;
using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Milestones;

/// <summary>
/// Save/reload: the rows the MySQL save writes and the grant rows the saga ledger carries must
/// reproduce the exact same state after a round trip, importing must replace rather than merge,
/// and a reloaded state must still refuse the second grant.
/// </summary>
public class MilestonePersistenceRoundTripTests
{
    private const uint MilestoneId = 5001;
    private static readonly DateTime Now = MilestoneTestCatalog.Now;

    /// <summary>The ground truth the populated fixture was built from: 5001 half-way,
    /// 5002 finished (grant taken).</summary>
    private static readonly Func<uint, bool> FixtureTruth =
        id => id is 9001 or 9010 or 9011;

    private static (MilestoneCatalog Catalog, SagaProgressState Ledger, MilestoneProgressState State)
        PopulatedState()
    {
        var catalog = MilestoneTestCatalog.Build(
            rows: [(MilestoneId, true), (5002, true)],
            triggers:
            [
                (9001, MilestoneId), (9002, MilestoneId), (9003, MilestoneId),
                (9010, 5002), (9011, 5002),
            ]);
        var ledger = new SagaProgressState();
        var state = new MilestoneProgressState(ledger);

        state.OnQuestCompleted(catalog, 9001, FixtureTruth, Now);
        state.Reconcile(catalog, FixtureTruth, Now);
        return (catalog, ledger, state);
    }

    [Test]
    public async Task SaveReload_ProducesTheSameState()
    {
        var (catalog, ledger, original) = PopulatedState();
        var rows = original.ExportRows();
        var grantRows = ledger.ExportGrants();

        var reloadedLedger = new SagaProgressState();
        reloadedLedger.Import([], grantRows);
        var reloaded = new MilestoneProgressState(reloadedLedger);
        reloaded.Import(rows);

        await Assert.That(reloaded.ExportRows()).IsEquivalentTo(original.ExportRows());
        await Assert.That(reloadedLedger.ExportGrants()).IsEquivalentTo(ledger.ExportGrants());

        // Behavioural equality, not just row equality: same records, same grants, same verdicts.
        await Assert.That(reloaded.TryGetRecord(MilestoneId, out var partial)).IsTrue();
        await Assert.That((int)partial.CompletedCount).IsEqualTo(1);
        await Assert.That(partial.Status).IsEqualTo(MilestoneStatus.Active);
        await Assert.That(reloaded.TryGetRecord(5002, out var done)).IsTrue();
        await Assert.That(done.Status).IsEqualTo(MilestoneStatus.Complete);
        await Assert.That(reloaded.HasGrant(5002)).IsTrue();
        await Assert.That(reloaded.HasGrant(MilestoneId)).IsFalse();

        // After the round trip the state has converged against the same ground truth:
        // reconcile finds nothing to change, which is exactly "sync once" surviving a restart.
        await Assert.That(reloaded.Reconcile(catalog, FixtureTruth, Now)).IsEmpty();
    }

    [Test]
    public async Task ReloadedState_StillRefusesTheSecondGrant()
    {
        var (_, originalLedger, original) = PopulatedState();

        var reloadedLedger = new SagaProgressState();
        reloadedLedger.Import([], originalLedger.ExportGrants());
        var reloaded = new MilestoneProgressState(reloadedLedger);
        reloaded.Import(original.ExportRows());

        await Assert.That(reloadedLedger.TryBeginGrant(
            MilestoneProgressState.GrantScope, 5002)).IsFalse();
        await Assert.That(reloadedLedger.ExportGrants().Count)
            .IsEqualTo(originalLedger.ExportGrants().Count);
    }

    [Test]
    public async Task Export_IsOrderedAndStable_AcrossRepeatedSaves()
    {
        var (_, _ledger, state) = PopulatedState();

        await Assert.That(state.ExportRows().Select(row => row.MilestoneId))
            .IsEquivalentTo([MilestoneId, 5002u]);
        await Assert.That(state.ExportRows()).IsEquivalentTo(state.ExportRows());
    }

    [Test]
    public async Task Import_ReplacesState_InsteadOfMerging()
    {
        var (_, _ledger, stale) = PopulatedState();
        stale.Import([new MilestoneProgressRow(4321, MilestoneStatus.Active, 7)]);

        var freshLedger = new SagaProgressState();
        var fresh = new MilestoneProgressState(freshLedger);
        fresh.Import([new MilestoneProgressRow(MilestoneId, MilestoneStatus.Active, 1)]);

        stale.Import(fresh.ExportRows());

        await Assert.That(stale.ExportRows()).IsEquivalentTo(fresh.ExportRows());
        await Assert.That(stale.TryGetRecord(4321, out _)).IsFalse();
    }

    [Test]
    public async Task EmptyState_RoundTripsToEmptyState()
    {
        var state = new MilestoneProgressState(new SagaProgressState());
        var rows = state.ExportRows();
        var reloaded = new MilestoneProgressState(new SagaProgressState());
        reloaded.Import(rows);

        await Assert.That(reloaded.ExportRows()).IsEmpty();
        await Assert.That(reloaded.HasGrant(MilestoneId)).IsFalse();
    }
}
