﻿namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The speed ratings used by combat timing: general <c>attack_speed_mul</c> (218), melee/ranged weapon
/// interval views (54/55), animation-specific speed (119), and the existing global-cooldown multiplier (74).
/// </summary>
/// <remarks>
/// These database attributes can mirror one another on buffs, so combat timing selects the rating for its
/// purpose instead of multiplying aliases together: 218 drives shared skill GCD and animation speed; 54/55
/// can adjust melee/ranged weapon intervals; 119 is the animation-specific fallback; and 74 is the existing
/// global-cooldown fallback.
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
    /// The factor an interval is multiplied by for a unit carrying <paramref name="rating"/>:
    /// 1000/(1000 + rating), so -666 is 2.99x slower and +2000 is 0.33x.
    /// </summary>
    /// <remarks>
    /// The <c>100000f / (rating + 1000f)</c> form and truncation to a whole percent match the existing
    /// <c>Character.GlobalCooldownMul</c> calculation for legacy callers.
    /// </remarks>
    public static double DelayFactor(long rating) => (int)(100000f / (ClampRating(rating) + 1000f)) / 100f;

    /// <summary>
    /// The continuous interval factor for the general attack-speed rating. Keeping the fractional percent
    /// gives a +177 rating 84.96% of the base interval, which the client displays as 85%.
    /// </summary>
    public static double AttackSpeedDelayFactor(long rating) => 1000d / (ClampRating(rating) + Baseline);

    /// <summary>
    /// General attack-speed selection for callers that model animation/GCD timing: the general rating when
    /// present, otherwise a supplied type rating, otherwise <paramref name="fallbackFactor"/>.
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
    /// Weapon swing interval selection. General attack speed is intentionally excluded because it controls
    /// attack animation completion and shared skill GCD, not the interval between auto-attacks.
    /// </summary>
    public static double WeaponIntervalFactor(long typeRating, double fallbackFactor) =>
        typeRating != 0 ? DelayFactor(typeRating) : fallbackFactor;

    /// <summary>
    /// The pace of the animation a skill plays, which the server turns into the combat-sync delay before the
    /// hit lands. <c>attack_anim_speed_mul</c> (119) is the animation's own view of the same rating; a unit
    /// without one falls back to <paramref name="fallbackFactor"/>, the <c>global_cooldown_mul</c> factor the
    /// delay was already scaled by.
    /// </summary>
    public static double AnimationFactor(long animationSpeedRating, double fallbackFactor) =>
        animationSpeedRating != 0 ? DelayFactor(animationSpeedRating) : fallbackFactor;

    /// <summary>
    /// General attack speed paces skill animations when present. The animation-specific rating remains a
    /// fallback for content that supplies it directly, followed by the existing global-cooldown multiplier.
    /// These attributes are alternate views of speed and must not be multiplied together.
    /// </summary>
    public static double AnimationFactor(long attackSpeedRating, long animationSpeedRating, double fallbackFactor) =>
        attackSpeedRating != 0
            ? AttackSpeedDelayFactor(attackSpeedRating)
            : AnimationFactor(animationSpeedRating, fallbackFactor);
}
