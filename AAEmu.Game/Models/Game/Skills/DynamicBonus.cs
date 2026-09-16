using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// A bonus whose value depends on the elapsed lifetime of its source buff, or on a
/// <c>formula_funcs</c> expression over the owning unit.
/// Unlike a static <see cref="Bonus"/>, it is NOT snapshotted at apply time: its value is
/// recomputed every time the owning attribute is read (see Unit.CalculateWithBonuses).
///
/// The backing func template is pre-resolved once (at buff Start) so the hot evaluation path
/// performs no manager lookup and no string allocation.
/// </summary>
public class DynamicBonus
{
    public DynamicBonusTemplate Template { get; init; }
    public Buff SourceBuff { get; init; }

    /// <summary>Backing row for <c>func_type = 'LinearFunc'</c>; null for the other func types.</summary>
    public LinearFuncTemplate LinearFunc { get; init; }

    /// <summary>Backing row for <c>func_type = 'FormulaFunc'</c>; null for the other func types.</summary>
    public FormulaFuncTemplate FormulaFunc { get; init; }

    /// <summary>
    /// Computes the current value of this dynamic bonus.
    /// Returns false when the bonus cannot be evaluated (missing data, unsupported func type),
    /// in which case it must NOT be applied.
    /// </summary>
    public bool Evaluate(out double value)
    {
        value = 0d;

        if (SourceBuff == null || Template == null)
            return false;

        switch (Template.FuncType)
        {
            case DynamicBonusFuncRules.LinearFuncType:
            {
                if (LinearFunc == null)
                    return false;

                // ratio = elapsed / duration -> 0 at start (StartValue), 1 at expiry (EndValue).
                // A non-expiring buff (Duration <= 0) is treated as fully elapsed (EndValue),
                // matching the prototype behavior; no real LinearFunc dynamic_unit_modifier is
                // expected on a permanent buff.
                var duration = SourceBuff.Duration;
                var ratio = 1d;
                if (duration > 0)
                {
                    var elapsed = (DateTime.UtcNow - SourceBuff.StartTime).TotalMilliseconds;
                    ratio = Math.Clamp(elapsed / duration, 0d, 1d);
                }

                value = LinearFunc.GetValue(ratio);
                return true;
            }

            case DynamicBonusFuncRules.FormulaFuncType:
            {
                if (FormulaFunc == null)
                    return false;

                // A formula row may read the attribute it modifies (attr_N = the row's own
                // unit_attribute_id in 228 of the 233 shipped rows), and that read arrives here
                // again through Unit.CalculateWithBonuses. A nested evaluation contributes nothing,
                // so such a row adds the unit's value once — the one it had before this modifier —
                // instead of recursing.
                if (FormulaFuncRules.InFormulaEvaluation)
                    return false;

                using (FormulaFuncRules.BeginFormulaEvaluation())
                {
                    if (!FormulaFunc.TryEvaluate(SourceBuff.Owner as Unit, out var formulaValue))
                        return false;

                    value = formulaValue;
                    return true;
                }
            }

            default:
                // Anything else (DynamicFunc, ManualFunc) is reported once at content load; see
                // DynamicBonusFuncRules.SummarizeUnsupported.
                return false;
        }
    }
}
