using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillGcdRulesTests
{
    [Test]
    public async Task SharedGcd_UsesTheContinuousAttackSpeedRating()
    {
        await Assert.That(SkillGcdRules.AttackSpeedGcdMultiplier(177, globalCooldownMul: 100f))
            .IsEqualTo(1000d / 1177d);
        await Assert.That(SkillGcdRules.AttackSpeedGcdMultiplier(1000, globalCooldownMul: 50f))
            .IsEqualTo(0.5d);
    }

    [Test]
    public async Task SharedGcd_UsesGlobalCooldownFallbackWithoutAttackSpeedRating()
    {
        await Assert.That(SkillGcdRules.AttackSpeedGcdMultiplier(0, globalCooldownMul: 50f))
            .IsEqualTo(0.5d);
    }

    [Test]
    public async Task DefaultGcd_TakesPriorityOverCustomGcd()
    {
        // 10501 제압 carries custom_gcd 500 next to default_gcd and weapon_gcd_id 16.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(500, defaultGcd: true, weaponGcdSpeed: 1200, isNpc: false))
            .IsEqualTo(1200);
        await Assert.That(SkillGcdRules.ResolveSharedGcd(500, defaultGcd: true, weaponGcdSpeed: 1300, isNpc: false))
            .IsEqualTo(1300);
        await Assert.That(SkillGcdRules.ResolveSharedGcd(700, defaultGcd: true, weaponGcdSpeed: 0, isNpc: false))
            .IsEqualTo(1000);

        // With default_gcd unset, custom_gcd remains the base and scales with weapon speed.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(700, defaultGcd: false, weaponGcdSpeed: 1300, isNpc: false))
            .IsEqualTo(910);
    }

    [Test]
    public async Task WeaponGcd_AppliesWhenNoCustomGcdIsAuthored()
    {
        // 10135 희생의 창: custom_gcd 0, default_gcd 't', weapon_gcd_id 15 selects Mainhand.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(0, defaultGcd: true, weaponGcdSpeed: 1100, isNpc: false))
            .IsEqualTo(1100);
    }

    [Test]
    public async Task DefaultGcd_IsTheFallback_AndIsSplitByCasterKind()
    {
        await Assert.That(SkillGcdRules.ResolveSharedGcd(0, defaultGcd: true, weaponGcdSpeed: 0, isNpc: false))
            .IsEqualTo(1000);
        await Assert.That(SkillGcdRules.ResolveSharedGcd(0, defaultGcd: true, weaponGcdSpeed: 0, isNpc: true))
            .IsEqualTo(1500);
        // A skill with no authored GCD fields falls back to the player default.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(0, defaultGcd: false, weaponGcdSpeed: 0, isNpc: false))
            .IsEqualTo(1000);
    }

    [Test]
    public async Task TripleSlashShape_DaggerIsFasterThanNodachi()
    {
        // 18131: custom_gcd 700, default_gcd t, weapon_gcd_id 15 → equipped mainhand speed wins.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(700, defaultGcd: true, weaponGcdSpeed: 800, isNpc: false))
            .IsEqualTo(800);
        await Assert.That(SkillGcdRules.ResolveSharedGcd(700, defaultGcd: true, weaponGcdSpeed: 1100, isNpc: false))
            .IsEqualTo(1100);
    }
}
