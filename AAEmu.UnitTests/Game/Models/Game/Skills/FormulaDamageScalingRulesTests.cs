using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The two distance factors, evaluated against the shipped <c>formulas</c> rows themselves. The expressions
/// quoted here are <c>formulas</c> 11 and 12 verbatim from the 10.0.2.13 content DB (read-only), so a change
/// to either row's shape shows up as a failing test rather than as silently different damage.
/// </summary>
public class FormulaDamageScalingRulesTests
{
    /// <summary>formulas 11, damage_multiplier_by_height.</summary>
    private const string HeightFormulaText = " if_negative(range - 1, 1, 1.05 + (min(range, 100)/100) )";

    /// <summary>formulas 12, damage_multiplier_by_range.</summary>
    private const string RangeFormulaText =
        "if_negative(range-optimum_range*2,if_negative(range-optimum_range," +
        "((range_damage_multiplier-1)/(optimum_range*optimum_range))*range*range+1," +
        "((range_damage_multiplier-1)/((optimum_range-(optimum_range*2))*(optimum_range-(optimum_range*2))))" +
        "*(range-2*optimum_range)*(range-2*optimum_range)+1),1)";

    private static Formula HeightFormula() => new(HeightFormulaText);

    private static Formula RangeFormula() => new(RangeFormulaText);

    // ---------------------------------------------------------------------------------------------------
    // height
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task HeightFactor_WithoutTheRow_IsExactlyOne()
    {
        // The 417 rows that turn adjust_damage_by_height off, and any build whose formulas table has no
        // row 11: the factor is exactly 1.0f, so the composed range is untouched to the bit.
        foreach (var height in new[] { -50f, 0f, 1f, 25f, 100f, 400f })
        {
            await Assert.That(FormulaDamageScalingRules.HeightFactor(null, height))
                .IsEqualTo(FormulaDamageScalingRules.NeutralFactor);
        }

        // An expression the engine cannot evaluate for the parameters it is handed answers the same way.
        await Assert.That(FormulaDamageScalingRules.HeightFactor(new Formula("missing_variable * 2"), 5f))
            .IsEqualTo(FormulaDamageScalingRules.NeutralFactor);
    }

    [Test]
    public async Task HeightFactor_MatchesTheShippedRow()
    {
        // "if_negative(range - 1, 1, 1.05 + (min(range, 100)/100) )": neutral up to one metre, then +1 % per
        // metre of height advantage, capped at 100 m.
        var formula = HeightFormula();

        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, -10f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 0f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 0.99f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 1f)).IsEqualTo(1.06f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 5f)).IsEqualTo(1.10f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 50f)).IsEqualTo(1.55f);
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 100f)).IsEqualTo(2.05f);
        // The cap: a 400 m glide attack is the same as a 100 m one.
        await Assert.That(FormulaDamageScalingRules.HeightFactor(formula, 400f)).IsEqualTo(2.05f);
    }

    [Test]
    public async Task HeightAdvantage_IsPositiveOnlyWhenTheCasterIsAbove()
    {
        var caster = UnitAt(0f, 0f, 0f);
        var victim = UnitAt(0f, 0f, 7.5f);

        await Assert.That(FormulaDamageScalingRules.HeightAdvantage(caster, victim)).IsEqualTo(-7.5f);
        await Assert.That(FormulaDamageScalingRules.HeightAdvantage(victim, caster)).IsEqualTo(7.5f);
    }

    // ---------------------------------------------------------------------------------------------------
    // range
    // ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task RangeFactor_WithoutTheRow_IsExactlyOne()
    {
        // The 10,878 rows that do not set adjust_damage_by_range, plus an unloaded table.
        foreach (var range in new[] { 0f, 15f, 30f, 60f, 300f })
        {
            await Assert.That(FormulaDamageScalingRules.RangeFactor(null, range, 30f, 1.3f))
                .IsEqualTo(FormulaDamageScalingRules.NeutralFactor);
        }

        await Assert.That(FormulaDamageScalingRules.RangeFactor(new Formula("not_a_variable + 1"), 30f, 30f, 1.3f))
            .IsEqualTo(FormulaDamageScalingRules.NeutralFactor);
    }

    [Test]
    public async Task RangeFactor_MatchesTheShippedRow()
    {
        // The 99 rows that ship optimum_range 30 with range_damage_multipier 1.3 (폭탄 사격 and friends):
        // 1 at the caster's feet, 1.3 at 30 m, and back to 1 at 60 m and beyond.
        var formula = RangeFormula();

        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 0f, 30f, 1.3f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 30f, 30f, 1.3f)).IsEqualTo(1.3f);
        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 60f, 30f, 1.3f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 200f, 30f, 1.3f)).IsEqualTo(1f);

        // Halfway to the sweet spot is halfway up the curve: (1.3-1)/30² * 15² + 1 = 1.075. The engine works
        // in doubles, so this one lands a float ulp away from the literal.
        await Assert.That(MathF.Abs(FormulaDamageScalingRules.RangeFactor(formula, 15f, 30f, 1.3f) - 1.075f))
            .IsLessThan(1e-5f);
    }

    [Test]
    public async Task RangeFactor_ReadsTheRowsOwnOptimumAndMultiplier()
    {
        // 25 m with 2.0 (7 shipped rows) must give 2.0 at 25 m, and the 10.0 outlier must give 10.0.
        var formula = RangeFormula();

        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 25f, 25f, 2f)).IsEqualTo(2f);
        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 50f, 25f, 2f)).IsEqualTo(1f);
        await Assert.That(FormulaDamageScalingRules.RangeFactor(formula, 10f, 10f, 10f)).IsEqualTo(10f);
    }

    [Test]
    public async Task HorizontalRange_IgnoresTheHeightDifference()
    {
        var caster = UnitAt(3f, 4f, 0f);
        var victim = UnitAt(0f, 0f, 120f);

        await Assert.That(FormulaDamageScalingRules.HorizontalRange(caster, victim)).IsEqualTo(5f);
    }

    private static Unit UnitAt(float x, float y = 0f, float z = 0f)
    {
        var unit = new Unit();
        unit.Transform.Local.SetPosition(x, y, z);
        return unit;
    }
}
