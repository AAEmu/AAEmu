namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// One <c>unit_attribute_limits</c> row: the bounds a composed attribute value is kept inside.
/// </summary>
public readonly record struct UnitAttributeLimit(long Minimum, long Maximum);

/// <summary>
/// Clamps a composed unit-attribute value to its <c>unit_attribute_limits</c> row.
/// </summary>
/// <remarks>
/// 10.0.2.13 ships 49 rows, e.g. <c>move_speed_mul</c> -10000..8000, <c>global_cooldown_mul</c>
/// -666..2000 or <c>item_evolving_cost_mul</c> -1000..0. They bound the value the client shows and the
/// formulas consume, so the clamp runs on the value <c>Unit.CalculateWithBonuses</c> has finished
/// composing - base plus every flat, percent, static and dynamic bonus - never on a single bonus.
///
/// An attribute with no row is returned untouched, which is every attribute but those 49.
/// </remarks>
public static class UnitAttributeLimitRules
{
    /// <summary>
    /// The composed value, kept inside <paramref name="limit"/> when it has one.
    /// </summary>
    /// <param name="composedValue">The value after every bonus has been folded in.</param>
    /// <param name="baseValue">
    /// What the caller passed in, i.e. the value with no bonuses at all. It says which scale the
    /// attribute is composed in and is never clamped itself.
    /// </param>
    /// <param name="limit">The row for this attribute, or null when the table has none.</param>
    public static double Clamp(double composedValue, double baseValue, UnitAttributeLimit? limit)
    {
        if (limit is not { } bounds)
            return composedValue;

        // The caller's base decides whether the row can be read in the caller's scale. When the base
        // already sits outside the row the two disagree about the unit, and clamping would rewrite the
        // baseline instead of bounding bonuses. drop_rate_mul is the live case: Character.DropRateMul
        // composes 0 as "no bonus" and LootPack adds the row's 100 baseline itself, as
        // `(100 + DropRateMul) / 100`, so clamping 0 up to the row's minimum of 100 would double every
        // loot roll. Such a row is reported by the loader and left alone here.
        if (baseValue < bounds.Minimum || baseValue > bounds.Maximum)
            return composedValue;

        if (composedValue < bounds.Minimum)
            return bounds.Minimum;
        if (composedValue > bounds.Maximum)
            return bounds.Maximum;
        return composedValue;
    }
}
