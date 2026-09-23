using AAEmu.Game.Models.Game.Milestones;
using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Milestones;

/// <summary>
/// The progression state machine: reversed-trigger advancement, eligibility straight from the
/// content row, the completion chain, the exactly-once grant on GF-W13's shared ledger, and
/// refusal of every replay.
/// </summary>
public class MilestoneProgressStateTests
{
    private const uint MilestoneId = 5001;
    private static readonly DateTime Now = MilestoneTestCatalog.Now;

    private static (MilestoneCatalog Catalog, SagaProgressState Ledger, MilestoneProgressState State)
        Fixture(
            (uint Id, bool Release)[] rows = null,
            (uint QuestId, uint MilestoneId)[] triggers = null)
    {
        var catalog = MilestoneTestCatalog.Build(
            rows ?? [(MilestoneId, true)],
            triggers ?? [(9001, MilestoneId), (9002, MilestoneId), (9003, MilestoneId)]);
        var ledger = new SagaProgressState();
        return (catalog, ledger, new MilestoneProgressState(ledger));
    }

    [Test]
    public async Task SameTriggerTwice_TakesExactlyOneGrantAndOneStatusEdge()
    {
        var (catalog, ledger, state) = Fixture();
        HashSet<uint> completed = [];

        // The event can arrive before the completed bit exists — counts follow ground truth.
        await Assert.That(state.OnQuestCompleted(catalog, 9001, completed.Contains, Now)).IsNull();

        completed.Add(9001);
        var partial = state.OnQuestCompleted(catalog, 9001, completed.Contains, Now);
        await Assert.That(partial).IsNotNull();
        await Assert.That(partial.StatusChanged).IsFalse();
        await Assert.That(partial.Granted).IsFalse();
        await Assert.That((int)partial.CompletedCount).IsEqualTo(1);

        // The same trigger again: nothing moved, no second change, no packet-worthy edge.
        await Assert.That(state.OnQuestCompleted(catalog, 9001, completed.Contains, Now)).IsNull();

        completed.Add(9002);
        var mid = state.OnQuestCompleted(catalog, 9002, completed.Contains, Now);
        await Assert.That((int)mid.CompletedCount).IsEqualTo(2);
        await Assert.That(mid.StatusChanged).IsFalse();
        await Assert.That(mid.Granted).IsFalse();

        // The last member of the chain finishes the milestone: grant + status edge, once.
        completed.Add(9003);
        var finish = state.OnQuestCompleted(catalog, 9003, completed.Contains, Now);
        await Assert.That(finish.StatusChanged).IsTrue();
        await Assert.That(finish.PreviousStatus).IsEqualTo(MilestoneStatus.Active);
        await Assert.That(finish.CurrentStatus).IsEqualTo(MilestoneStatus.Complete);
        await Assert.That(finish.Granted).IsTrue();
        await Assert.That((int)finish.CompletedCount).IsEqualTo(3);

        // Replaying the finishing edge changes nothing and grants nothing more.
        await Assert.That(state.OnQuestCompleted(catalog, 9003, completed.Contains, Now)).IsNull();
        await Assert.That(state.HasGrant(MilestoneId)).IsTrue();
        await Assert.That(ledger.HasGrant(MilestoneProgressState.GrantScope, MilestoneId)).IsTrue();
        await Assert.That(
            ledger.TryBeginGrant(MilestoneProgressState.GrantScope, MilestoneId)).IsFalse();
        await Assert.That(ledger.ExportGrants().Count).IsEqualTo(1);
    }

    [Test]
    public async Task UntaggedQuest_IsNotATrigger_AtAll()
    {
        var (catalog, _ledger, state) = Fixture();

        await Assert.That(state.OnQuestCompleted(catalog, 9999, _ => true, Now)).IsNull();
        await Assert.That(state.ExportRows()).IsEmpty();
    }

    [Test]
    public async Task ChainProgress_AdvancesFromEveryMember_InAnyOrder()
    {
        var (catalog, _ledger, state) = Fixture();

        // Out-of-order completion: the chain is a set of content rows, not a script.
        state.OnQuestCompleted(catalog, 9003, id => id is 9003 or 9001, Now);

        await Assert.That(state.TryGetRecord(MilestoneId, out var record)).IsTrue();
        await Assert.That((int)record.CompletedCount).IsEqualTo(2);
        await Assert.That(record.Status).IsEqualTo(MilestoneStatus.Active);
        await Assert.That(state.HasGrant(MilestoneId)).IsFalse();

        var finish = state.OnQuestCompleted(catalog, 9002, _ => true, Now);
        await Assert.That(finish.CurrentStatus).IsEqualTo(MilestoneStatus.Complete);
        await Assert.That(finish.Granted).IsTrue();
    }

    [Test]
    public async Task UnreleasedMilestone_NeverAdvances_EvenWithAFinishedChain()
    {
        // release = 'f' straight from the content row: test/unshipped batches stay inert.
        var (catalog, ledger, state) = Fixture(rows: [(MilestoneId, false)]);

        var change = state.OnQuestCompleted(catalog, 9003, _ => true, Now);
        await Assert.That(change).IsNull();
        await Assert.That(state.ExportRows()).IsEmpty();
        await Assert.That(ledger.ExportGrants()).IsEmpty();
    }

