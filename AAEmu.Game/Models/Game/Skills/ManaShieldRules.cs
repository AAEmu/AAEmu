namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.mana_shield_ratio</c> (11 rows): while the buff is up, damage comes off mana before it comes
/// off health.
/// </summary>
/// <remarks>
/// The ratio is stored in hundredths of a percent and is a share of the incoming hit, not a pool: 20433
/// 마나실드 and 22945 활력 보호막 carry 10000, and buff 27915 마나실드(test) spells its own description out
/// as "100%". 19960 하나된 마법의 힘 보호막 carries 15000, which is the same reading one step further —
/// every point of damage costs one and a half points of mana. The shield stops paying when the mana runs
/// out; the rest of the hit falls through to health, and the buff itself is left alone, because the column
/// is a redirection and nothing in the content gives it a pool of its own.
/// </remarks>
public static class ManaShieldRules
{
    /// <summary>Stored values are hundredths of a percent: 10000 is 100%.</summary>
    public const int RatioScale = 10000;

    /// <summary>The stored ratio as a percentage, floored at 0 so a negative row is inert.</summary>
    public static int EffectiveRatio(int stored) => Math.Max(0, stored);

    /// <summary>
    /// How much of <paramref name="damage"/> comes off mana: the row's share of the hit, held to what the
    /// unit actually has. The remainder is what the hit still costs in health.
    /// </summary>
    public static int ChargedToMana(int damage, int ratio, long availableMp)
    {
        var effective = EffectiveRatio(ratio);
        if (damage <= 0 || effective <= 0 || availableMp <= 0)
            return 0;

        var wanted = (long)damage * effective / RatioScale;
        return (int)Math.Min(wanted, Math.Min(availableMp, damage));
    }

    /// <summary>
    /// The strongest ratio among the active shields, so two overlapping mana shields resolve to the same
    /// one every time instead of whichever the buff list happens to enumerate first. 0 when none is up.
    /// </summary>
    public static int StrongestRatio(IEnumerable<int> ratios)
    {
        var strongest = 0;
        foreach (var ratio in ratios)
            strongest = Math.Max(strongest, EffectiveRatio(ratio));

        return strongest;
    }
}
