using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a hit does to a cast in progress: cancel it, or push it back.
/// </summary>
/// <remarks>
/// Three unread flags (<c>stop_casting_on_big_hit</c> on 456 skills, <c>casting_cancelable</c> on 187,
/// <c>casting_delayable</c> on 7,002) and two unevaluated <c>formulas</c> rows decide this:
/// <list type="bullet">
/// <item><description><c>formulas</c> 2, <c>enum_formula_kinds.casting_cancel_percent</c>:
/// <c>0 * (100 / casting_tolerance) * (1 + (1 * floor(damage_percent / 10)))</c>. The shipped expression
/// is multiplied by zero, so it evaluates to 0 for every hit and <b>damage never cancels a cast through
/// this formula</b>. It is evaluated rather than skipped so a server that edits the row gets the edited
/// behaviour.</description></item>
/// <item><description><c>formulas</c> 3, <c>casting_delay_time</c>:
/// <c>300 * (100 / casting_tolerance) * (0.42 * floor(damage_percent / 5))</c>. With the shipped
/// <c>casting_tolerance</c> of 100 that is 126 ms per whole 5% of the victim's maximum health, so a
/// 25% hit pushes the cast back 630 ms. A delay is capped at
/// <see cref="MaximumDelayMilliseconds"/>.</description></item>
/// </list>
/// <c>casting_tolerance</c> is <c>unit_formulas</c> kind 30 and is the literal 100 for all eight
/// <c>enum_unit_owner_types</c> rows in the content; <c>unit_attribute</c> 89 carries the same name and
/// is read by nothing.
///
/// The "big hit" in <c>stop_casting_on_big_hit</c> has no threshold column, so it uses the granularity
/// formula 2 counts damage in: one whole 10% of the victim's maximum health.
/// </remarks>
public static class SkillCastInterruptRules
{
    /// <summary><c>formulas</c> row for <c>casting_cancel_percent</c>.</summary>
    public const uint CastingCancelPercentFormulaId = 2;

    /// <summary><c>formulas</c> row for <c>casting_delay_time</c>.</summary>
    public const uint CastingDelayTimeFormulaId = 3;

    /// <summary>The shipped <c>casting_tolerance</c>: 100 means "no adjustment".</summary>
    public const double DefaultCastingTolerance = 100.0;

    /// <summary>A single hit costs at most this much extra cast time.</summary>
    public const int MaximumDelayMilliseconds = 5000;

    /// <summary>A hit of at least this share of maximum health counts as a big hit.</summary>
    public const double BigHitPercent = 10.0;

    /// <summary>What the cast does about one hit.</summary>
    public readonly record struct InterruptDecision(bool Cancel, int DelayMilliseconds);

    public static readonly InterruptDecision NoInterrupt = new(false, 0);

    /// <summary>The hit as a percentage of the victim's maximum health; 0 when there is no maximum.</summary>
    public static double DamagePercent(int damage, int maxHp)
        => maxHp <= 0 ? 0d : damage * 100d / maxHp;

    public static bool IsBigHit(double damagePercent) => damagePercent >= BigHitPercent;

    /// <summary>
    /// The cancel chance out of 100, clamped into range. A missing or unparsable formula gives 0, which
    /// is also what the shipped row evaluates to.
    /// </summary>
    public static double CancelPercent(double? formulaValue)
        => formulaValue.HasValue ? Math.Clamp(formulaValue.Value, 0d, 100d) : 0d;

    /// <summary>
    /// The extra cast time, clamped into 0..<see cref="MaximumDelayMilliseconds"/>. A negative formula
    /// value shortens nothing: the row is authored as a delay.
    /// </summary>
    public static int DelayMilliseconds(double? formulaValue)
        => formulaValue.HasValue
            ? (int)Math.Clamp(formulaValue.Value, 0d, MaximumDelayMilliseconds)
            : 0;

    /// <summary>
    /// Resolves one hit against a cast.
    /// </summary>
    /// <param name="stopCastingOnBigHit">The skill's <c>stop_casting_on_big_hit</c>.</param>
    /// <param name="castingCancelable">The skill's <c>casting_cancelable</c>: false means damage cannot break it.</param>
    /// <param name="castingDelayable">The skill's <c>casting_delayable</c>: false means damage cannot push it back.</param>
    /// <param name="damagePercent">The hit as a share of maximum health.</param>
    /// <param name="cancelPercent">Formula 2's value for this hit.</param>
    /// <param name="delayMs">Formula 3's value for this hit.</param>
    /// <param name="rollPercent">A uniform roll in 0..100 for this hit.</param>
    public static InterruptDecision Decide(
        bool stopCastingOnBigHit,
        bool castingCancelable,
        bool castingDelayable,
        double damagePercent,
        double cancelPercent,
        int delayMs,
        double rollPercent)
    {
        if (stopCastingOnBigHit && castingCancelable && IsBigHit(damagePercent))
            return new InterruptDecision(true, 0);

        if (castingCancelable && rollPercent < cancelPercent)
            return new InterruptDecision(true, 0);

        if (castingDelayable && delayMs > 0)
            return new InterruptDecision(false, delayMs);

        return NoInterrupt;
    }

    /// <summary>
    /// Evaluates one of the two cast-interruption formulas for a hit, or null when the row is absent or
    /// does not compile.
    /// </summary>
    public static double? Evaluate(double damagePercent, double castingTolerance, uint formulaId)
    {
        var formula = FormulaManager.Instance?.GetFormula(formulaId);
        if (formula == null)
            return null;

        var parameters = new Dictionary<string, double>(2, StringComparer.Ordinal)
        {
            ["damage_percent"] = damagePercent,
            ["casting_tolerance"] = castingTolerance <= 0 ? DefaultCastingTolerance : castingTolerance
        };

        return formula.TryEvaluate(parameters, out var value) ? value : null;
    }

    /// <summary>
    /// The <c>casting_tolerance</c> the two formulas divide by, read from <c>unit_formulas</c> kind 30.
    /// The shipped rows are the literal 100 for every owner type, and a load that has not happened (a
    /// unit test) falls back to the same value.
    /// </summary>
    public static double ReadCastingTolerance(Units.Unit unit)
    {
        var owner = unit switch
        {
            Char.Character => FormulaOwnerType.Character,
            NPChar.Npc => FormulaOwnerType.Npc,
            Units.Slave => FormulaOwnerType.Slave,
            Units.Mate => FormulaOwnerType.Mate,
            _ => FormulaOwnerType.Character
        };

        var formula = FormulaManager.Instance?.GetUnitFormula(owner, UnitFormulaKind.CastingTolerance);
        if (formula == null)
            return DefaultCastingTolerance;

        var value = formula.Evaluate([]);
        return value <= 0 ? DefaultCastingTolerance : value;
    }
}
