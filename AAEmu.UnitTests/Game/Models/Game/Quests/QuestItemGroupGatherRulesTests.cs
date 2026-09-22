using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestItemGroupGatherRulesTests
{
    // quest_item_group_items for group 2 (row 2, quest 1955, count 1).
    private static readonly uint[] Group2 = [3557, 7992, 7994, 7998, 8001, 8010, 8012, 8016, 8018, 16232, 7763, 8006];

    // Group 9 (row 20, quest 5490, count 10, cleanup t).
    private static readonly uint[] Group9 = [28557, 28558, 29288];

    [Test]
    public async Task CountInGroup_SumsEveryItemOfTheGroup()
    {
        var bag = new Dictionary<uint, int> { [3557] = 1, [8001] = 2, [7763] = 3 };
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group2, id => bag.GetValueOrDefault(id))).IsEqualTo(6);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group2, _ => 0)).IsEqualTo(0);
    }

    [Test]
    public async Task CountInGroup_IgnoresDuplicatesNegativesAndMissingInput()
    {
        await Assert.That(QuestItemGroupGatherRules.CountInGroup([28557, 28557], _ => 4)).IsEqualTo(4);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group9, _ => -1)).IsEqualTo(0);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(null, _ => 1)).IsEqualTo(0);
        await Assert.That(QuestItemGroupGatherRules.CountInGroup(Group9, null)).IsEqualTo(0);
    }

    [Test]
    public async Task CleanupPlan_RemovesAtMostTheObjectiveInContentOrder()
    {
        var bag = new Dictionary<uint, int> { [28557] = 4, [28558] = 8, [29288] = 1 };
        var plan = QuestItemGroupGatherRules.CleanupPlan(Group9, id => bag.GetValueOrDefault(id), 10).ToList();
        await Assert.That(plan).IsEquivalentTo([(28557u, 4), (28558u, 6)]);
    }

    [Test]
    public async Task CleanupPlan_SkipsEmptyStacksAndStopsAtZero()
    {
        var bag = new Dictionary<uint, int> { [28558] = 2 };
        var plan = QuestItemGroupGatherRules.CleanupPlan(Group9, id => bag.GetValueOrDefault(id), 5).ToList();
        await Assert.That(plan).IsEquivalentTo([(28558u, 2)]);
        await Assert.That(QuestItemGroupGatherRules.CleanupPlan(Group9, id => bag.GetValueOrDefault(id), 0)).IsEmpty();
        await Assert.That(QuestItemGroupGatherRules.CleanupPlan(null, _ => 1, 5)).IsEmpty();
    }
}
