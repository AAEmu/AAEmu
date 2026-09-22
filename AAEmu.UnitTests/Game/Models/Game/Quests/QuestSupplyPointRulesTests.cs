using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestSupplyPointRulesTests
{
    // quest_act_supply_leadership_points 2 (quest 2971) gives 30, row 3 (quest 6572) gives 10.
    [Test]
    public async Task Leadership_GrantsTheRowPoint()
    {
        await Assert.That(QuestSupplyPointRules.Grant(30)).IsEqualTo(30);
        await Assert.That(QuestSupplyPointRules.Grant(10)).IsEqualTo(10);
    }

    // quest_act_supply_actabilities 2 (quest 4445, group 34) gives 50; row 580 (quest 8731, group 1) 5000.
    [Test]
    public async Task Actability_GrantsTheRowPoint()
    {
        await Assert.That(QuestSupplyPointRules.Grant(50)).IsEqualTo(50);
        await Assert.That(QuestSupplyPointRules.Grant(5000)).IsEqualTo(5000);
    }

    // quest_act_supply_local_lps 2 (festival quest 11175) gives 150.
    [Test]
    public async Task LocalLabor_GrantsTheRowAmount()
    {
        await Assert.That(QuestSupplyPointRules.Grant(150)).IsEqualTo(150);
    }

    [Test]
    public async Task NonPositive_GrantsNothing()
    {
        await Assert.That(QuestSupplyPointRules.Grant(0)).IsEqualTo(0);
        await Assert.That(QuestSupplyPointRules.Grant(-5)).IsEqualTo(0);
    }
}
