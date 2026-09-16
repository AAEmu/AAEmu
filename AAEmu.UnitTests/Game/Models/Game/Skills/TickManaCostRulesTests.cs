using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.tick_mana_cost</c> / <c>buffs.tick_level_mana_cost</c>. The acceptance line for B9 is that
/// Dash keeps behaving exactly as it did, so the first tests here pin its cadence: buffs.tick 200 over a
/// 200 ms manager tick is one payment per tick, which is what the hardcoded Dash drain did.
/// </summary>
public class TickManaCostRulesTests
{
    [Test]
    public async Task PaysPerTick_Dash_PaysOnEveryManagerTick()
    {
        // buffs 2675: tick 200, tick_mana_cost 0, tick_level_mana_cost 0.5.
        await Assert.That(TickManaCostRules.PaysPerTick(0, 0.5, 200)).IsTrue();
        await Assert.That(TickManaCostRules.TicksPerPayment(200)).IsEqualTo(1);
    }

    [Test]
    public async Task PaysPerTick_WithoutACost_IsNotRegistered()
    {
        // The 30 644 rows that carry neither cost column.
        await Assert.That(TickManaCostRules.PaysPerTick(0, 0, 1000)).IsFalse();
        await Assert.That(TickManaCostRules.PaysPerTick(0, 0, 0)).IsFalse();
    }

    [Test]
    public async Task PaysPerTick_CostWithoutATickInterval_IsRefused()
    {
        // buffs 4140 is the one row with tick_mana_cost 10000 and tick 0: nothing to pace a payment with,
        // so it keeps doing nothing rather than draining 10 000 mana on the manager's own clock.
        await Assert.That(TickManaCostRules.PaysPerTick(10000, 0, 0)).IsFalse();
    }

    [Test]
    [Arguments(200, 1)]
    [Arguments(500, 3)]
    [Arguments(1000, 5)]
    [Arguments(2000, 10)]
    public async Task TicksPerPayment_DividesTheBuffTickByTheManagerTick(int tick, int expected)
    {
        await Assert.That(TickManaCostRules.TicksPerPayment(tick)).IsEqualTo(expected);
    }

    [Test]
    public async Task TicksPerPayment_ShorterThanTheManagerTick_StillPaysOncePerTick()
    {
        // Never faster than the manager's own clock, which is also the floor Dash sits on.
        await Assert.That(TickManaCostRules.TicksPerPayment(50)).IsEqualTo(1);
        await Assert.That(TickManaCostRules.TicksPerPayment(0)).IsEqualTo(1);
    }

    [Test]
    public async Task Payment_DashAtLevel40_IsTheFormula13CurveTimesItsMultiplier()
    {
        // formulas 13 = (ab_level * 1.6 + 8) * 0.6; at level 40 that is 43.2, and Dash stores 0.5.
        var levelCurve = (40 * 1.6 + 8) * 0.6;

        await Assert.That(TickManaCostRules.Payment(0, 0.5, levelCurve)).IsEqualTo(21.6).Within(1e-9);
    }

    [Test]
    public async Task Payment_FlatCost_IsPaidAsStored()
    {
        // buffs 200: tick_mana_cost 39, no level column.
        await Assert.That(TickManaCostRules.Payment(39, 0, 43.2)).IsEqualTo(39.0).Within(1e-9);
        await Assert.That(TickManaCostRules.FlatCost(-5)).IsEqualTo(0);
    }
}
