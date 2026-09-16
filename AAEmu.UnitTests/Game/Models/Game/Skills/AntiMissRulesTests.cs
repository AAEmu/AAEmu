using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The anti-miss multipliers <c>melee_anti_miss_mul</c> / <c>ranged_anti_miss_mul</c> /
/// <c>spell_anti_miss_mul</c> (78/83/88) as a factor on the attacker's accuracy.
/// </summary>
public class AntiMissRulesTests
{
    [Test]
    public async Task Multiplier_WithoutARow_IsExactlyOne()
    {
        // The exact-equality pin for RollCombatDice: a caster with none of these rows keeps rolling against
        // the plain MeleeAccuracy / RangedAccuracy / SpellAccuracy it always did.
        await Assert.That(AntiMissRules.Multiplier(0)).IsEqualTo(1f);
        await Assert.That(AntiMissRules.HitChance(100f, AntiMissRules.Multiplier(0))).IsEqualTo(100f);
    }

    [Test]
    public async Task Multiplier_ReadsTheStoredValueAsTenthsOfAPercent()
    {
        // Buff 26158 (실명) stores -700 for "물리공격 성공률이 70% 감소됩니다", buff 388 -250 for "25% 감소",
        // buff 2466 -70 for "마법 성공률이 7% 감소", buff 23000 -10 for "공격 성공률 1% 감소".
        await Assert.That(AntiMissRules.Multiplier(-700)).IsEqualTo(0.3f);
        await Assert.That(AntiMissRules.Multiplier(-250)).IsEqualTo(0.75f);
        await Assert.That(AntiMissRules.Multiplier(-70)).IsEqualTo(0.93f);
        await Assert.That(AntiMissRules.Multiplier(-10)).IsEqualTo(0.99f);
        await Assert.That(AntiMissRules.Multiplier(1000)).IsEqualTo(2f);
    }

    [Test]
    public async Task Multiplier_StopsAtZero_SoAnOverdrawnDebuffOnlyEverMisses()
    {
        // Buff 807 (주문 방해) stores -3000 on the spell id: a negative multiplier is not a hit chance, so
        // the row is clipped to 0 and every roll against it misses.
        await Assert.That(AntiMissRules.Multiplier(-1000)).IsEqualTo(0f);
        await Assert.That(AntiMissRules.Multiplier(-3000)).IsEqualTo(0f);
        await Assert.That(AntiMissRules.Multiplier(long.MinValue)).IsEqualTo(0f);
        await Assert.That(AntiMissRules.HitChance(100f, AntiMissRules.Multiplier(-3000))).IsEqualTo(0f);
    }

    [Test]
    public async Task Buff807_IsFlooredToASilence_NotRescaledToTheThirtyPercentItReads()
    {
        // Buff 807 (주문 방해) stores -3000 on spell_anti_miss_mul while its own description and its sibling
        // casting_time_mul row (71, 300) both say 30%, and it is the only blind row past the -1000 edge
        // (26158 -700, 388 -250, 2466 -70, 2214 -60, 23000 -10 and 15040 -100 all agree with per-mille).
        // The recorded decision is the floor, so the row is a silence rather than a 30% reduction: with the
        // floor the victim's 100 spell accuracy becomes 0, and the debuff now stops spells landing where
        // attribute 88 used to be read by nothing at all.
        await Assert.That(AntiMissRules.Multiplier(-3000)).IsEqualTo(0f);
        await Assert.That(AntiMissRules.HitChance(100f, AntiMissRules.Multiplier(-3000))).IsEqualTo(0f);

        // The rejected alternative, kept asserted so the choice cannot drift into it silently: reading the
        // row as the authored 30% divided by ten is Multiplier(-300), which would leave 70% of spells
        // landing.
        await Assert.That(AntiMissRules.Multiplier(-300)).IsEqualTo(0.7f);
        await Assert.That(AntiMissRules.Multiplier(-3000)).IsNotEqualTo(AntiMissRules.Multiplier(-300));
    }

    [Test]
    public async Task HitChance_ScalesTheAccuracyStat_NotTheRoll()
    {
        // 100 accuracy is the "always hits" baseline every unit ships with, which is why a -700 row and a
        // flat "70 percentage points" reading agree; the two only diverge once accuracy itself has moved.
        await Assert.That(AntiMissRules.HitChance(100f, AntiMissRules.Multiplier(-700))).IsEqualTo(30f).Within(0.001f);
        await Assert.That(AntiMissRules.HitChance(50f, AntiMissRules.Multiplier(-700))).IsEqualTo(15f).Within(0.001f);
        await Assert.That(AntiMissRules.HitChance(0f, AntiMissRules.Multiplier(1000))).IsEqualTo(0f);
    }
}
