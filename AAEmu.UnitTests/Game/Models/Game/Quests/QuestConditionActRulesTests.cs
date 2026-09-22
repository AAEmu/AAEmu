using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestConditionActRulesTests
{
    // quest_act_obj_conditions 23: quest 6774 waits for quest 6621 to reach condition 2 (fail).
    [Test]
    public async Task MetByStep_FailNeedsTheFailStep()
    {
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Fail, QuestComponentKind.Fail)).IsTrue();
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Fail, QuestComponentKind.Progress)).IsFalse();
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Fail, QuestComponentKind.Reward)).IsFalse();
    }

    [Test]
    public async Task MetByStep_ReadyAndProgressMapToTheirSteps()
    {
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Ready, QuestComponentKind.Ready)).IsTrue();
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Progress, QuestComponentKind.Progress)).IsTrue();
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Progress, QuestComponentKind.Ready)).IsFalse();
    }

    [Test]
    public async Task Complete_IsTheCompletedFlagNotAStep()
    {
        await Assert.That(QuestConditionActRules.MetByStep(QuestConditionObj.Complete, QuestComponentKind.Reward)).IsFalse();
        await Assert.That(QuestConditionActRules.MetByCompletion(QuestConditionObj.Complete, true)).IsTrue();
        await Assert.That(QuestConditionActRules.MetByCompletion(QuestConditionObj.Complete, false)).IsFalse();
        await Assert.That(QuestConditionActRules.MetByCompletion(QuestConditionObj.Fail, true)).IsFalse();
    }
}
