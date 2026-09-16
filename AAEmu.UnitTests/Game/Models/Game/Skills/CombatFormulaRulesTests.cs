using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <see cref="CombatFormulaRules"/> against the shipped <c>formulas</c> rows, with the real expression text
/// so a variable name the row does not carry fails here instead of silently falling back.
/// </summary>
// The formulas live in a singleton; a parallel test installing its own FormulaManager would swap it
// mid-assertion.
[NotInParallel]
public class CombatFormulaRulesTests
{
    private const string BattleResistRow = "battle_resist / (battle_resist + 8000)";
    private const string BullsEyeRow = "bulls_eye * 105";
    private const string FlexibilityRatioRow = "flexibility * 100";
    private const string FlexibilityBonusRow = "flexibility / 8";

    /// <summary>Installs a manager carrying exactly these rows; dispose to restore the previous one.</summary>
    private static SingletonScope<FormulaManager> WithRows(params (FormulaKind Kind, string Text)[] rows)
    {
        var manager = new FormulaManager();
        var table = new Dictionary<uint, Formula>();
        foreach (var (kind, text) in rows)
        {
            var formula = new Formula { Id = (uint)kind, TextFormula = text };
            formula.Prepare();
            table[(uint)kind] = formula;
        }

        SetField(manager, "_formulas", table);
        return new SingletonScope<FormulaManager>(manager);
    }

