namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The refill arithmetic for a charge-bearing skill, as a value-in/value-out function.
/// </summary>
/// <param name="Available">Charges left after the elapsed time was credited.</param>
/// <param name="Since">Time since the last credited charge, to carry into the stored state.</param>
public readonly record struct ChargeRefill(int Available, TimeSpan Since);

/// <summary>
/// Rules behind the charge effects (158 charge_cooldown, 166 change_charge_skill_count,
/// 167 change_charge_cooldown) and behind spending a charge on cast.
/// </summary>
/// <remarks>
/// 26 skills author <c>skills.charge_count</c> (매의 발톱 11368 at 2, 다발 사격 13281 at 5, 빛의 사격
/// 38893 at 3, 영구동토 44677 and 시간의 고치 44713 at 3). A charge is one use the skill may be fired
/// for before it locks out: the pool refills one charge per recharge interval, and only the cast that
/// finds the pool empty arms the skill's own <c>cooldown_time</c>.
/// <para>
/// The interval is <c>skills.charge_cooldown_time</c>, not <c>cooldown_time</c>: 38893 pairs 3 charges
/// with a 16000 ms charge interval and a 9000 ms cooldown, and the four day-long rows (44677, 44702,
/// 44713, 44727) pair 3 charges with 86400000 against 300000. All 26 charge rows fill the column in;
/// the loader has read it into <c>SkillTemplate.ChargeCooldownTime</c> all along but nothing consumed
/// it before. Only the interval arithmetic lives here so it can be tested without a unit.
/// </para>
/// </remarks>
public static class ChargeSkillRules
{
    /// <summary>
    /// Credits the charges that have come back since <paramref name="since"/>.
    /// </summary>
    /// <param name="available">Charges the pool holds right now.</param>
    /// <param name="max">The pool's ceiling; a pool already at it does not bank progress.</param>
    /// <param name="rechargeMs">Interval per charge (<c>skills.charge_cooldown_time</c>); zero refills
    /// the whole pool at once.</param>
    /// <param name="since">How long ago the last charge was credited.</param>
    public static ChargeRefill Refill(int available, int max, uint rechargeMs, TimeSpan since)
    {
        if (max <= 0)
            return new ChargeRefill(0, since);

        if (available >= max)
            return new ChargeRefill(max, TimeSpan.Zero);

        if (rechargeMs == 0)
            return new ChargeRefill(max, TimeSpan.Zero);

        if (since < TimeSpan.Zero)
            since = TimeSpan.Zero;

        var granted = (int)Math.Min(int.MaxValue, (long)(since.TotalMilliseconds / rechargeMs));
        if (granted <= 0)
            return new ChargeRefill(available, since);

        var refilled = Math.Min(max, available + granted);
        if (refilled >= max)
            return new ChargeRefill(max, TimeSpan.Zero);

        // Only the credited intervals are consumed, so the remainder keeps running toward the next
        // charge instead of restarting from the moment somebody happened to look at the pool.
        return new ChargeRefill(refilled, since - TimeSpan.FromMilliseconds((double)rechargeMs * granted));
    }

    /// <summary>
    /// Pool ceiling after change_charge_skill_count's delta. The pool never drops below zero, and a
    /// ceiling of zero turns the skill back into an ordinary cooldown skill.
    /// </summary>
    public static int AddedMax(int max, int delta) => Math.Max(0, max + delta);

    /// <summary>
    /// Recharge interval after change_charge_cooldown's delta in milliseconds. Clamped at zero (an
    /// instant refill) rather than allowed to go negative.
    /// </summary>
    public static uint ChangedRecharge(uint rechargeMs, int deltaMs)
        => deltaMs >= 0
            ? (uint)Math.Min(uint.MaxValue, (long)rechargeMs + deltaMs)
            : (uint)Math.Max(0, (long)rechargeMs + deltaMs);
}
