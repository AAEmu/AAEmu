using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestEffectFireRulesTests
{
    // quest_act_obj_effect_fires 132: quest 950, effect 78463 (InteractionEffect), count 1, team_share t.
    [Test]
    public async Task Counts_OnlyTheRowsEffectId()
    {
        await Assert.That(QuestEffectFireRules.Counts(78463, 78463)).IsTrue();
        await Assert.That(QuestEffectFireRules.Counts(78463, 78417)).IsFalse();
        await Assert.That(QuestEffectFireRules.Counts(0, 0)).IsFalse();
    }

    // Row 131 (quest 3514, effect 78417) is one of the team_share f rows.
    [Test]
    public async Task SharesWithTeam_OnlyFromTheFiringCharacter()
    {
        await Assert.That(QuestEffectFireRules.SharesWithTeam(true, 10, 10)).IsTrue();
        await Assert.That(QuestEffectFireRules.SharesWithTeam(true, 10, 11)).IsFalse();
        await Assert.That(QuestEffectFireRules.SharesWithTeam(false, 10, 10)).IsFalse();
        await Assert.That(QuestEffectFireRules.SharesWithTeam(true, 0, 0)).IsFalse();
    }

    // Row 15: quest 1388, effect 61085 (BuffEffect), count 3, quest score 0.
    [Test]
    public async Task ObjectiveMet_FollowsTheRowCount()
    {
        await Assert.That(QuestProgressActRules.ObjectiveMet(2, 3)).IsFalse();
        await Assert.That(QuestProgressActRules.ObjectiveMet(3, 3)).IsTrue();
    }
}
