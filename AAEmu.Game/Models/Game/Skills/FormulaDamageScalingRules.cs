using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The two distance factors a damage effect scales by, evaluated from the content's own formula rows rather
/// than from a constant in the server: <c>damage_multiplier_by_height</c> (<c>formulas</c> 11) and
/// <c>damage_multiplier_by_range</c> (12).
/// </summary>
/// <remarks>
/// Both take the variables the rows name — <c>range</c> for 11, and <c>range</c>, <c>optimum_range</c> and
/// <c>range_damage_multiplier</c> for 12 (the column is spelled <c>range_damage_multipier</c> in
/// <c>damage_effects</c>; the formula text is the authority on the variable name) — and both answer exactly
/// 1.0f when the row or the whole table is absent, so a caller that does not have the content keeps the
/// numbers it had.
/// </remarks>
public static class FormulaDamageScalingRules
{
    /// <summary>The factor of a scale that is not in play: exactly 1.0f.</summary>
    public const float NeutralFactor = 1f;

    /// <summary>
    /// How far above the victim the caster is, in metres: positive is a height advantage, negative is a
    /// victim on higher ground.
    /// </summary>
    /// <remarks>
    /// formulas 11 is <c>if_negative(range - 1, 1, 1.05 + (min(range, 100)/100) )</c>, so a negative or
    /// sub-metre input is exactly 1: the row only ever rewards height, it never punishes being below. That is
    /// why the signed difference is passed here rather than its magnitude, and why the 417 rows that turn
    /// <c>adjust_damage_by_height</c> off are the ones where height should not matter at all (자폭 비행,
    /// 감아올리기, 메테오 소환 and the other scripted hits).
    /// </remarks>
    public static float HeightAdvantage(Unit caster, Unit victim) =>
        caster.Transform.World.Position.Z - victim.Transform.World.Position.Z;

    /// <summary>
    /// How far the victim stands from the caster in the plane the two are fighting on, in metres. The
    /// vertical component is left to <see cref="HeightAdvantage"/> so one metre of height is not counted
    /// twice against the range curve.
    /// </summary>
    public static float HorizontalRange(Unit caster, Unit victim)
    {
        var dx = caster.Transform.World.Position.X - victim.Transform.World.Position.X;
        var dy = caster.Transform.World.Position.Y - victim.Transform.World.Position.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// formulas 11 (<c>damage_multiplier_by_height</c>) for one height advantage. An absent row, an
    /// unevaluatable one or an unloaded table is exactly <see cref="NeutralFactor"/>.
    /// </summary>
    public static float HeightFactor(Formula formula, float heightAdvantage)
    {
        if (formula == null)
            return NeutralFactor;

        var parameters = new Dictionary<string, double>(1, StringComparer.Ordinal)
        {
            ["range"] = heightAdvantage
        };

        return formula.TryEvaluate(parameters, out var value) ? (float)value : NeutralFactor;
    }

    /// <summary>
    /// formulas 12 (<c>damage_multiplier_by_range</c>) for one distance: 1 at the caster's feet, the authored
    /// <paramref name="rangeDamageMultiplier"/> at <paramref name="optimumRange"/>, and 1 again at twice that
    /// distance and beyond. An absent row, an unevaluatable one or an unloaded table is exactly
    /// <see cref="NeutralFactor"/>.
    /// </summary>
    public static float RangeFactor(Formula formula, float range, float optimumRange, float rangeDamageMultiplier)
    {
        if (formula == null)
            return NeutralFactor;

        var parameters = new Dictionary<string, double>(3, StringComparer.Ordinal)
        {
            ["range"] = range,
            ["optimum_range"] = optimumRange,
            ["range_damage_multiplier"] = rangeDamageMultiplier
        };

        return formula.TryEvaluate(parameters, out var value) ? (float)value : NeutralFactor;
    }

    /// <summary>
    /// The height factor for <paramref name="caster"/> against <paramref name="victim"/>, read straight from
    /// the loaded table.
    /// </summary>
    public static float HeightFactorFor(Unit caster, Unit victim) =>
        HeightFactor(FormulaManager.Instance.GetFormulaOrNull(FormulaKind.DamageMultiplierByHeight),
            HeightAdvantage(caster, victim));

    /// <summary>
    /// The range factor for <paramref name="caster"/> against <paramref name="victim"/>, read straight from
    /// the loaded table and the effect's own <c>optimum_range</c>/<c>range_damage_multipier</c> pair.
    /// </summary>
    public static float RangeFactorFor(Unit caster, Unit victim, float optimumRange, float rangeDamageMultiplier) =>
        RangeFactor(FormulaManager.Instance.GetFormulaOrNull(FormulaKind.DamageMultiplierByRange),
            HorizontalRange(caster, victim), optimumRange, rangeDamageMultiplier);
}
