using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestContributionHuntRulesTests
{
    // long_dist is 't' on all 29 contr rows, so distance never gates them.
    [Test]
    public async Task Contributes_LongDistIgnoresDistance()
    {
        await Assert.That(QuestContributionHuntRules.Contributes(true, 5000f, LootingContainer.MaxLootingRange)).IsTrue();
    }

    // A NULL or 'f' row keeps the ordinary kill-credit range (200 m).
    [Test]
    public async Task Contributes_ShortDistUsesTheKillCreditRange()
    {
        await Assert.That(QuestContributionHuntRules.Contributes(false, 200f, LootingContainer.MaxLootingRange)).IsTrue();
        await Assert.That(QuestContributionHuntRules.Contributes(false, 200.5f, LootingContainer.MaxLootingRange)).IsFalse();
    }

    // quest_act_obj_monster_contr_group_hunts 2: hero quest 9118 (score 100), group 907, count 30 -> 4 kills.
    // Row 11 of the same quest: group 887, count 1 -> 101 kills.
    [Test]
    public async Task ObjectiveMet_WeighsTheGroupCountAgainstTheQuestScore()
    {
        await Assert.That(QuestProgressActRules.ObjectiveMet(3, 30, 100)).IsFalse();
        await Assert.That(QuestProgressActRules.ObjectiveMet(4, 30, 100)).IsTrue();
        await Assert.That(QuestProgressActRules.ObjectiveMet(100, 1, 100)).IsFalse();
        await Assert.That(QuestProgressActRules.ObjectiveMet(101, 1, 100)).IsTrue();
    }

    // quest_act_obj_monster_contr_hunts 13: quest 10737 (score 0), npc 21204, count 1.
    [Test]
    public async Task ObjectiveMet_PlainCountWithoutScore()
    {
        await Assert.That(QuestProgressActRules.ObjectiveMet(0, 1)).IsFalse();
        await Assert.That(QuestProgressActRules.ObjectiveMet(1, 1)).IsTrue();
    }
}
