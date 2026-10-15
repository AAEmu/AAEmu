using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCooldownDurationRulesTests
{
    [Test]
    public async Task Accept_ArmsWhenCooldownAuthored_EvenWithBypassGcd()
    {
        // bypassGcd is not an argument: skill CD arms for ItemProc / NpcEvents bypass casts.
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(unsupportedCast: false, cooldownTime: 15000))
            .IsTrue();
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(unsupportedCast: true, cooldownTime: 15000))
            .IsFalse();
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(unsupportedCast: false, cooldownTime: 0))
            .IsFalse();
    }

    [Test]
    public async Task ResolveDuration_PassesThroughWithoutGlobalCooldownMul()
    {
        await Assert.That(SkillCooldownDurationRules.ResolveDuration(10000)).IsEqualTo(10000u);
        await Assert.That(SkillCooldownDurationRules.ResolveDuration(0)).IsEqualTo(0u);
    }
}
