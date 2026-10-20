using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCooldownCancelRulesTests
{
    [Test]
    public async Task ShouldRefund_WhenSkillCooldownWasArmedAtAccept()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnCancel(
            skillCooldownArmed: true, gcdArmed: false)).IsTrue();
    }

    [Test]
    public async Task ShouldRefund_WhenOnlyGcdWasArmed()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnCancel(
            skillCooldownArmed: false, gcdArmed: true)).IsTrue();
    }

    [Test]
    public async Task ShouldNotRefund_WhenNothingWasArmed()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnCancel(
            skillCooldownArmed: false, gcdArmed: false)).IsFalse();
    }

    [Test]
    public async Task CancelledPlot_RefundsOnlyBeforeItCommits()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundCancelledPlot(hasFired: false)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.ShouldRefundCancelledPlot(hasFired: true)).IsFalse();
    }

    [Test]
    public async Task Stop_RefundsOnlyBeforeCastOrPlotCommits()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnStop(
            plotHasFired: false, castCommitted: false)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnStop(
            plotHasFired: false, castCommitted: true)).IsFalse();
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnStop(
            plotHasFired: true, castCommitted: false)).IsFalse();
        await Assert.That(SkillCooldownCancelRules.ShouldRefundOnStop(
            plotHasFired: true, castCommitted: true)).IsFalse();
    }

    [Test]
    public async Task PlotEnd_RefundsWhenTheGraphNeverCommitted()
    {
        await Assert.That(SkillCooldownCancelRules.ShouldRefundPlotEnd(hasFired: false)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.ShouldRefundPlotEnd(hasFired: true)).IsFalse();
    }

    [Test]
    [Arguments(SpecialType.GlobalCooldown, false)]
    [Arguments(SpecialType.AutoAttack, false)]
    [Arguments(SpecialType.CancelStealth, false)]
    [Arguments(SpecialType.CancelOngoingBuff, false)]
    [Arguments(SpecialType.SetVariable, false)]
    [Arguments(SpecialType.Cooldown, true)]
    [Arguments(SpecialType.Anim, true)]
    public async Task SetupOnlySpecials_DoNotCommitThePlot(SpecialType type, bool counts)
    {
        await Assert.That(SkillCooldownCancelRules.IsSetupOnlySpecial(type)).IsEqualTo(!counts);
        await Assert.That(SkillCooldownCancelRules.CountsTowardPlotCommit("SpecialEffect", type)).IsEqualTo(counts);
    }

    [Test]
    public async Task NonSpecialPlotEffects_AlwaysCommit()
    {
        await Assert.That(SkillCooldownCancelRules.CountsTowardPlotCommit("DamageEffect", null)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.CountsTowardPlotCommit("BuffEffect", null)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.CountsTowardPlotCommit("SkillController", null)).IsTrue();
    }

    [Test]
    public async Task PlotCommitted_CastBarOpenerStaysRefundable()
    {
        await Assert.That(SkillCooldownCancelRules.PlotCommitted(
            appliedEffects: true, openedCastBar: true, fireMoment: false, enteredChannel: false)).IsFalse();
    }

    [Test]
    public async Task PlotCommitted_FireMomentAndAppliedEffectsCommit()
    {
        await Assert.That(SkillCooldownCancelRules.PlotCommitted(
            appliedEffects: false, openedCastBar: false, fireMoment: true, enteredChannel: false)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.PlotCommitted(
            appliedEffects: true, openedCastBar: false, fireMoment: false, enteredChannel: false)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.PlotCommitted(
            appliedEffects: false, openedCastBar: false, fireMoment: false, enteredChannel: true)).IsTrue();
    }

    [Test]
    public async Task StillOwnsCooldown_OnlyTheCurrentToken()
    {
        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(armedToken: 4, currentToken: 4)).IsTrue();
        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(armedToken: 4, currentToken: 5)).IsFalse();
        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(armedToken: 0, currentToken: 0)).IsFalse();
    }

    [Test]
    public async Task OwnedCooldown_ALaterArmIsNotClearedByTheEarlierToken()
    {
        var cooldowns = new UnitCooldowns();
        var first = cooldowns.AddCooldown(skillId: 10, duration: 5000, tagIds: [3]);
        var second = cooldowns.AddCooldown(skillId: 10, duration: 8000, tagIds: [3]);

        await Assert.That(cooldowns.TryRemoveOwnedCooldown(10, first, [3])).IsFalse();
        await Assert.That(cooldowns.CheckCooldown(10)).IsTrue();
        await Assert.That(cooldowns.CheckTagCooldown([3])).IsTrue();

        await Assert.That(cooldowns.TryRemoveOwnedCooldown(10, second, [3])).IsTrue();
        await Assert.That(cooldowns.CheckCooldown(10)).IsFalse();
        await Assert.That(cooldowns.CheckTagCooldown([3])).IsFalse();
    }

    [Test]
    public async Task ChargeEpoch_ALaterSpendDoesNotMatchTheEarlierCast()
    {
        var cooldowns = new UnitCooldowns();
        cooldowns.ConsumeCharge(skillId: 10, maxCharges: 3, rechargeTime: 1000);
        var first = cooldowns.ChargeEpoch(10);
        cooldowns.ConsumeCharge(skillId: 10, maxCharges: 3, rechargeTime: 1000);
        var second = cooldowns.ChargeEpoch(10);

        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(first, cooldowns.ChargeEpoch(10))).IsFalse();
        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(second, cooldowns.ChargeEpoch(10))).IsTrue();
    }

    [Test]
    public async Task GlobalCooldownClaim_ALaterClaimReplacesTheEarlierToken()
    {
        var unit = new Unit();
        var first = unit.ClaimGlobalCooldown();
        var second = unit.ClaimGlobalCooldown();

        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(first, unit.GlobalCooldownToken)).IsFalse();
        await Assert.That(SkillCooldownCancelRules.StillOwnsCooldown(second, unit.GlobalCooldownToken)).IsTrue();
    }
}
