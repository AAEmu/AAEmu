using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Sagas;

/// <summary>
/// Save/reload: the row pairs the MySQL save writes and the load imports must reproduce the exact
/// same state, and importing must replace rather than merge.
/// </summary>
public class SagaPersistenceRoundTripTests
{
    private static SagaQuestCatalog Catalog() => SagaTestCatalog.Build(
        groups: [(8u, 0u, 130u), (9u, 0u, 130u)],
        members: [(8u, 9001u), (8u, 9002u), (9u, 9009u)]);

    private static SagaProgressState PopulatedState()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();
        state.Unlock(catalog, 8);
        state.Unlock(catalog, 9);

        // Group 8 half-way, group 9 finished (grant taken).
        state.OnQuestCompleted(catalog, 9001u, questId => questId == 9001u);
        state.OnQuestCompleted(catalog, 9009u, _ => true);
        return state;
    }

    [Test]
    public async Task SaveReload_ProducesTheSameState()
    {
        var original = PopulatedState();
        var groups = original.ExportGroups();
        var grants = original.ExportGrants();

        var reloaded = new SagaProgressState();
        reloaded.Import(groups, grants);

        await Assert.That(reloaded.ExportGroups()).IsEquivalentTo(original.ExportGroups());
        await Assert.That(reloaded.ExportGrants()).IsEquivalentTo(original.ExportGrants());

        // Behavioural equality, not just row equality: same gates, same records, same grants.
        var catalog = Catalog();
        await Assert.That(reloaded.EvaluateStartGate(catalog, 9001u))
            .IsEqualTo(original.EvaluateStartGate(catalog, 9001u));
        await Assert.That(reloaded.EvaluateStartGate(catalog, 9009u))
            .IsEqualTo(SagaStartGate.Allowed);
        await Assert.That(reloaded.TryGetRecord(8, out var eight)).IsTrue();
        await Assert.That(eight.Status).IsEqualTo(SagaGroupStatus.Active);
        await Assert.That((int)eight.CompletedCount).IsEqualTo(1);
        await Assert.That(reloaded.TryGetRecord(9, out var nine)).IsTrue();
        await Assert.That(nine.Status).IsEqualTo(SagaGroupStatus.Complete);
        await Assert.That(reloaded.HasGrant(9, 130u)).IsTrue();
        await Assert.That(reloaded.HasGrant(8, 130u)).IsFalse();
    }

    [Test]
    public async Task ReloadedState_StillRefusesTheSecondGrant()
    {
        var reloaded = new SagaProgressState();
        reloaded.Import(PopulatedState().ExportGroups(), PopulatedState().ExportGrants());

        await Assert.That(reloaded.TryBeginGrant(9, 130u)).IsFalse();
        await Assert.That(reloaded.ExportGrants().Count).IsEqualTo(1);
    }

    [Test]
    public async Task Export_IsOrderedAndStable_AcrossRepeatedSaves()
    {
        var state = PopulatedState();

        await Assert.That(state.ExportGroups().Select(row => row.GroupId)).IsEquivalentTo([8u, 9u]);
        await Assert.That(state.ExportGroups()).IsEquivalentTo(state.ExportGroups());
        await Assert.That(state.ExportGrants()).IsEquivalentTo(state.ExportGrants());
    }

    [Test]
    public async Task Import_ReplacesState_InsteadOfMerging()
    {
        var stale = PopulatedState();
        stale.Unlock(Catalog(), 7); // a group the reloaded rows no longer carry

        var fresh = PopulatedState();
        stale.Import(fresh.ExportGroups(), fresh.ExportGrants());

        await Assert.That(stale.IsUnlocked(7)).IsFalse();
        await Assert.That(stale.ExportGroups()).IsEquivalentTo(fresh.ExportGroups());
    }
}
