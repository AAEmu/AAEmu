using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Sagas;

/// <summary>
/// The progression state machine: eligibility → active → complete → reward granted exactly once,
/// group ordering-driven evaluation, and refusal of every re-grant.
/// </summary>
public class SagaProgressStateTests
{
    private const uint GroupId = 8;
    private const uint MilestoneKey = 130;
    private const uint QuestA = 9001;
    private const uint QuestB = 9002;
    private const uint QuestC = 9003;

    private static SagaQuestCatalog Catalog(
        (uint Id, uint CompletionCondId, uint MilestoneId)[] groups = null,
        (uint GroupId, uint QuestId)[] members = null) =>
        SagaTestCatalog.Build(
            groups ?? [(GroupId, 0u, MilestoneKey)],
            members ?? [(GroupId, QuestA), (GroupId, QuestB), (GroupId, QuestC)]);

    [Test]
    public async Task SagaQuest_WithoutBoughtGroup_RefusesStartWithChronicleInfoNeed()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();

        await Assert.That(state.EvaluateStartGate(catalog, QuestA))
            .IsEqualTo(SagaStartGate.ChronicleInfoNeed);
        // A quest outside every saga group is not this gate's business.
        await Assert.That(state.EvaluateStartGate(catalog, 12345u))
            .IsEqualTo(SagaStartGate.Allowed);
    }

    [Test]
    public async Task Unlock_MakesSagaStartable_AndNeverRewindsARecord()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();

        await Assert.That(state.Unlock(catalog, GroupId)).IsEqualTo(SagaUnlockResult.Ok);
        await Assert.That(state.IsUnlocked(GroupId)).IsTrue();
        await Assert.That(state.EvaluateStartGate(catalog, QuestA))
            .IsEqualTo(SagaStartGate.Allowed);
        await Assert.That(state.TryGetRecord(GroupId, out var record)).IsTrue();
        await Assert.That(record.Status).IsEqualTo(SagaGroupStatus.Active);

        // Run the group to completion, then re-buy: a finished group must stay finished.
        state.Reconcile(catalog, _ => true);
        await Assert.That(state.Unlock(catalog, GroupId)).IsEqualTo(SagaUnlockResult.AlreadyUnlocked);
        await Assert.That(state.TryGetRecord(GroupId, out record)).IsTrue();
        await Assert.That(record.Status).IsEqualTo(SagaGroupStatus.Complete);
    }

    [Test]
    public async Task Unlock_UnknownGroup_FailsLoudly()
    {
        var catalog = Catalog();

        await Assert.That(new SagaProgressState().Unlock(catalog, 42))
            .IsEqualTo(SagaUnlockResult.UnknownGroup);
    }

    [Test]
    public async Task Lifecycle_EligibilityActiveComplete_GrantingExactlyOnce()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();
        state.Unlock(catalog, GroupId);
        HashSet<uint> completed = [];

        // Before the completed bit exists the event cannot credit anything — counts follow the
        // ground truth, not the event itself.
        await Assert.That(state.OnQuestCompleted(catalog, QuestA, completed.Contains)).IsNull();
        completed.Add(QuestA);
        var credited = state.OnQuestCompleted(catalog, QuestA, completed.Contains);
        await Assert.That(credited).IsNotNull();
        await Assert.That(credited.StatusChanged).IsFalse();
        await Assert.That(credited.RewardGranted).IsFalse();
        // Replaying the same completion event cannot double-count.
        await Assert.That(state.OnQuestCompleted(catalog, QuestA, completed.Contains)).IsNull();
        await Assert.That(state.TryGetRecord(GroupId, out var record)).IsTrue();
        await Assert.That((int)record.CompletedCount).IsEqualTo(1);
        await Assert.That(record.Status).IsEqualTo(SagaGroupStatus.Active);

        completed.Add(QuestB);
        var mid = state.OnQuestCompleted(catalog, QuestB, completed.Contains);
        await Assert.That(mid.StatusChanged).IsFalse();
        await Assert.That(mid.RewardGranted).IsFalse();
        await Assert.That((int)mid.CompletedCount).IsEqualTo(2);

        // The last member finishes the group and takes the grant — the one edge W14 reuses.
        completed.Add(QuestC);
        var finish = state.OnQuestCompleted(catalog, QuestC, completed.Contains);
        await Assert.That(finish.StatusChanged).IsTrue();
        await Assert.That(finish.PreviousStatus).IsEqualTo(SagaGroupStatus.Active);
        await Assert.That(finish.CurrentStatus).IsEqualTo(SagaGroupStatus.Complete);
        await Assert.That(finish.RewardGranted).IsTrue();
        await Assert.That(finish.RewardGrantKey).IsEqualTo(MilestoneKey);
        await Assert.That((int)finish.CompletedCount).IsEqualTo(3);

        // Replaying the completion edge grants nothing more.
        await Assert.That(state.OnQuestCompleted(catalog, QuestC, completed.Contains)).IsNull();
        await Assert.That(state.HasGrant(GroupId, MilestoneKey)).IsTrue();
        await Assert.That(state.TryBeginGrant(GroupId, MilestoneKey)).IsFalse();
        await Assert.That(state.TryGetRecord(GroupId, out record)).IsTrue();
        await Assert.That(record.Status).IsEqualTo(SagaGroupStatus.Complete);
    }

    [Test]
    public async Task CompletionConditionGroup_FinishesOnTheConditionQuestAlone()
    {
        var catalog = Catalog(
            groups: [(GroupId, QuestC, MilestoneKey)],
            members: [(GroupId, QuestA), (GroupId, QuestB), (GroupId, QuestC)]);
        var state = new SagaProgressState();
        state.Unlock(catalog, GroupId);

        HashSet<uint> completed = [QuestC];
        var change = state.OnQuestCompleted(catalog, QuestC, completed.Contains);

        await Assert.That(change.CurrentStatus).IsEqualTo(SagaGroupStatus.Complete);
        await Assert.That(change.RewardGranted).IsTrue();
        // The two non-condition members are still unfinished progress.
        await Assert.That((int)change.CompletedCount).IsEqualTo(1);
    }

    [Test]
    public async Task LockedGroups_NeverAdvance_FromCompletionEvents()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();

        var change = state.OnQuestCompleted(catalog, QuestA, _ => true);

        await Assert.That(change).IsNull();
        await Assert.That(state.ExportGroups()).IsEmpty();
    }

    [Test]
    public async Task SharedMilestoneKey_CollectsOneGrantPerGroup()
    {
        var catalog = Catalog(
            groups: [(3u, 0u, MilestoneKey), (4u, 0u, MilestoneKey)],
            members: [(3u, 9003u), (4u, 9004u)]);
        var state = new SagaProgressState();
        state.Unlock(catalog, 3);
        state.Unlock(catalog, 4);

        var group3 = state.OnQuestCompleted(catalog, 9003u, _ => true);
        var group4 = state.OnQuestCompleted(catalog, 9004u, _ => true);

        await Assert.That(group3.RewardGranted).IsTrue();
        await Assert.That(group4.RewardGranted).IsTrue();
        await Assert.That(state.HasGrant(3, MilestoneKey)).IsTrue();
        await Assert.That(state.HasGrant(4, MilestoneKey)).IsTrue();
        await Assert.That(state.ExportGrants().Count).IsEqualTo(2);
    }

    [Test]
    public async Task Reconcile_CatchesUpFromCompletedQuests_ThenStaysQuiet()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();
        state.Unlock(catalog, GroupId);

        // Character already finished the whole chain in an earlier session.
        var changes = state.Reconcile(catalog, _ => true);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].CurrentStatus).IsEqualTo(SagaGroupStatus.Complete);
        await Assert.That(changes[0].RewardGranted).IsTrue();
        await Assert.That(state.HasGrant(GroupId, MilestoneKey)).IsTrue();

        // A second pass (next login) finds converged state: no changes, no second grant.
        await Assert.That(state.Reconcile(catalog, _ => true)).IsEmpty();
        await Assert.That(state.TryBeginGrant(GroupId, MilestoneKey)).IsFalse();
    }

    [Test]
    public async Task Reconcile_NeverCreatesRecordsForLockedGroups()
    {
        var catalog = Catalog();
        var state = new SagaProgressState();

        await Assert.That(state.Reconcile(catalog, _ => true)).IsEmpty();
        await Assert.That(state.ExportGroups()).IsEmpty();
    }

    [Test]
    public async Task GroupOrdering_EvaluationFollowsCatalogGroupForEachQuest()
    {
        var catalog = Catalog(
            groups: [(1u, 0u, 110u), (2u, 0u, 116u)],
            members: [(1u, 9001u), (2u, 9002u)]);
        var state = new SagaProgressState();
        state.Unlock(catalog, 1);
        state.Unlock(catalog, 2);

        var change = state.OnQuestCompleted(catalog, 9002u, _ => true);

        await Assert.That(change.GroupId).IsEqualTo(2u);
        await Assert.That(state.TryGetRecord(1, out var one)).IsTrue();
        await Assert.That((int)one.CompletedCount).IsEqualTo(0);
    }
}
