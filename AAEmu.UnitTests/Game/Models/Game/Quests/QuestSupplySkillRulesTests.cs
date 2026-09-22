using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestSupplySkillRulesTests
{
    // quest_act_supply_skills 14: festival quest 10452, skill 46452, in Ready component 45516 next to
    // quest_act_con_report_npcs 8386.
    [Test]
    public async Task Quest10452_CastsOnceTheReportCompletedTheComponent()
    {
        await Assert.That(QuestSupplySkillRules.ShouldCast(true, false, 46452)).IsTrue();
    }

    [Test]
    public async Task Quest10452_WaitsForTheReport()
    {
        await Assert.That(QuestSupplySkillRules.ShouldCast(false, false, 46452)).IsFalse();
    }

    [Test]
    public async Task Quest10452_CastsOnlyOnce()
    {
        await Assert.That(QuestSupplySkillRules.ShouldCast(true, true, 46452)).IsFalse();
    }

    [Test]
    public async Task NoSkill_NeverCasts()
    {
        await Assert.That(QuestSupplySkillRules.ShouldCast(true, false, 0)).IsFalse();
    }
}
