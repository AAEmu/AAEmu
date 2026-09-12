using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// Explicit emulator policy for Farmhand bonus harvests and experience. The 10.0.2.13 client carries
/// the inputs and resulting state, but the original World backend calculations are not available.
/// </summary>
public sealed class ButlerHarvestRewardPolicy
{
    private readonly Func<uint, uint, double> _evaluateExperience;
    private readonly Func<long, long> _nextRoll;

    public ButlerHarvestRewardPolicy(IFormulaManager formulaManager)
        : this(
            (laborPower, butlerLevel) => EvaluateExperience(formulaManager, laborPower, butlerLevel),
            upperExclusive => Random.Shared.NextInt64(upperExclusive))
    {
    }

    internal ButlerHarvestRewardPolicy(
        Func<uint, uint, double> evaluateExperience,
        Func<long, long> nextRoll)
    {
        _evaluateExperience = evaluateExperience ?? throw new ArgumentNullException(nameof(evaluateExperience));
        _nextRoll = nextRoll ?? throw new ArgumentNullException(nameof(nextRoll));
    }

    /// <summary>
    /// Treats the static harvest ratio plus the currently emitted Farmhand bonus attribute as units of
    /// <paramref name="ratioScale"/>. A zero scale is the operator switch that disables bonus rolls.
    /// </summary>
    public bool GrantsBonus(uint contentRatio, uint bonusRatioAttribute, uint ratioScale)
    {
        if (ratioScale == 0)
            return false;

        var effectiveRatio = Math.Min((ulong)contentRatio + bonusRatioAttribute, ratioScale);
        if (effectiveRatio == 0)
            return false;
        if (effectiveRatio == ratioScale)
            return true;

        return (ulong)_nextRoll(ratioScale) < effectiveRatio;
    }

    /// <summary>
    /// Calculates the one-time XP award for a completed registration. Positive formula results follow
    /// Character.ChangeLabor's integer truncation. Storage overflow fails closed; usable Farmhand
    /// level resolution remains independently capped at level 40 without capping valid cumulative XP.
    /// </summary>
    public bool TryCalculateExperienceAward(
        uint laborPowerForExperience,
        uint currentButlerLevel,
        double experienceRate,
        ulong currentExperience,
        out ulong award,
        out ulong resultingExperience)
    {
        award = 0;
        resultingExperience = currentExperience;
        if (!double.IsFinite(experienceRate) || experienceRate < 0d || currentButlerLevel == 0)
            return false;
        if (experienceRate == 0d || laborPowerForExperience == 0)
            return true;

        var evaluated = _evaluateExperience(laborPowerForExperience, currentButlerLevel) * experienceRate;
        if (!double.IsFinite(evaluated) || evaluated < 0d)
            return false;

        // Character.ChangeLabor uses a positive int cast. Values outside that range are invalid content/config,
        // rather than an opportunity to wrap the permanent-data scalar.
        if (evaluated > int.MaxValue)
            return false;
        award = (ulong)(int)evaluated;
        if (ulong.MaxValue - currentExperience < award)
        {
            award = 0;
            return false;
        }

        resultingExperience = currentExperience + award;
        return true;
    }

    private static double EvaluateExperience(IFormulaManager formulaManager, uint laborPower, uint butlerLevel)
    {
        var formula = formulaManager?.GetFormula((uint)FormulaKind.ExpByLaborPower);
        if (formula == null)
            return double.NaN;

        return formula.Evaluate(new Dictionary<string, double>
        {
            ["labor_power"] = laborPower,
            ["pc_level"] = butlerLevel
        });
    }
}
