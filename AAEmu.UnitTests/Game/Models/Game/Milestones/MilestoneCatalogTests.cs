using AAEmu.Game.Models.Game.Milestones;

namespace AAEmu.UnitTests.Game.Models.Game.Milestones;

/// <summary>
/// The content model: reversed quest → milestone indexing, validation that reports rejected rows
/// instead of dropping them, and eligibility read straight from the milestone row's own columns.
/// </summary>
public class MilestoneCatalogTests
{
    [Test]
    public async Task Build_IndexesReversedTriggers_AndOrdersEverythingByShippedId()
    {
        // Shuffled in on purpose: output order must come from content ids, not arrival order.
        var catalog = MilestoneTestCatalog.Build(
            rows: [(5002, true), (5001, true)],
            triggers: [(9010, 5002), (9005, 5001), (9001, 5001)]);

        await Assert.That(catalog.Rows.Select(row => row.Id)).IsEquivalentTo([5001u, 5002u]);
        await Assert.That(catalog.FindMilestoneByQuest(9010)).IsEqualTo(5002u);
        await Assert.That(catalog.FindMilestoneByQuest(9005)).IsEqualTo(5001u);
        // The chain is the milestone's trigger set in ascending quest id order — the order the
        // completion walk consumes, straight from the reversed content rows.
        await Assert.That(catalog.GetQuestChain(5001)).IsEquivalentTo([9001u, 9005u]);
        await Assert.That(catalog.Issues).IsEmpty();
    }

    [Test]
    public async Task Build_RejectsTriggerForMissingMilestone_Loudly()
    {
        var catalog = MilestoneTestCatalog.Build(
            rows: [(5001, true)],
            triggers: [(9001, 5001), (9002, 4999)]);

        await Assert.That(catalog.Issues.Count).IsEqualTo(1);
        var issue = catalog.Issues[0];
        await Assert.That(issue.QuestId).IsEqualTo(9002u);
        await Assert.That(issue.MilestoneId).IsEqualTo(4999u);
        await Assert.That(issue.Reason).IsEqualTo("milestones row missing");
        // The rejected trigger never reaches the index: the quest advances nothing.
        await Assert.That(catalog.FindMilestoneByQuest(9002)).IsEqualTo(0u);
    }

    [Test]
    public async Task Build_RejectsDuplicateId_AndWindowThatEndsBeforeItStarts()
    {
        var catalog = MilestoneCatalog.Build(
            [
                new MilestoneRow(5001, MilestoneTestCatalog.Now.AddDays(-1),
                    MilestoneTestCatalog.Now.AddDays(1), true),
                new MilestoneRow(5001, MilestoneTestCatalog.Now.AddDays(-1),
                    MilestoneTestCatalog.Now.AddDays(1), true),
                new MilestoneRow(5002, MilestoneTestCatalog.Now.AddDays(2),
                    MilestoneTestCatalog.Now.AddDays(1), true),
            ],
            []);

        await Assert.That(catalog.Issues.Count).IsEqualTo(2);
        await Assert.That(catalog.Rows.Select(row => row.Id)).IsEquivalentTo([5001u]);
        await Assert.That(catalog.Issues.Select(issue => issue.Reason)).Contains(
            "duplicate milestone id");
        await Assert.That(catalog.Issues.Select(issue => issue.Reason)).Contains(
            "end_date precedes start_date — window unusable, row dropped");
    }

    [Test]
    public async Task GetRow_UnknownId_ThrowsInsteadOfReturningAGhost()
    {
        var catalog = MilestoneTestCatalog.Build(rows: [(5001, true)], triggers: []);

        await Assert.That(() => catalog.GetRow(4999)).Throws<InvalidOperationException>();
        await Assert.That(catalog.Contains(4999)).IsFalse();
        await Assert.That(catalog.Contains(5001)).IsTrue();
    }

    [Test]
    public async Task Eligibility_ComesFromTheRowColumns_ReleaseFlagAndWindow()
    {
        var now = MilestoneTestCatalog.Now;
        var catalog = MilestoneCatalog.Build(
            [
                new MilestoneRow(5001, now.AddDays(-1), now.AddDays(1), true),
                new MilestoneRow(5002, now.AddDays(-1), now.AddDays(1), false),
                new MilestoneRow(5003, now.AddDays(1), now.AddDays(2), true),
                new MilestoneRow(5004, now.AddDays(-2), now.AddDays(-1), true),
            ],
            []);

        await Assert.That(catalog.IsEligible(5001, now)).IsTrue();
        // Unreleased content never advances, whatever the clock says.
        await Assert.That(catalog.IsEligible(5002, now)).IsFalse();
        // Window not opened yet / already closed.
        await Assert.That(catalog.IsEligible(5003, now)).IsFalse();
        await Assert.That(catalog.IsEligible(5004, now)).IsFalse();
        // The window is inclusive at both bounds.
        await Assert.That(catalog.IsEligible(5003, now.AddDays(1))).IsTrue();
        await Assert.That(catalog.IsEligible(5004, now.AddDays(-1))).IsTrue();
        // Missing content is not eligible — it is a failure, surfaced by GetRow above.
        await Assert.That(catalog.IsEligible(4999, now)).IsFalse();
    }

    [Test]
    public async Task FindMilestoneByQuest_UntaggedQuest_AdvancesNothing()
    {
        var catalog = MilestoneTestCatalog.Build(rows: [(5001, true)], triggers: [(9001, 5001)]);

        await Assert.That(catalog.FindMilestoneByQuest(9999)).IsEqualTo(0u);
        await Assert.That(catalog.GetQuestChain(5002)).IsEmpty();
    }
}
