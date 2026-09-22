using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestAcceptBuffRulesTests
{
    // quest_act_con_accept_buffs 30: capture quest 9340, buff 24422, started by buff_triggers row
    // 29517's AcceptQuestEffect 99 on event 12 Started.
    [Test]
    public async Task Quest9340_AcceptsFromItsBuff()
    {
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Buff, 24422, 24422, false)).IsTrue();
    }

    [Test]
    public async Task Quest9340_AcceptsWhileTheBuffIsCarried()
    {
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Unknown, 0, 24422, true)).IsTrue();
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Npc, 7504, 24422, true)).IsTrue();
    }

    [Test]
    public async Task Quest9340_RefusesWithoutTheBuff()
    {
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Unknown, 0, 24422, false)).IsFalse();
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Npc, 7504, 24422, false)).IsFalse();
        // Row 27's buff 24423 (quest 9339) does not start 9340.
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Buff, 24423, 24422, false)).IsFalse();
    }

    [Test]
    public async Task NoBuffId_NeverAccepts()
    {
        await Assert.That(QuestAcceptBuffRules.Accepts(QuestAcceptorType.Buff, 0, 0, true)).IsFalse();
    }
}
