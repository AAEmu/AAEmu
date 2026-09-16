using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.reflection_chance</c>, <c>reflection_ratio</c>, <c>reflection_target_ratio</c> and the five
/// damage-type flags (77 rows).
/// </summary>
public class DamageReflectionRulesTests
{
    [Test]
    public async Task EffectiveChance_ClampsToTheRollRange()
    {
        await Assert.That(DamageReflectionRules.EffectiveChance(0)).IsEqualTo(0);
        await Assert.That(DamageReflectionRules.EffectiveChance(47)).IsEqualTo(47);   // 177 마법 반사 (1레벨)
        await Assert.That(DamageReflectionRules.EffectiveChance(100)).IsEqualTo(100); // 386 가시방패
        await Assert.That(DamageReflectionRules.EffectiveChance(-10)).IsEqualTo(0);
        await Assert.That(DamageReflectionRules.EffectiveChance(5_000)).IsEqualTo(100);
    }

    [Test]
    public async Task Rolls_SucceedsBelowTheChance_AndFailsAtIt()
    {
        await Assert.That(DamageReflectionRules.Rolls(47, 46)).IsTrue();
        await Assert.That(DamageReflectionRules.Rolls(47, 47)).IsFalse();
        await Assert.That(DamageReflectionRules.Rolls(47, 99)).IsFalse();

        // Chance 0 is the authored default on 30,577 rows and has to miss every roll.
        for (var roll = 0; roll < DamageReflectionRules.ChanceDenominator; roll++)
            await Assert.That(DamageReflectionRules.Rolls(0, roll)).IsFalse();

        // Chance 100 (75 rows) has to hit every roll.
        for (var roll = 0; roll < DamageReflectionRules.ChanceDenominator; roll++)
            await Assert.That(DamageReflectionRules.Rolls(100, roll)).IsTrue();
    }

    [Test]
    public async Task AppliesTo_MapsTheFiveFlagsOntoTheDamageTypes()
    {
        // 167 방탄강기 is melee only; 177 마법 반사 is spell only; 20327 벌의 독침 is siege only;
        // 4408 자기장 보호막 is ranged only. reflection_heal is set on every spell row and never alone.
        await Assert.That(DamageReflectionRules.AppliesTo(true, false, false, false, false, DamageType.Melee)).IsTrue();
        await Assert.That(DamageReflectionRules.AppliesTo(true, false, false, false, false, DamageType.Magic)).IsFalse();

        await Assert.That(DamageReflectionRules.AppliesTo(false, true, false, false, true, DamageType.Magic)).IsTrue();
        await Assert.That(DamageReflectionRules.AppliesTo(false, true, false, false, true, DamageType.Heal)).IsTrue();
        await Assert.That(DamageReflectionRules.AppliesTo(false, true, false, false, true, DamageType.Melee)).IsFalse();

        await Assert.That(DamageReflectionRules.AppliesTo(false, false, true, false, false, DamageType.Siege)).IsTrue();
        await Assert.That(DamageReflectionRules.AppliesTo(false, false, false, true, false, DamageType.Ranged)).IsTrue();
    }

    [Test]
    public async Task AppliesTo_WithNoFlagSet_ReflectsNothing()
    {
        // Every one of the 77 rows with a chance sets at least one flag, so this combination is the
        // authored default and must stay inert whatever the hit is.
        foreach (var damageType in Enum.GetValues<DamageType>())
            await Assert.That(DamageReflectionRules.AppliesTo(false, false, false, false, false, damageType)).IsFalse();
    }

    [Test]
    public async Task ReflectedDamage_IsTheTargetRatioOfTheHit()
    {
        // 386 가시방패 and 2049 빈틈있는 가시방패 are 50: "받는 근접 피해율의 50%를 반사합니다."
        await Assert.That(DamageReflectionRules.ReflectedDamage(1_000, 50)).IsEqualTo(500);
        // 5646 가시 갑옷 is 6.
        await Assert.That(DamageReflectionRules.ReflectedDamage(1_000, 6)).IsEqualTo(60);
        // 26818 복수 is 200: "공격 피해의 200%를 반사".
        await Assert.That(DamageReflectionRules.ReflectedDamage(1_000, 200)).IsEqualTo(2_000);
        // 32522 냥냥냥냥! is 400.
        await Assert.That(DamageReflectionRules.ReflectedDamage(1_000, 400)).IsEqualTo(4_000);
        // 27581 빗나가는 연막 is 200 and 21417 보호의 깃털 is 70.
        await Assert.That(DamageReflectionRules.ReflectedDamage(120, 70)).IsEqualTo(84);

        // Nothing reflects for free.
        await Assert.That(DamageReflectionRules.ReflectedDamage(0, 100)).IsEqualTo(0);
        await Assert.That(DamageReflectionRules.ReflectedDamage(500, 0)).IsEqualTo(0);
        await Assert.That(DamageReflectionRules.ReflectedDamage(500, -50)).IsEqualTo(0);
    }

    [Test]
    public async Task DefenderDamage_AtTheDefaultHundred_IsTheNumberItself()
    {
        // 100 is what 30,639 rows store and what every "반사 시 자신이 입는 피해는 감소되지 않습니다"
        // row means. It must pass the value through exactly, not through a float round trip.
        for (var damage = 0; damage <= 2_000; damage++)
            await Assert.That(DamageReflectionRules.DefenderDamage(damage, 100)).IsEqualTo(damage);

        await Assert.That(DamageReflectionRules.DefenderDamage(int.MaxValue, 100)).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task DefenderDamage_IsTheRatioOfTheHit()
    {
        // 4366 자기장 보호막: "받는 모든 피해율의 100%를 반사합니다. 반사 시 자신은 10%의 피해를 입습니다."
        await Assert.That(DamageReflectionRules.DefenderDamage(1_000, 10)).IsEqualTo(100);
        // 26818 복수: "반사 시 자신은 50%의 피해를 입습니다."
        await Assert.That(DamageReflectionRules.DefenderDamage(1_000, 50)).IsEqualTo(500);
        // 21375 활력 방패: 불꽃 (5단계): "반사 시 자신은 75%의 마법 피해를 입습니다."
        await Assert.That(DamageReflectionRules.DefenderDamage(400, 75)).IsEqualTo(300);
        // 27581 빗나가는 연막: "받는 피해를 극도로 감소시키며".
        await Assert.That(DamageReflectionRules.DefenderDamage(1_000, 1)).IsEqualTo(10);

        await Assert.That(DamageReflectionRules.DefenderDamage(1_000, 0)).IsEqualTo(0);
        await Assert.That(DamageReflectionRules.DefenderDamage(0, 100)).IsEqualTo(0);
    }
}
