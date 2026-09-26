using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The distance band a cast is measured against and the two verdicts it can fail with.
/// </summary>
/// <remarks>
/// The client validates a unit-target cast in two steps. It resolves the target by
/// target type and measures only when that unit is not the caster itself; a self cast is
/// never measured. The band is built from: min_range and
/// max_range from the skill record, or from the equipped holdable when weapon_slot_for_range_id names a
/// slot, then skill attribute 17 (min_range) on the minimum and attribute 2 (range) on the maximum, and a
/// maximum that ends up below the minimum is lifted to min + 0.5, then it judges,
/// in this order: TOO_CLOSE_RANGE (0xE) when min &gt; 0 and distance &lt;= min, TOO_FAR_RANGE (0xF) when
/// distance &gt; max. So a target standing at exactly min_range is too close and one at exactly max_range is
/// in range. The distance is: the smallest shape-to-shape distance from the caster's collision
/// shape to any of the target's (double-dispatches on the two shape kinds), so it is measured
/// edge to edge, which is what BaseUnit.GetDistanceTo does with the actor model radii.
///
/// content, 10.0.2.13 game_decrypted: 975 of 38,043 skills carry a min_range. 123 of them target self
/// (51 are Npc kit rows in np_skills, 35 are plot_only, the rest quest and test skills) and only two are
/// player abilities, both ground-targeted: 13281 다발 사격 (10..50 m) and 23587 적진으로 (6..20 m). One
/// skill_modifiers row touches attribute 17: 2144, buff 27701 on tag 3849, +4. holdables row 0 (fist) is
/// 0..3 m, row 19 (bow) 0..20 m and row 31 (shot_gun) 0..15 m.
/// </remarks>
public static class SkillRangeRules
{
    /// <summary>How far a maximum that fell below the minimum is lifted above it.</summary>
    public const double MaxBelowMinLift = 0.5;

    /// <summary>The band in metres. <see cref="MaxUnbounded"/> means no far limit applies to this cast.</summary>
    public readonly record struct Band(double Min, double Max, bool MaxUnbounded)
    {
        /// <summary>A band as the client builds it: a negative minimum is no minimum, and a maximum below
        /// the minimum is lifted to min + 0.5 (only 46100 술통 기뢰, 3..0, ships one).</summary>
        public static Band Of(double min, double max, bool maxUnbounded = false)
        {
            var effectiveMin = Math.Max(0d, min);
            var effectiveMax = max < effectiveMin ? effectiveMin + MaxBelowMinLift : max;
            return new Band(effectiveMin, effectiveMax, maxUnbounded);
        }
    }

    /// <summary>
    /// Whether the band is measured at all. The client skips the check when the resolved
    /// target is the caster, so the 123 self-target skills with a minimum (the distance to
    /// oneself being 0) fire.
    /// </summary>
    public static bool Measures(uint casterObjId, uint targetObjId) => casterObjId != targetObjId;

    /// <summary>
    /// The verdict for <paramref name="distance"/> against <paramref name="band"/>, or null when the cast
    /// is in range. Too close is judged first, as the client does.
    /// </summary>
    public static SkillResult? Check(double distance, Band band)
    {
        if (band.Min > 0d && distance <= band.Min)
            return SkillResult.TooCloseRange;
        if (!band.MaxUnbounded && distance > band.Max)
            return SkillResult.TooFarRange;
        return null;
    }
}
