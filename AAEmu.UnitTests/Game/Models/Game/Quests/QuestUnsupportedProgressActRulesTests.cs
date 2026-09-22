using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestUnsupportedProgressActRulesTests
{
    [Test]
    public async Task FirstReport_ReportsEachActTypeOnce()
    {
        var reported = new HashSet<string>();
        await Assert.That(QuestUnsupportedProgressActRules.FirstReport(reported, "QuestActObjFactionCompetition")).IsTrue();
        await Assert.That(QuestUnsupportedProgressActRules.FirstReport(reported, "QuestActObjFactionCompetition")).IsFalse();
        await Assert.That(QuestUnsupportedProgressActRules.FirstReport(reported, "QuestActObjConquestWar")).IsTrue();
    }

    [Test]
    public async Task FirstReport_IgnoresMissingInput()
    {
        await Assert.That(QuestUnsupportedProgressActRules.FirstReport(new HashSet<string>(), "")).IsFalse();
        await Assert.That(QuestUnsupportedProgressActRules.FirstReport(null, "QuestActObjConquestWar")).IsFalse();
    }
}
