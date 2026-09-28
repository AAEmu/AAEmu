namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Ranking used by the aggro objective (quest_act_obj_aggros, 107 rows). The objective's
/// rank1/rank2/rank3 columns are percentile cut-offs on the same 0-100 scale as their
/// sibling reward columns rank1_ratio/rank2_ratio/rank3_ratio, whose shipped domain is
/// 0-100 with 100 meaning "the full pool" (Quest.Reward turns the ratio into a fraction
/// with /100). So the score handed to <see cref="Band"/> has to be a percentile too: the
/// strongest aggro on the table is percentile 0 and a unit that never aggroed is
/// percentile 100.
///
/// The percentile is "how much of the table out-aggroed me", so equal aggro scores equal
/// percentiles. Ranking by a stable sort over a concurrent table cannot promise that -
/// two units that tie would be separated by whichever one happened to be inserted first.
/// Counting instead of sorting also drops the O(n log n) and the allocation.
/// </summary>
public static class QuestAggroRankRules
{
    /// <summary>Percentile of a unit that is not on the aggro table at all: no aggro.</summary>
    public const float NoAggroPercentile = 100f;

    /// <summary>
    /// Percentile of a unit holding <paramref name="ownTotalAggro"/> on a table whose
    /// entries hold the values in <paramref name="tableTotalAggro"/>: the share of the
    /// table that out-aggroed it, in percent. 0 is the top of the table, and an empty
    /// table is no aggro.
    /// </summary>
    public static float PercentileFor(int ownTotalAggro, IReadOnlyList<int> tableTotalAggro)
    {
        if (tableTotalAggro.Count == 0)
            return NoAggroPercentile;

        var higher = 0;
        foreach (var total in tableTotalAggro)
        {
            if (total > ownTotalAggro)
                higher++;
        }
        return 100f * higher / tableTotalAggro.Count;
    }

    /// <summary>
    /// The band (1, 2 or 3) a percentile falls in, or 0 when it is past every cut-off and
    /// the row pays nothing. A NULL rank2/rank3 in content loads as 0, which can only be
    /// reached by the very top of the table - the widest cut-off is checked first, so that
    /// is the row's own first band and the degenerate 0 never steals it.
    /// </summary>
    public static int Band(float percentile, int rank1, int rank2, int rank3)
    {
        if (percentile <= rank1)
            return 1;
        if (percentile <= rank2)
            return 2;
        if (percentile <= rank3)
            return 3;
        return 0;
    }
}
