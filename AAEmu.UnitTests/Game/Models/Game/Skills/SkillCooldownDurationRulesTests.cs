using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCooldownDurationRulesTests
{
    [Test]
    public async Task Accept_ArmsWhenCooldownAuthored()
    {
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(false, false, 15000))
            .IsTrue();
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(true, false, 15000))
            .IsFalse();
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(false, true, 15000))
            .IsFalse();
        await Assert.That(SkillCooldownDurationRules.ShouldArmOnAccept(false, false, 0))
            .IsFalse();
    }

    [Test]
    public async Task ResolveDuration_ScalesByGlobalCooldownMulPercent()
    {
        // Character.GlobalCooldownMul is already the percent form (100 = unchanged).
        await Assert.That(SkillCooldownDurationRules.ResolveDuration(10000, globalCooldownMul: 100f))
            .IsEqualTo(10000u);
        await Assert.That(SkillCooldownDurationRules.ResolveDuration(10000, globalCooldownMul: 50f))
            .IsEqualTo(5000u);
        await Assert.That(SkillCooldownDurationRules.ResolveDuration(0, globalCooldownMul: 50f))
            .IsEqualTo(0u);
    }
}
