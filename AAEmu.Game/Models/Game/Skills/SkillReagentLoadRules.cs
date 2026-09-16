namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The <c>skill_reagents.enable</c> filter. The 10.0.2.13 table holds 2 804 rows and 2 of them are
/// disabled — row 3852 (skill 36492 wants item 43108) and row 4995 (skill 50997 wants item 54478) —
/// and the loader used to add them like any other, so those two skills charged a reagent the content
/// had switched off.
/// </summary>
public static class SkillReagentLoadRules
{
    /// <summary>Whether a row's <c>enable</c> column lets it load.</summary>
    public static bool IsEnabled(bool enable) => enable;

    /// <summary>
    /// The enabled rows, keyed by row id exactly as <c>SkillManager._skillReagents</c> stores them.
    /// </summary>
    public static Dictionary<uint, SkillReagent> Enabled(IEnumerable<SkillReagent> rows)
    {
        var load = new Dictionary<uint, SkillReagent>();
        if (rows == null)
            return load;

        foreach (var row in rows)
        {
            if (row != null && IsEnabled(row.Enable))
                load[row.Id] = row;
        }

        return load;
    }

    /// <summary>The disabled rows' ids, ascending, for the one line the loader reports them on.</summary>
    public static IReadOnlyList<uint> DisabledRowIds(IEnumerable<SkillReagent> rows)
    {
        var disabled = new SortedSet<uint>();
        if (rows == null)
            return [];

        foreach (var row in rows)
        {
            if (row != null && !IsEnabled(row.Enable))
                disabled.Add(row.Id);
        }

        return disabled.ToList();
    }

    /// <summary>The single warning line for the table, naming the rows and the skills they belong to.</summary>
    public static string DisabledWarning(IReadOnlyCollection<SkillReagent> rows)
    {
        var disabled = (rows ?? []).Where(row => row != null && !IsEnabled(row.Enable)).ToList();
        if (disabled.Count == 0)
            return string.Empty;

        var detail = string.Join(", ", disabled.Select(row => $"row {row.Id} skips skill {row.SkillId}"));
        return $"skill_reagents: {disabled.Count} disabled row(s) skipped ({detail}).";
    }
}
