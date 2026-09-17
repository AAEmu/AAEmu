using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.mana_shield_ratio</c> (11 rows): damage paid for out of mana before health.
/// </summary>
public class ManaShieldRulesTests
{
    // 20433 마나실드 and 22945 활력 보호막, and buff 27915 마나실드(test) whose description is "100%".
    private const int FullShield = 10000;

    // 19960 하나된 마법의 힘 보호막.
    private const int OneAndAHalfShield = 15000;

    [Test]
    public async Task EffectiveRatio_IsHundredthsOfAPercent_FlooredAtZero()
    {
        await Assert.That(ManaShieldRules.EffectiveRatio(FullShield)).IsEqualTo(10000);
        await Assert.That(ManaShieldRules.EffectiveRatio(0)).IsEqualTo(0);
        await Assert.That(ManaShieldRules.EffectiveRatio(-500)).IsEqualTo(0);
    }

    [Test]
    public async Task ChargedToMana_AHundredPercentShield_TakesTheWholeHit()
    {
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, FullShield, 5_000)).IsEqualTo(1_000);
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, FullShield, 1_000)).IsEqualTo(1_000);
    }

    [Test]
    public async Task ChargedToMana_StopsAtTheManaTheUnitHas()
    {
        // 400 mana left, a 1,000 hit, a full shield: the rest falls through to health.
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, FullShield, 400)).IsEqualTo(400);
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, FullShield, 0)).IsEqualTo(0);
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, FullShield, -5)).IsEqualTo(0);
    }

    [Test]
    public async Task ChargedToMana_WithNoShield_TakesNothing()
    {
        // The 30,643 rows that leave mana_shield_ratio at 0 must not touch a single point of mana.
        for (var damage = 0; damage < 50; damage++)
            await Assert.That(ManaShieldRules.ChargedToMana(damage, 0, 1_000_000)).IsEqualTo(0);

        await Assert.That(ManaShieldRules.ChargedToMana(0, FullShield, 1_000)).IsEqualTo(0);
        await Assert.That(ManaShieldRules.ChargedToMana(-10, FullShield, 1_000)).IsEqualTo(0);
    }

    [Test]
    public async Task ChargedToMana_NeverCostsMoreManaThanTheHitIsWorth()
    {
        // 19960 is 15000 = 150%: mana pays one and a half times the hit, but a hit can still only cost
        // what it deals — the remainder is capped at the damage.
        await Assert.That(ManaShieldRules.ChargedToMana(1_000, OneAndAHalfShield, 100_000)).IsEqualTo(1_000);
        await Assert.That(ManaShieldRules.ChargedToMana(100, 5000, 100_000)).IsEqualTo(50);
    }

    [Test]
    public async Task StrongestRatio_PicksTheSameShieldEveryTime()
    {
        await Assert.That(ManaShieldRules.StrongestRatio([])).IsEqualTo(0);
        await Assert.That(ManaShieldRules.StrongestRatio([0, 0])).IsEqualTo(0);
        await Assert.That(ManaShieldRules.StrongestRatio([FullShield, OneAndAHalfShield])).IsEqualTo(OneAndAHalfShield);
        await Assert.That(ManaShieldRules.StrongestRatio([OneAndAHalfShield, FullShield])).IsEqualTo(OneAndAHalfShield);
        await Assert.That(ManaShieldRules.StrongestRatio([-1, 0])).IsEqualTo(0);
    }
}
