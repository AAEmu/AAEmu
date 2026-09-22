using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestItemGroupGatherRulesTests
{
    private static QuestGroupItemEntry Item(uint itemId) => new(itemId, []);

    // quest_item_group_items for group 2 (row 2, quest 1955, count 1).
    private static readonly QuestGroupItemEntry[] Group2 =
        [Item(3557), Item(7992), Item(7994), Item(7998), Item(8001), Item(8010), Item(8012), Item(8016), Item(8018), Item(16232), Item(7763), Item(8006)];

    // Group 9 (row 20, quest 5490, count 10, cleanup t).
    private static readonly QuestGroupItemEntry[] Group9 = [Item(28557), Item(28558), Item(29288)];

    // Group 85 (quest 10835, "(고급 이상)"): 45217 has a quest_item_group_items row per grade 2..12,
    // and 45218 is gated over the same band.
    private static readonly QuestGroupItemEntry[] Group85 =
        [new(45217, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]), new(45218, [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])];

    // Group 94 (quest 11112, "(유물 이상)"): 46590 lists grades 7 and 8 only.
    private static readonly QuestGroupItemEntry[] Group94 = [new(46590, [7, 8]), new(46593, [7, 8])];

    [Test]
    public async Task CountInGroup_SumsEveryItemOfTheGroup()
    {
        var bag = new Dictionary<uint, int> { [3557] = 1, [8001] = 2, [7763] = 3 };
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group2, (id, _) => bag.GetValueOrDefault(id))).IsEqualTo(6);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group2, (_, _) => 0)).IsEqualTo(0);
    }

    [Test]
    public async Task CountInGroup_IgnoresDuplicatesNegativesAndMissingInput()
    {
        await Assert.That(QuestItemGroupGatherRules.CountInGroup([Item(28557), Item(28557)], (_, _) => 4)).IsEqualTo(4);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group9, (_, _) => -1)).IsEqualTo(0);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(null, (_, _) => 1)).IsEqualTo(0);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group9, null)).IsEqualTo(0);
    }

    [Test]
    public async Task CountInGroup_GatedRowOnlyCountsTheListedGrades()
    {
        // The bag holds 45217 at grade 1 and grade 7: only grade 7 is a group-85 row.
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group85, (id, grade) => id == 45217 && grade == 7 ? 1 : 0)).IsEqualTo(1);
        // Two copies at listed grades sum.
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group94, (id, grade) => id == 46590 ? grade : 0)).IsEqualTo(15);
        // A grade the group does not list never counts, so a lower-grade copy cannot satisfy it.
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group94, (_, grade) => grade == 6 ? 4 : 0)).IsEqualTo(0);
        // Ungated items of the same group keep counting every grade.
        await Assert.That(QuestItemGroupGatherRules.CountInGroup([new(46590, []), new(46593, [7, 8])], (_, grade) => grade < 0 ? 2 : 0)).IsEqualTo(2);
    }

    [Test]
    public async Task CleanupPlan_RemovesAtMostTheObjectiveInContentOrder()
    {
        var bag = new Dictionary<uint, int> { [28557] = 4, [28558] = 8, [29288] = 1 };
        var plan = QuestItemGroupGatherRules.CleanupPlan(Group9, (id, _) => bag.GetValueOrDefault(id), 10).ToList();
        await Assert.That(plan).IsEquivalentTo([(28557u, 4, -1), (28558u, 6, -1)]);
    }

    [Test]
    public async Task CleanupPlan_SkipsEmptyStacksAndStopsAtZero()
    {
        var bag = new Dictionary<uint, int> { [28558] = 2 };
        var plan = QuestItemGroupGatherRules.CleanupPlan(Group9, (id, _) => bag.GetValueOrDefault(id), 5).ToList();
        await Assert.That(plan).IsEquivalentTo([(28558u, 2, -1)]);
        await Assert.That(QuestItemGroupGatherRules.CleanupPlan(Group9, (id, _) => bag.GetValueOrDefault(id), 0)).IsEmpty();
        await Assert.That(QuestItemGroupGatherRules.CleanupPlan(null, (_, _) => 1, 5)).IsEmpty();
    }

    [Test]
    public async Task CleanupPlan_TakesOnlyTheGradesTheObjectiveCounted()
    {
        // Group 85 lists 45217 at grades 2..12, so a grade-3 copy would be planned before grade 7;
        // only the grades the counter reports are taken, and here that is the two grade-7 copies.
        var plan = QuestItemGroupGatherRules.CleanupPlan(Group85, (id, grade) => id == 45217 && grade == 7 ? 2 : 0, 3).ToList();
        await Assert.That(plan).IsEquivalentTo([(45217u, 2, 7)]);
    }
}
