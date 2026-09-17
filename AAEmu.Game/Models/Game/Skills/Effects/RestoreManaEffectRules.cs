namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The one authored decision a <see cref="RestoreManaEffect"/> makes around the number it composes:
/// whether the row restores a share of the target's maximum mana instead of an absolute amount.
/// </summary>
public static class RestoreManaEffectRules
{
    /// <summary>
    /// <c>restore_mana_effects.percent</c> (144 of 261 rows): the value is a share of the target's maximum
    /// mana, and the row restores that share instead of the absolute value it would otherwise compose.
    /// </summary>
    public static bool IsPercent(bool percent, bool useFixedValue) => percent && useFixedValue;

    /// <summary>
    /// The percentage a percent row rolls, inclusive of both ends.
    /// </summary>
    /// <remarks>
    /// <c>fixed_min</c> == <c>fixed_max</c> on 140 of the 144 percent rows; the exception is restore-mana
    /// effect 89 at 10–20.
    /// </remarks>
    public static int PercentRoll(int fixedMin, int fixedMax, int roll)
    {
        var lo = Math.Min(fixedMin, fixedMax);
        var hi = Math.Max(fixedMin, fixedMax);
        if (hi <= lo)
            return lo;
        return lo + Math.Clamp(roll, 0, hi - lo);
    }

    /// <summary>
    /// The mana a percent row restores: that share of <paramref name="targetMaxMp"/>, floored.
    /// </summary>
    /// <remarks>
    /// The percent rows are authored as round shares — 5, 10, 20, 50, 100 — and read as absolute amounts
    /// they restore a level-50 character's mana bar for ten points. Six of them author 0–0, which restores
    /// nothing and is left at zero rather than falling through to the absolute composition, because the
    /// row says "a share" and its share is nothing.
    /// </remarks>
    public static int PercentAmount(int targetMaxMp, int fixedMin, int fixedMax, int roll)
    {
        var percent = PercentRoll(fixedMin, fixedMax, roll);
        return (int)(Math.Max(0, targetMaxMp) * (percent / 100f));
    }
}
