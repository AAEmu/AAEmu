using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>ignore_shield_chance</c> (204) as the attacker's per-mille roll against the victim's absorption buffs.
/// </summary>
public class ShieldIgnoreRulesTests
{
    [Test]
    public async Task EffectiveChance_WithoutARow_IsZero()
    {
        await Assert.That(ShieldIgnoreRules.EffectiveChance(0)).IsEqualTo(0);
    }

    [Test]
    public async Task BypassesAbsorption_WithoutARow_NeverBypasses()
    {
        // Every roll in the range has to miss, because ChanceDenominator is the exclusive upper bound.
        for (var roll = 0; roll < ShieldIgnoreRules.ChanceDenominator; roll++)
            await Assert.That(ShieldIgnoreRules.BypassesAbsorption(0, roll)).IsFalse();
    }

    [Test]
    public async Task BypassesAbsorption_SucceedsBelowTheChance_AndFailsAtIt()
    {
        // Buff 16763 stores 10 for "방패 관통률 +1%": one roll in a hundred.
        await Assert.That(ShieldIgnoreRules.BypassesAbsorption(10, 9)).IsTrue();
        await Assert.That(ShieldIgnoreRules.BypassesAbsorption(10, 10)).IsFalse();
        await Assert.That(ShieldIgnoreRules.BypassesAbsorption(10, 999)).IsFalse();
    }

    [Test]
    public async Task EffectiveChance_ClampsNegativeAndOverflowingValues()
    {
        // unit_attribute_limits row 26 floors 204 at 0; buff 8227 stores 500 (50%) and the damage-effect rows
        // go up to 1000, which is a certain bypass rather than 10 rolls in 10.
        await Assert.That(ShieldIgnoreRules.EffectiveChance(-50)).IsEqualTo(0);
        await Assert.That(ShieldIgnoreRules.EffectiveChance(500)).IsEqualTo(500);
        await Assert.That(ShieldIgnoreRules.EffectiveChance(1000)).IsEqualTo(1000);
        await Assert.That(ShieldIgnoreRules.EffectiveChance(5000)).IsEqualTo(1000);

        for (var roll = 0; roll < ShieldIgnoreRules.ChanceDenominator; roll++)
            await Assert.That(ShieldIgnoreRules.BypassesAbsorption(1000, roll)).IsTrue();
    }

    [Test]
    public async Task EffectiveChance_IsPositiveForEveryShippedRow()
    {
        // The 71 rows of 204 are all >= 10 except for the two test/negative ones, so the ceiling of the
        // clamp is what a stacked value can reach, never a wraparound.
        await Assert.That(ShieldIgnoreRules.EffectiveChance(15)).IsEqualTo(15);
        await Assert.That(ShieldIgnoreRules.EffectiveChance(240)).IsEqualTo(240);
    }
}
