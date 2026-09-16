using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which <c>dynamic_unit_modifiers.func_type</c> values this server evaluates, and the one-line
/// report for the ones it does not.
/// </summary>
/// <remarks>
/// 10.0.2.13 ships four func types: LinearFunc (466 rows), FormulaFunc (233), DynamicFunc (12) and
/// ManualFunc (2). LinearFunc and FormulaFunc are implemented. The other two name their expression in
/// the <c>formulas</c> table, not in <c>formula_funcs</c> (the two id spaces overlap, but DynamicFunc
/// func_id 3 exists only in <c>formulas</c>): DynamicFunc ids are expressions whose variables
/// (range, item_level, item_grade, gear_score_multiplier, scaling_multiplier, element_level,
/// source_level, target_level, stealth_level, damage_percent, casting_tolerance, labor_power) are
/// supplied per call site by the damage/stealth/skill pipelines, and the two ManualFunc rows — both
/// move_speed_mul — are formula 11, a range-dependent multiplier, and formula 15, the literal -1,
/// which the native client applies by hand at its own call site.
/// </remarks>
public static class DynamicBonusFuncRules
{
    public const string LinearFuncType = "LinearFunc";
    public const string FormulaFuncType = "FormulaFunc";

    /// <summary>Whether the server can compute a value for <paramref name="funcType"/>.</summary>
    public static bool IsSupported(string funcType) => funcType is LinearFuncType or FormulaFuncType;

    /// <summary>
    /// One line naming how many rows were dropped and which func types and func_ids they use, or
    /// null when every row is supported. Built once at content load so a dropped row is reported once
    /// instead of on every buff application.
    /// </summary>
    public static string SummarizeUnsupported(IEnumerable<DynamicBonusTemplate> templates)
    {
        var idsByType = new SortedDictionary<string, SortedSet<uint>>(StringComparer.Ordinal);
        var rowCount = 0;

        foreach (var template in templates ?? [])
        {
            if (template == null || IsSupported(template.FuncType))
                continue;

            rowCount++;
            var funcType = string.IsNullOrEmpty(template.FuncType) ? "(none)" : template.FuncType;
            if (!idsByType.TryGetValue(funcType, out var funcIds))
                idsByType[funcType] = funcIds = [];
            funcIds.Add(template.FuncId);
        }

        if (rowCount == 0)
            return null;

        var detail = string.Join("; ", idsByType.Select(pair =>
            $"{pair.Key}: {pair.Value.Count} func_ids ({string.Join(",", pair.Value)})"));
        return $"10.0.2.13: {rowCount} dynamic_unit_modifiers rows use func types this server does not " +
               $"evaluate and are inert ({detail})";
    }
}
