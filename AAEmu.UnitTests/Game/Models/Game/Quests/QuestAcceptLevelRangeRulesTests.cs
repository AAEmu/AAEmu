using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestAcceptLevelRangeRulesTests
{
    // quest_act_con_accept_level_ranges 2: festival quest 10930, level_min 10, level_max 19.
    [Test]
    public async Task Quest10930_PassesInsideTenToNineteen()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(10, 10, 19)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(15, 10, 19)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(19, 10, 19)).IsTrue();
    }

    [Test]
    public async Task Quest10930_FailsOutsideTenToNineteen()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(9, 10, 19)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(20, 10, 19)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(55, 10, 19)).IsFalse();
    }

    // Rows 16 and 24: anniversary quests 10534 (10..125) and 10542 (54..125), whose quest_contexts
    // min_level and max_level are 0.
    [Test]
    public async Task AnniversaryChain_GatesOnLevelMinOnly()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(10, 10, 125)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(125, 10, 125)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(9, 10, 125)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(53, 54, 125)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(54, 54, 125)).IsTrue();
    }
}
