using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Instant complete's extra gold: <see cref="FormulaKind.MinCraftOrderFee"/> then
/// <see cref="FormulaKind.CraftOrderAdditionalFee"/>, each rounded the way the client
/// turns the float into copper.
/// </summary>
public static class CraftOrderInstantFeeRules
{
    /// <summary>
    /// Instant complete writes the product grade into the <c>pc_actability</c> slot of
    /// <see cref="FormulaKind.MinCraftOrderFee"/>. The player's actability points are not used
    /// on that path.
    /// </summary>
    public static int InstantPcActability(byte grade) => grade;

    /// <summary>
    /// Min fee, then additional fee. Missing text, a failed evaluate, or a negative result is
    /// no fee the Instant path can charge — the caller must refuse rather than invent a number.
    /// </summary>
    public static bool TryAdditionalFee(
        Formula minFeeFormula,
        Formula additionalFeeFormula,
        int craftCost,
        int consumeLp,
        int requireActability,
        int pcActability,
        uint craftCount,
        out int additionalFee)
    {
        additionalFee = 0;
        if (!TryMinFee(minFeeFormula, craftCost, consumeLp, requireActability, pcActability, out var minFee))
            return false;

        return TryAdditionalFee(additionalFeeFormula, consumeLp, craftCount, craftCost, minFee, out additionalFee);
    }

    public static bool TryMinFee(
        Formula formula,
        int craftCost,
        int consumeLp,
        int requireActability,
        int pcActability,
        out int minFee)
    {
        minFee = 0;
        if (formula == null)
            return false;

        var parameters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["craft_cost"] = craftCost,
            ["consume_lp"] = consumeLp,
            ["require_actability"] = requireActability,
            ["pc_actability"] = pcActability
        };
        if (!formula.TryEvaluate(parameters, out var raw))
            return false;

        minFee = RoundToCopper(raw);
        return minFee >= 0;
    }

    public static bool TryAdditionalFee(
        Formula formula,
        int consumeLp,
        uint craftCount,
        int craftCost,
        int minFee,
        out int additionalFee)
    {
        additionalFee = 0;
        if (formula == null)
            return false;

        var parameters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["consume_lp"] = consumeLp,
            ["craft_count"] = craftCount,
            ["craft_cost"] = craftCost,
            ["min_craft_order_fee"] = minFee
        };
        if (!formula.TryEvaluate(parameters, out var raw))
            return false;

        additionalFee = RoundToCopper(raw);
        return additionalFee >= 0;
    }

    /// <summary>Positive copper: add 0.5 and take the integer part. Negatives are rejected.</summary>
    public static int RoundToCopper(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return -1;

        return (int)Math.Floor(value + 0.5);
    }
}
