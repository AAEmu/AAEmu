using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>incoming_siege_damage_mul</c> (149) and <c>siege_damage_mul</c> (261) as factors.
/// </summary>
public class SiegeDamageRulesTests
{
    [Test]
    public async Task Factor_WithoutARow_IsExactlyOne()
    {
        // The exact-equality pin: DamageEffect multiplies siege damage by this and by the victim's
        // IncomingDamageMul, so 1.0f is what keeps every number where it was.
        await Assert.That(SiegeDamageRules.Factor(0)).IsEqualTo(1f);
    }

    [Test]
    public async Task Factor_ReadsTheStoredValueAsTenthsOfAPercent()
    {
        // Buff 21088 stores -900 for "받는 근접/공성 피해율이 90% 감소", buff 14993 stores +400 for
        // "받는 공성 피해율을 40% 증가시킵니다", buff 21902 stores -165 for "16.5% 감소".
        await Assert.That(SiegeDamageRules.Factor(-900)).IsEqualTo(0.1f);
        await Assert.That(SiegeDamageRules.Factor(-500)).IsEqualTo(0.5f);
        await Assert.That(SiegeDamageRules.Factor(-165)).IsEqualTo(0.835f);
        await Assert.That(SiegeDamageRules.Factor(400)).IsEqualTo(1.4f);
        await Assert.That(SiegeDamageRules.Factor(700)).IsEqualTo(1.7f);
    }

    [Test]
    public async Task Factor_StopsAtZero_InsteadOfTurningImmunityIntoAHeal()
    {
        // Buff 14857 stores -1000 (immunity) and buffs 24950 / 24837 store -21000 / -7000, which a plain
        // (value + 1000) / 1000 would turn negative.
        await Assert.That(SiegeDamageRules.Factor(-1000)).IsEqualTo(0f);
        await Assert.That(SiegeDamageRules.Factor(-7000)).IsEqualTo(0f);
        await Assert.That(SiegeDamageRules.Factor(-21000)).IsEqualTo(0f);
        await Assert.That(SiegeDamageRules.Factor(long.MinValue)).IsEqualTo(0f);
    }
}