    [Test]
    public async Task OutOfWindowMilestone_NeverAdvances_BeforeStartOrAfterEnd()
    {
        var notOpened = MilestoneCatalog.Build(
            [new MilestoneRow(MilestoneId, Now.AddDays(1), Now.AddDays(2), true)],
            [new MilestoneTriggerRow(9001, MilestoneId)]);
        var closed = MilestoneCatalog.Build(
            [new MilestoneRow(MilestoneId, Now.AddDays(-2), Now.AddDays(-1), true)],
            [new MilestoneTriggerRow(9001, MilestoneId)]);
        var ledger = new SagaProgressState();

        await Assert.That(new MilestoneProgressState(ledger)
            .OnQuestCompleted(notOpened, 9001, _ => true, Now)).IsNull();
        await Assert.That(new MilestoneProgressState(ledger)
            .OnQuestCompleted(closed, 9001, _ => true, Now)).IsNull();
        await Assert.That(ledger.ExportGrants()).IsEmpty();
    }

    [Test]
    public async Task Reconcile_CatchesUpPreStateCompletions_OnceThenStaysQuiet()
    {
        var (catalog, ledger, state) = Fixture();

        // The whole chain was finished in an earlier session, before this state existed.
        var first = state.Reconcile(catalog, _ => true, Now);
        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(first[0].StatusChanged).IsTrue();
        await Assert.That(first[0].Granted).IsTrue();
        await Assert.That(state.HasGrant(MilestoneId)).IsTrue();

        // The next login finds converged state: no changes, no second grant, no second sync.
        await Assert.That(state.Reconcile(catalog, _ => true, Now)).IsEmpty();
        await Assert.That(ledger.ExportGrants().Count).IsEqualTo(1);

        // Partial progress reconciles into a record with no status edge (nothing to sync).
        var partialLedger = new SagaProgressState();
        var partialState = new MilestoneProgressState(partialLedger);
        var partial = partialState.Reconcile(catalog, id => id == 9001, Now);
        await Assert.That(partial.Count).IsEqualTo(1);
        await Assert.That(partial[0].StatusChanged).IsFalse();
        await Assert.That(partial[0].Granted).IsFalse();
        await Assert.That(partialLedger.ExportGrants()).IsEmpty();
        // Zero-progress milestones never grow a record at all.
        var untouchedLedger = new SagaProgressState();
        await Assert.That(new MilestoneProgressState(untouchedLedger)
            .Reconcile(catalog, _ => false, Now)).IsEmpty();
    }

    [Test]
    public async Task Reconcile_FailsLoud_WhenARecordOutlivesItsContentRow()
    {
        var (catalog, _ledger, state) = Fixture();
        state.Import([new MilestoneProgressRow(4999, MilestoneStatus.Active, 1)]);

        await Assert.That(() => state.Reconcile(catalog, _ => true, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChainlessMilestone_CanNeverCompleteOutOfThinAir()
    {
        // A milestone row with no quest triggers (shipped content has 35 of those): progress
        // cannot exist, so completion must not be manufactured to make the numbers work.
        var catalog = MilestoneTestCatalog.Build(rows: [(MilestoneId, true)], triggers: []);
        var ledger = new SagaProgressState();
        var state = new MilestoneProgressState(ledger);
        state.Import([new MilestoneProgressRow(MilestoneId, MilestoneStatus.Active, 0)]);

        var changes = state.Reconcile(catalog, _ => true, Now);
        await Assert.That(changes).IsEmpty();
        await Assert.That(ledger.ExportGrants()).IsEmpty();
    }

    [Test]
    public async Task MilestoneGrants_AreScopeSeparated_FromSagaGroupGrants()
    {
        // The shared ledger keys saga grants by (group, key) and milestone grants by
        // (GrantScope, milestone id): the same content key under different scopes is two grants.
        var (catalog, ledger, state) = Fixture();
        const uint sagaGroupId = 4990; // synthetic group, not a content id
        ledger.TryBeginGrant(sagaGroupId, MilestoneId);

        await Assert.That(state.HasGrant(MilestoneId)).IsFalse();
        state.Reconcile(catalog, _ => true, Now);
        await Assert.That(state.HasGrant(MilestoneId)).IsTrue();

        var rows = ledger.ExportGrants();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Any(row =>
            row.GroupId == MilestoneProgressState.GrantScope && row.GrantKey == MilestoneId)).IsTrue();
        await Assert.That(rows.Any(row =>
            row.GroupId == sagaGroupId && row.GrantKey == MilestoneId)).IsTrue();
    }

    [Test]
    public async Task SyncEdges_AreTheOnesAClientWouldBeToldAbout()
    {
        // Sync contract the character layer consumes: StatusChanged is the packet edge, and it
        // fires exactly once across the whole lifecycle (creation with partial progress carries
        // no edge — there is no milestone list packet to announce a record in).
        var (catalog, _ledger, state) = Fixture();
        HashSet<uint> completed = [];
        var edges = 0;

        foreach (var questId in new[] { 9001u, 9002u, 9001u, 9003u, 9003u })
        {
            completed.Add(questId);
            var change = state.OnQuestCompleted(catalog, questId, completed.Contains, Now);
            if (change is { StatusChanged: true })
                edges++;
        }

        await Assert.That(edges).IsEqualTo(1);
    }
}
