using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillSynergyRulesTests
{
    [Test]
    public async Task OrdinaryEffect_AlwaysLands()
    {
        await Assert.That(SkillSynergyRules.AllowsSynergyEffect(
            effectIsSynergy: false, skillHasSynergyTags: false, targetHasAnySynergyTag: false)).IsTrue();
        await Assert.That(SkillSynergyRules.AllowsSynergyEffect(
            effectIsSynergy: false, skillHasSynergyTags: true, targetHasAnySynergyTag: false)).IsTrue();
    }

    [Test]
    public async Task SynergyEffect_NeedsTheTargetToCarryOneOfTheSkillsTags()
    {
        // 10134 지옥의 창 synergises with 수면 (sleep, tag 97).
        await Assert.That(SkillSynergyRules.AllowsSynergyEffect(
            effectIsSynergy: true, skillHasSynergyTags: true, targetHasAnySynergyTag: true)).IsTrue();
        await Assert.That(SkillSynergyRules.AllowsSynergyEffect(
            effectIsSynergy: true, skillHasSynergyTags: true, targetHasAnySynergyTag: false)).IsFalse();
    }

    [Test]
    public async Task SynergyEffect_OnASkillWithNoTags_NeverLands()
    {
        await Assert.That(SkillSynergyRules.AllowsSynergyEffect(
            effectIsSynergy: true, skillHasSynergyTags: false, targetHasAnySynergyTag: true)).IsFalse();
    }
}
