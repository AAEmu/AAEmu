using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The combat rows of the <c>formulas</c> table (70 rows in 10.0.2.13) that the hit pipeline used to spell
/// out as numbers. Each rule evaluates its row with the variables the row's expression names and returns
/// the same unit the call site already works in: a per-cent or a plain multiplier.
/// </summary>
/// <remarks>
/// <para>
/// Absence is neutral by construction. <c>formulas</c> is a live content table, so every rule keeps the
/// expression this server shipped before as its fallback and only leaves it when the row is there and
/// evaluates. A process with no table loaded — a unit test, or a content root missing the row — computes
/// exactly what it computed before, which is what the rules' tests pin with exact equality.
/// </para>
/// <para>
/// Every fallback is the expression this server shipped inline, kept verbatim as the fallback:
/// <c>battle_resist / (battle_resist + 8000)</c> for formula 23, the flat <c>3 / 1000</c> per rating point
/// the two rating reductions used (which is the level-50 value of <c>rating * 100 / facets</c>, facets
/// 3,286,000, and is where the literal came from) and <c>flexibility / 100</c> for formula 26.
/// </para>
/// </remarks>
public static class CombatFormulaRules
{
    /// <summary>
    /// The 8000 of formula 23 (<c>battle_resist / (battle_resist + 8000)</c>), kept as the fallback so the
    /// toughness reduction is bit-for-bit the expression that was inline before.
    /// </summary>
    public const double BattleResistConstant = 8000d;

    /// <summary>
    /// Per-cent-point reduction one point of flexibility applies to an incoming critical chance, at the
    /// level-50 facets value: <c>100 / 3286000 * 100</c> rounded to the 3/1000 the code carried. Spelled the
    /// way the call site had it so the fallback is bit-for-bit, not merely close.
    /// </summary>
    private const float FlexibilityPerThousand = 1000f;

    /// <summary>Per-cent reduction one point of flexibility applied to an incoming critical bonus.</summary>
    private const float FlexibilityBonusPerPoint = 100f;

    /// <summary>
    /// Formula 26's <c>flexibility / 8</c> is authored in the same scale as <c>melee_critical_bonus</c>
    /// (whose rows read 1500 and which the character getters divide by 10 to reach per-cent), so the bonus
    /// side divides by the same 10.
    /// </summary>
    private const float CriticalBonusScale = 10f;

    /// <summary>
    /// The share of a PvP hit that toughness removes: formula 23, <c>battle_resist / (battle_resist + 8000)</c>.
    /// </summary>
    public static float BattleResistReduction(int battleResist)
    {
        var formula = Row(FormulaKind.DamageReduceRadioByBattleResist);
        if (formula != null && formula.TryEvaluate(
                new Dictionary<string, double> { ["battle_resist"] = battleResist }, out var value))
            return (float)value;

        return battleResist / (float)(BattleResistConstant + battleResist);
    }

    /// <summary>
    /// The per-cent points a victim's flexibility removes from an incoming critical chance: formula 25,
    /// <c>flexibility * 100</c>, normalised by the victim's facets the way its own critical, dodge, parry
    /// and block getters normalise theirs.
    /// </summary>
    /// <param name="facets">The victim's facets; a unit that never computed them (0) falls back.</param>
    public static float FlexibilityCriticalChanceReduction(int flexibility, int facets)
    {
        var formula = Row(FormulaKind.FlexibilityRatio);
        if (formula != null && facets > 0 && formula.TryEvaluate(
                new Dictionary<string, double> { ["flexibility"] = flexibility }, out var value))
            return (float)(value / facets * 100d);

        return flexibility / FlexibilityPerThousand * 3f;
    }

    /// <summary>
    /// The per-cent reduction a victim's flexibility applies to an incoming critical bonus: formula 26,
    /// <c>flexibility / 8</c>, in the same scale as <c>melee_critical_bonus</c>.
    /// </summary>
    public static float FlexibilityCriticalBonusReduction(int flexibility)
    {
        var formula = Row(FormulaKind.FlexibilityBonus);
        if (formula != null && formula.TryEvaluate(
                new Dictionary<string, double> { ["flexibility"] = flexibility }, out var value))
            return (float)(value / CriticalBonusScale);

        return flexibility / FlexibilityBonusPerPoint;
    }

    /// <summary>
    /// The share of the victim's dodge, block and parry that an attacker's bulls eye removes: formula 24,
    /// <c>bulls_eye * 105</c>, over the attacker's facets.
    /// </summary>
    /// <remarks>
    /// This call site subtracts the result from a rate that is already in per-cent, in the same (fraction)
    /// unit it always used, so the rule keeps that unit rather than the per-cent points
    /// <see cref="FlexibilityCriticalChanceReduction"/> returns. The frozen literal
    /// <c>bulls_eye / 1000 * 3 / 100</c> is the level-50 value of <c>bulls_eye * 100 / facets</c>; the row's
    /// own <c>* 105</c> is 5% larger, and it now follows the attacker's facets instead of level 50's.
    /// </remarks>
    /// <param name="facets">The attacker's facets; 0 falls back to the flat per-point share.</param>
    public static float BullsEyeAvoidanceReduction(int bullsEye, int facets)
    {
        var formula = Row(FormulaKind.FacetsForBullsEye);
        if (formula != null && facets > 0 && formula.TryEvaluate(
                new Dictionary<string, double> { ["bulls_eye"] = bullsEye }, out var value))
            return (float)(value / facets);

        return bullsEye / 1000f * 3f / 100f;
    }

    /// <summary>
    /// The <c>formulas</c> row for a kind, or null when the table has no such row or is not loaded.
    /// </summary>
    private static Formula Row(FormulaKind kind)
    {
        var manager = FormulaManager.Instance;
        return manager is { Loaded: true } ? manager.GetFormula((uint)kind) : null;
    }
}
