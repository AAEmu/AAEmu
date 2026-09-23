using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.UnitTests.Game.Models.Game.Sagas;

/// <summary>
/// Content validation: group order, shipped quest order, and the loud rejection of membership rows
/// whose group or quest_context row does not exist.
/// </summary>
public class SagaQuestCatalogTests
{
    [Test]
    public async Task Groups_AreOrderedById_EvenWhenRowsArriveOutOfOrder()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(10u, 0u, 127u), (1u, 0u, 110u), (4u, 0u, 115u)],
            members: [(1u, 9001u), (4u, 9004u), (10u, 9010u)]);

        await Assert.That(catalog.Groups.Select(group => group.Id)).IsEquivalentTo([1u, 4u, 10u]);
    }

    [Test]
    public async Task Quests_KeepShippedMembershipRowOrder_NotQuestIdOrder()
    {
        // Shipped membership order is the progression order; quest ids inside a group are not
        // monotonic (an authored row can sit between two lower ids).
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 0u, 110u)],
            members: [(1u, 9002u), (1u, 9001u), (1u, 9003u)]);

        await Assert.That(catalog.GetGroup(1).OrderedQuestIds).IsEquivalentTo([9002u, 9001u, 9003u]);
    }

    [Test]
    public async Task MembershipWithoutGroupRow_IsSkippedAndReportedLoudly()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 0u, 110u)],
            members: [(1u, 9001u), (12u, 9068u)]);

        var issue = catalog.Issues.SingleOrDefault(row => row.SagaQuestGroupId == 12);
        await Assert.That(issue).IsNotNull();
        await Assert.That(issue.Reason).IsEqualTo("saga_quest_groups row missing");
        await Assert.That(catalog.TryGetGroup(12, out _)).IsFalse();
        await Assert.That(catalog.Groups.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MembershipWithMissingQuestContextRow_IsSkippedAndReportedLoudly()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 0u, 110u)],
            members: [(1u, 9001u), (1u, 9002u)],
            questExists: questId => questId != 9002u);

        var issue = catalog.Issues.SingleOrDefault(row => row.QuestContextId == 9002u);
        await Assert.That(issue).IsNotNull();
        await Assert.That(issue.Reason).IsEqualTo("quest_contexts row missing");
        await Assert.That(catalog.GetGroup(1).OrderedQuestIds).IsEquivalentTo([9001u]);
    }

    [Test]
    public async Task GroupLeftWithoutUsableMembers_IsReportedAndNeverCompletes()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(12u, 0u, 10u)],
            members: [(12u, 9068u)],
            questExists: _ => false);

        await Assert.That(catalog.Issues.Any(row => row.SagaQuestGroupId == 12)).IsTrue();

        var group = catalog.GetGroup(12);
        await Assert.That(group.OrderedQuestIds).IsEmpty();
        // Completing "all members" vacuously on an empty group would grant a reward out of thin air.
        await Assert.That(group.IsComplete(_ => true)).IsFalse();
    }

    [Test]
    public async Task CompletionCondition_QuestFinishesTheGroup_OnlyWhenThatQuestIsDone()
    {
        const uint conditionQuest = 9003u;
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, conditionQuest, 110u)],
            members: [(1u, 9001u), (1u, 9002u), (1u, conditionQuest)]);
        var group = catalog.GetGroup(1);

        await Assert.That(group.IsComplete(questId => questId == 9001u)).IsFalse();
        await Assert.That(group.IsComplete(questId => questId == conditionQuest)).IsTrue();
    }

    [Test]
    public async Task CompletionCondition_OutsideTheGroupsMembership_CannotComplete()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 9999u, 110u)],
            members: [(1u, 9001u)]);

        await Assert.That(catalog.GetGroup(1).IsComplete(_ => true)).IsFalse();
    }

    [Test]
    public async Task AllMembersDone_FinishesAGroupWithoutACompletionCondition()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 0u, 110u)],
            members: [(1u, 9001u), (1u, 9002u)]);
        var group = catalog.GetGroup(1);

        await Assert.That(group.IsComplete(questId => questId == 9001u)).IsFalse();
        await Assert.That(group.IsComplete(_ => true)).IsTrue();
        await Assert.That(group.CompletedCount(questId => questId == 9001u)).IsEqualTo(1);
    }

    [Test]
    public async Task GetGroup_UnknownId_ThrowsInsteadOfReturningNull()
    {
        var catalog = SagaTestCatalog.Build([(1u, 0u, 110u)], [(1u, 9001u)]);

        await Assert.That(() => catalog.GetGroup(42)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FindGroupByQuest_ResolvesMembersAndIgnoresForeignQuests()
    {
        var catalog = SagaTestCatalog.Build(
            groups: [(1u, 0u, 110u)],
            members: [(1u, 9001u)]);

        await Assert.That(catalog.FindGroupByQuest(9001u)?.Id).IsEqualTo(1u);
        await Assert.That(catalog.FindGroupByQuest(12345u)).IsNull();
    }
}
