namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The authored decisions a <see cref="HealEffect"/> makes around the number it composes: whether the row
/// heals a share of the target's maximum instead of an absolute amount, and whether the caster and the
/// healed unit are the same unit (which the self-target multiplier is keyed on).
/// </summary>
/// <remarks>
/// Every member here answers "no row said otherwise" with the behaviour the effect already had, so a heal
/// row that authors none of these columns heals exactly what it healed before.
/// </remarks>
public static class HealEffectRules
{
    /// <summary>
    /// The percentage a percent row rolls, inclusive of both ends.
    /// </summary>
    /// <remarks>
    /// <c>fixed_min</c> == <c>fixed_max</c> on 217 of the 237 <c>percent</c> rows, so the common case is
    /// exact and the roll only exists for the authored ranges (heal effect 813 is 10–50, 279 is 400–500).
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
    /// The heal a percent row pays out: that share of <paramref name="targetMaxHp"/>, floored.
    /// </summary>
    /// <remarks>
    /// A percent row is a share of the healed unit's own ceiling and replaces the composed heal rather
    /// than entering it as a term. <c>heal_effects.percent</c> is set on 237 rows and every one of them
    /// also carries <c>use_fixed_heal</c>, authored as a round percentage — 10, 20, 30, 40, 50, 70, 100,
    /// 130, 200, 210, 500, 700, 1000, 2000 (heal effect 299 is 10 %, 359 is 2000 %). Read as absolute
    /// amounts those same rows heal a level-50 character for ten health, which is the "237 rows → flat HP"
    /// the effect used to do. The columns that compose an absolute heal — <c>dps_multiplier</c>,
    /// <c>level_md</c>, <c>use_charged_buff</c> — do not apply to a share.
    /// </remarks>
    public static int PercentAmount(int targetMaxHp, int fixedMin, int fixedMax, int roll)
    {
        var percent = PercentRoll(fixedMin, fixedMax, roll);
        return (int)(Math.Max(0, targetMaxHp) * (percent / 100f));
    }

    /// <summary>
    /// <c>heal_effects.self_target_multiplier</c> (152 rows at 0.7, 802 at 1.0): the factor applied when
    /// the caster heals itself.
    /// </summary>
    /// <remarks>
    /// The 0.7 rows are the heal-over-time songs and the group heals that include the caster — heal effects
    /// 21, 26, 94, 184–187 — where the healer gets less out of its own copy. The factor is held at 1.0 for
    /// every other target, so the only heals this can move are the 152 flagged rows cast on the caster.
    /// A row that authors no multiplier reads back as 0 and is neutral, the same as the 1.0 rows.
    /// </remarks>
    public static float SelfTargetMultiplier(float selfTargetMultiplier, bool isSelf)
    {
        if (!isSelf)
            return 1.0f;
        return selfTargetMultiplier > 0f ? selfTargetMultiplier : 1.0f;
    }

    /// <summary>
    /// Whether caster and healed unit are the same unit. Both sides have to carry a real object id: a zero
    /// id is an unspawned unit (tests, plot placeholders) and two of those are not "the same unit".
    /// </summary>
    public static bool IsSelfTarget(uint casterObjId, uint targetObjId) =>
        casterObjId != 0 && casterObjId == targetObjId;
}
