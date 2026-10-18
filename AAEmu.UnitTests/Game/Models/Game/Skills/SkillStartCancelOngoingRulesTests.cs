using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillStartCancelOngoingRulesTests
{
    [Test]
    public async Task PlayerPress_WithCancelFlag_Applies()
    {
        await Assert.That(SkillStartCancelOngoingRules.ShouldApply(bypassGcd: false, templateCancelOngoingBuffs: true))
            .IsTrue();
    }

    [Test]
    public async Task PlayerPress_WithoutCancelFlag_DoesNotApply()
    {
        await Assert.That(SkillStartCancelOngoingRules.ShouldApply(bypassGcd: false, templateCancelOngoingBuffs: false))
            .IsFalse();
    }

    [Test]
    public async Task NestedSkillUseBypass_DoesNotCancelEvenWhenTemplateAsks()
    {
        // UseSkillTask / cadence 운율 skill: bypassGcd true, cancel_ongoing_buffs true, no exception tag.
        await Assert.That(SkillStartCancelOngoingRules.ShouldApply(bypassGcd: true, templateCancelOngoingBuffs: true))
            .IsFalse();
    }

    [Test]
    public async Task AutoAttackBypass_DoesNotCancel()
    {
        await Assert.That(SkillStartCancelOngoingRules.ShouldApply(bypassGcd: true, templateCancelOngoingBuffs: false))
            .IsFalse();
    }
}
