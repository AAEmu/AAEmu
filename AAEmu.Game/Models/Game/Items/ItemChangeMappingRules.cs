namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Pure decision helpers for awakening recipe lookup.
/// </summary>
public static class ItemChangeMappingRules
{
    /// <summary>
    /// Whether <paramref name="itemGrade"/> satisfies a mapping's <c>source_grade_id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A negative <paramref name="sourceGradeId"/> means any grade. A non-negative value is the
    /// <em>minimum</em> grade the piece must already hold - Exact Unique-only matching rejected
    /// pieces the awaken tab itself still offered (live: Brilliant Explorer dagger at grade 8
    /// against Rank-3 recipe <c>source_grade_id</c> 6).
    /// </para>
    /// </remarks>
    public static bool MatchesSourceGrade(int sourceGradeId, byte itemGrade)
    {
        if (sourceGradeId < 0)
            return true;

        return itemGrade >= sourceGradeId;
    }
}
