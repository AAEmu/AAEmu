using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillGcdRulesTests
{
    [Test]
    public async Task SpellGcd_UsesCastSpeedNotAttackSpeed()
    {
        await Assert.That(SkillGcdRules.SharedGcdMultiplier(false, globalCooldownMul: 50f, castTimeMul: 1f))
            .IsEqualTo(1f);
        await Assert.That(SkillGcdRules.SharedGcdMultiplier(false, globalCooldownMul: 100f, castTimeMul: 0.8f))
            .IsEqualTo(0.8f);
    }

    [Test]
    public async Task WeaponGcd_UsesAttackSpeed()
    {
        await Assert.That(SkillGcdRules.SharedGcdMultiplier(true, globalCooldownMul: 50f, castTimeMul: 1f))
            .IsEqualTo(0.5f);
    }

    [Test]
    public async Task CustomGcd_WinsOverWeaponClassAndDefault()
    {
        // 10501 제압 carries custom_gcd 500 next to weapon_gcd_id 16.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(500, defaultGcd: true, weaponGcdSpeed: 1200, isNpc: false))
            .IsEqualTo(500);
    }

    [Test]
    public async Task WeaponGcd_AppliesWhenNoCustomGcdIsAuthored()
    {
        // 10135 지옥의 창: custom_gcd 0, default_gcd 't', weapon_gcd_id 15 = 한손창 1100 ms.
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
        // A skill that declares nothing at all arms no shared cooldown.
        await Assert.That(SkillGcdRules.ResolveSharedGcd(0, defaultGcd: false, weaponGcdSpeed: 0, isNpc: false))
            .IsEqualTo(0);
    }
}
