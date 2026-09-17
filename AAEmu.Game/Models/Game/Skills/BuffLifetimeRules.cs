namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.max_life_time</c>: the ceiling on how long one instance of a buff may live.
/// </summary>
/// <remarks>
/// 96 of 30,654 shipped buffs author it, and it is a ceiling rather than a duration. The base
/// <c>duration</c> of 11 of those 96 is already longer than the ceiling — 31544 전율하는 매: 파도 is
/// <c>duration</c> 8,000 against <c>max_life_time</c> 4,000, 18379 방패 진격 3,000 against 1,000 — so
/// there is no ordering in which the base duration alone produces the authored lifetime, and the column
/// cannot be a second duration that simply wins.
/// <para>
/// The interesting rows are the duration-0 families that carry one. Duration 0 is this server's
/// "permanent" — <c>Buff.GetTimeLeft</c> answers -1 for it and no dispel is scheduled — so 23749 깃발의
/// 기운 (11,000) and 23151 추격: 파도 (5,000), 30 rows in all, are currently permanent buffs the content
/// says should expire. The clamp gives them their expiry.
/// </para>
/// </remarks>
public static class BuffLifetimeRules
{
    /// <summary>
    /// <paramref name="duration"/> held down to <paramref name="maxLifeTime"/> when the template
    /// authors a ceiling.
    /// </summary>
    /// <remarks>
    /// A ceiling of zero means "no ceiling": every duration is returned unchanged, including the 0 that
    /// means permanent, because the clamp must not invent an expiry for a family whose content says there
    /// is none. That covers 30,558 of the 30,654 rows. The column is non-negative in the database (all
    /// 30,654 rows are integers, none below zero), so the parameter is unsigned rather than carrying a
    /// sentinel meaning.
    /// </remarks>
    public static int ClampedDuration(int duration, uint maxLifeTime)
    {
        if (maxLifeTime == 0)
            return duration;

        // Duration 0 is the permanent sentinel, not a zero-length buff: with a ceiling authored it means
        // "until the ceiling", which is the only value left for it to mean.
        if (duration <= 0)
            return (int)maxLifeTime;

        return Math.Min(duration, (int)maxLifeTime);
    }
}
