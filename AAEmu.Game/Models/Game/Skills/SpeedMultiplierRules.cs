namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The attack-speed ratings behind a unit's attack interval: <c>attack_speed_mul</c>
/// (<c>enum_unit_attribute</c> 218), the melee and ranged views <c>melee_speed_mul</c>/<c>ranged_speed_mul</c>
/// (54/55), the animation view <c>attack_anim_speed_mul</c> (119) and the pre-existing
/// <c>global_cooldown_mul</c> (74).
/// </summary>
/// <remarks>
/// All five ids are one rating seen from five sides, and the content DB proves it: for the 390 buff rows
/// that carry 54, the rows for 74 and 119 store the identical value (<c>select owner_id, value from
/// unit_modifiers where owner_type='Buff' and unit_attribute_id in (54,74,119)</c> agrees on 377 and 384 of
/// them, and 55 agrees with 74 on 373). Only 218 is a family of its own: 75 buff rows carry it and just
/// four of them also carry 74. So an attack interval takes the newest rating the unit has and never two of
/// them at once — <see cref="AttackIntervalFactor"/> — which is also why 54/55 can be consumed here without
/// squaring the <c>global_cooldown_mul</c> factor the weapon-speed branch already applied.
///
/// The direction is the one <c>Character.GlobalCooldownMul</c> already implements for 74: the stored value
/// is a per-mille delta on a rate of 1000 (a <c>unit_modifiers</c> row of -700 means "70% slower"), and the
/// interval is divided by the rate. Buff 4343 (공속테스트 (느려짐), "attack speed reduced by 66%") stores
/// -700 on 54, 55, 74 and 119 and its own tooltip confirms the shape once the value is clipped to the
/// client's bound below.
/// </remarks>
public static class SpeedMultiplierRules
{
    /// <summary>Rating meaning "no change": the interval factor is exactly 1.0.</summary>
    public const int Baseline = 1000;

    /// <summary>
    /// Bounds the client puts on these ids. <c>unit_attribute_limits</c> rows 1 (74), 3 (119), 4 (54), 5 (55)
    /// and 46 (218) all carry minimum -666 and maximum 2000, so the two shipped rows that sit outside the
    /// range — buff 28775 at -900 and buff 5913 at +4000 — are clipped before they reach a formula. Applying
    /// the clip here keeps a stacked debuff from turning into an infinite interval.
    /// </summary>
    public const long MinRating = -666;
    public const long MaxRating = 2000;

    public static long ClampRating(long rating) => Math.Clamp(rating, MinRating, MaxRating);

    /// <summary>
    /// The factor an attack interval is multiplied by for a unit carrying <paramref name="rating"/>:
    /// 1000/(1000 + rating), so -666 is 2.99x slower and +2000 is 0.33x.
    /// </summary>
    /// <remarks>
    /// The <c>100000f / (rating + 1000f)</c> form and the truncation to a whole percent are not decoration.
    /// They are the arithmetic <c>Character.GlobalCooldownMul</c> already applies to weapon speed and to the
    /// GCD, so a rating that arrives here from 54/55/119/218 produces bit-identical numbers to the same
    /// rating arriving through 74 — which is what makes consuming 54/55 provably neutral on the 377 buff rows
    /// where the two agree.
    /// </remarks>
    public static double DelayFactor(long rating) => (int)(100000f / (ClampRating(rating) + 1000f)) / 100f;

    /// <summary>
    /// The pace of an attack: the general attack-speed rating when the unit carries one, otherwise the rating
    /// for that attack's own type, otherwise <paramref name="fallbackFactor"/> — the factor the caller
    /// already had, which is how a unit with none of these rows keeps its current interval exactly.
    /// </summary>
    /// <param name="attackSpeedRating">Sum of the unit's <c>attack_speed_mul</c> (218) rows; 0 when absent.</param>
    /// <param name="typeRating">
    /// Sum of the matching melee/ranged <c>*_speed_mul</c> rows (54 for a melee or offhand attack, 55 for a
    /// ranged one); 0 when the attack has no weapon type, which leaves the value to 218 or the fallback.
    /// </param>
    /// <param name="fallbackFactor">The caller's existing factor, i.e. <c>GlobalCooldownMul / 100</c> or 1.0.</param>
    public static double AttackIntervalFactor(long attackSpeedRating, long typeRating, double fallbackFactor)
    {
        if (attackSpeedRating != 0)
            return DelayFactor(attackSpeedRating);
        if (typeRating != 0)
            return DelayFactor(typeRating);

        return fallbackFactor;
    }

    /// <summary>
    /// The pace of the animation a skill plays, which the server turns into the combat-sync delay before the
    /// hit lands. <c>attack_anim_speed_mul</c> (119) is the animation's own view of the same rating; a unit
    /// without one falls back to <paramref name="fallbackFactor"/>, the <c>global_cooldown_mul</c> factor the
    /// delay was already scaled by.
    /// </summary>
    public static double AnimationFactor(long animationSpeedRating, double fallbackFactor) =>
        animationSpeedRating != 0 ? DelayFactor(animationSpeedRating) : fallbackFactor;
}