    /// <summary>The value the row itself produces, evaluated straight from its text.</summary>
    private static double EvaluateRow(string text, Dictionary<string, double> parameters)
    {
        var formula = new Formula { Id = 0, TextFormula = text };
        formula.Prepare();
        return formula.Evaluate(parameters);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"No field {name} on {target.GetType().Name}");
    }

    [Test]
    public async Task BattleResistReduction_EvaluatesTheRow()
    {
        using var scope = WithRows((FormulaKind.DamageReduceRadioByBattleResist, BattleResistRow));
        var parameters = new Dictionary<string, double> { ["battle_resist"] = 8000d };

        var fromRow = EvaluateRow(BattleResistRow, parameters);

        await Assert.That((float)fromRow).IsEqualTo(0.5f);
        await Assert.That(CombatFormulaRules.BattleResistReduction(8000)).IsEqualTo((float)fromRow);
        // Same curve as the inline expression it replaces, at a value the inline one could not reach
        // differently: 12000 / 20000.
        await Assert.That(CombatFormulaRules.BattleResistReduction(12000)).IsEqualTo(0.6f);
    }

    [Test]
    public async Task BattleResistReduction_WithoutTheRow_IsExactlyTheInlineExpression()
    {
        using var scope = WithRows();

        foreach (var battleResist in new[] { 0, 1, 8000, 12000, 30000, 100000 })
        {
            var inline = battleResist / (8000f + battleResist);
            await Assert.That(CombatFormulaRules.BattleResistReduction(battleResist)).IsEqualTo(inline);
        }
    }

    [Test]
    public async Task FlexibilityCriticalChanceReduction_EvaluatesTheRowOverFacets()
    {
        using var scope = WithRows((FormulaKind.FlexibilityRatio, FlexibilityRatioRow));
        const int facets = 3286000;
        const int flexibility = 560;
        var parameters = new Dictionary<string, double> { ["flexibility"] = flexibility };

        var fromRow = EvaluateRow(FlexibilityRatioRow, parameters);

        await Assert.That((float)fromRow).IsEqualTo(56000f);
        // 56000 / 3286000 * 100 = 1.704 per-cent points; the frozen 3/1000 per point gave 1.68.
        await Assert.That(CombatFormulaRules.FlexibilityCriticalChanceReduction(flexibility, facets))
            .IsEqualTo((float)(fromRow / facets * 100d));
        await Assert.That(CombatFormulaRules.FlexibilityCriticalChanceReduction(flexibility, facets))
            .IsGreaterThan(flexibility / 1000f * 3f);
    }

    [Test]
    public async Task FlexibilityCriticalChanceReduction_WithoutTheRowOrFacets_IsExactlyTheInlineExpression()
    {
        using var scope = WithRows((FormulaKind.FlexibilityRatio, FlexibilityRatioRow));

        foreach (var flexibility in new[] { 0, 150, 560, 10000 })
        {
            // No facets (an NPC before this batch computed them, a hull) falls back.
            await Assert.That(CombatFormulaRules.FlexibilityCriticalChanceReduction(flexibility, 0))
                .IsEqualTo(flexibility / 1000f * 3f);
        }

        using var noRows = WithRows();
        await Assert.That(CombatFormulaRules.FlexibilityCriticalChanceReduction(560, 3286000))
            .IsEqualTo(560 / 1000f * 3f);
    }

    [Test]
    public async Task FlexibilityCriticalBonusReduction_EvaluatesTheRow()
    {
        using var scope = WithRows((FormulaKind.FlexibilityBonus, FlexibilityBonusRow));
        var parameters = new Dictionary<string, double> { ["flexibility"] = 560d };

        var fromRow = EvaluateRow(FlexibilityBonusRow, parameters);

        await Assert.That((float)fromRow).IsEqualTo(70f);
        // 70 / 10 = 7 per-cent, against the frozen 560 / 100 = 5.6.
        await Assert.That(CombatFormulaRules.FlexibilityCriticalBonusReduction(560)).IsEqualTo((float)(fromRow / 10f));
    }

    [Test]
    public async Task FlexibilityCriticalBonusReduction_WithoutTheRow_IsExactlyTheInlineExpression()
    {
        using var scope = WithRows();

        foreach (var flexibility in new[] { 0, 120, 560, 10000 })
            await Assert.That(CombatFormulaRules.FlexibilityCriticalBonusReduction(flexibility))
                .IsEqualTo(flexibility / 100f);
    }

    [Test]
    public async Task BullsEyeAvoidanceReduction_EvaluatesTheRowOverFacets()
    {
        using var scope = WithRows((FormulaKind.FacetsForBullsEye, BullsEyeRow));
        const int facets = 3286000;
        const int bullsEye = 2145;
        var parameters = new Dictionary<string, double> { ["bulls_eye"] = bullsEye };

        var fromRow = EvaluateRow(BullsEyeRow, parameters);

        await Assert.That((float)fromRow).IsEqualTo(225225f);
        await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(bullsEye, facets))
            .IsEqualTo((float)(fromRow / facets));
        // The frozen per-point share is rating * 3 / 1000 / 100; the row's * 105 is 5% more than the * 100
        // that shape was built from.
        await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(bullsEye, facets))
            .IsGreaterThan(bullsEye / 1000f * 3f / 100f);
    }

    [Test]
    public async Task BullsEyeAvoidanceReduction_WithoutTheRowOrFacets_IsExactlyTheInlineExpression()
    {
        using var scope = WithRows((FormulaKind.FacetsForBullsEye, BullsEyeRow));

        foreach (var bullsEye in new[] { 0, 430, 2145, 2910 })
        {
            await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(bullsEye, 0))
                .IsEqualTo(bullsEye / 1000f * 3f / 100f);
        }

        using var noRows = WithRows();
        await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(2145, 3286000))
            .IsEqualTo(2145 / 1000f * 3f / 100f);
    }

    [Test]
    public async Task Rows_WithNoManagerLoaded_UseTheFallbacksInsteadOfThrowing()
    {
        // A process that never ran FormulaManager.Load (a unit test, a scratch tool) reads no table at all;
        // the rules must answer the inline expression rather than dereference a null dictionary.
        using var scope = new SingletonScope<FormulaManager>(new FormulaManager());

        await Assert.That(CombatFormulaRules.BattleResistReduction(8000)).IsEqualTo(8000 / (8000f + 8000));
        await Assert.That(CombatFormulaRules.FlexibilityCriticalChanceReduction(560, 3286000))
            .IsEqualTo(560 / 1000f * 3f);
        await Assert.That(CombatFormulaRules.FlexibilityCriticalBonusReduction(560)).IsEqualTo(5.6f);
        await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(2145, 3286000))
            .IsEqualTo(2145 / 1000f * 3f / 100f);
    }

    [Test]
    public async Task Rows_ThatDoNotCompile_UseTheFallbacks()
    {
        using var scope = WithRows(
            (FormulaKind.DamageReduceRadioByBattleResist, "battle_resist / (battle_resist + "),
            (FormulaKind.FacetsForBullsEye, "bulls_eye * "));

        await Assert.That(CombatFormulaRules.BattleResistReduction(8000)).IsEqualTo(8000 / (8000f + 8000));
        await Assert.That(CombatFormulaRules.BullsEyeAvoidanceReduction(2145, 3286000))
            .IsEqualTo(2145 / 1000f * 3f / 100f);
    }
}
