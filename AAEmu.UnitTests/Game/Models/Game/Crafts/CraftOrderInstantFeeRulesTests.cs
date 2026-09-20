using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Instant additional gold: evaluate the two content formulas, refuse when either is missing or
/// comes back negative. The numbers in these tests are the formula inputs, not shipped ids.
/// </summary>
public class CraftOrderInstantFeeRulesTests
{
    [Test]
    public async Task InstantPcActability_IsTheProductGrade()
    {
        await Assert.That(CraftOrderInstantFeeRules.InstantPcActability(0)).IsEqualTo(0);
        await Assert.That(CraftOrderInstantFeeRules.InstantPcActability(4)).IsEqualTo(4);
    }

    [Test]
    public async Task AdditionalFee_RunsMinThenAdditionalAndRoundsHalfUp()
    {
        var min = new Formula("craft_cost + pc_actability");
        var extra = new Formula("min_craft_order_fee + craft_count");

        await Assert.That(CraftOrderInstantFeeRules.TryAdditionalFee(
            min, extra, craftCost: 100, consumeLp: 0, requireActability: 0, pcActability: 2, craftCount: 3,
            out var fee)).IsTrue();
        await Assert.That(fee).IsEqualTo(105);
    }

    [Test]
    public async Task AdditionalFee_RefusesAMissingFormula()
    {
        await Assert.That(CraftOrderInstantFeeRules.TryAdditionalFee(
            null, new Formula("1"), 1, 0, 0, 0, 1, out _)).IsFalse();
        await Assert.That(CraftOrderInstantFeeRules.TryAdditionalFee(
            new Formula("1"), null, 1, 0, 0, 0, 1, out _)).IsFalse();
    }

    [Test]
    public async Task RoundToCopper_RejectsNegatives()
    {
        await Assert.That(CraftOrderInstantFeeRules.RoundToCopper(1.4)).IsEqualTo(1);
        await Assert.That(CraftOrderInstantFeeRules.RoundToCopper(1.5)).IsEqualTo(2);
        await Assert.That(CraftOrderInstantFeeRules.RoundToCopper(-0.1)).IsEqualTo(-1);
    }
}
