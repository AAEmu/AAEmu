using AAEmu.Game.Models.Game.Skills;

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
}
