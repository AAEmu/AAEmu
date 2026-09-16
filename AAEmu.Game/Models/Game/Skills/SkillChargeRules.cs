namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Per-skill charges: <c>skills.charge_count</c> is how many times the skill can be used before it has
/// to wait, and <c>charge_cooldown_time</c> is how long one charge takes to come back.
/// </summary>
/// <remarks>
/// 26 shipped skills carry charges; the family spans both ends of the scale. 11368 매의 발톱 has 2
/// charges on an 8,000 ms recharge, 13281 다발 사격 has 5 on 22,000 ms with no plain cooldown at all
/// (<c>cooldown_time</c> 0), and the four 3-charge cooldown-less 86,400,000 ms skills (44677 영구동토,
/// 44702 피의 복수, 44713 시간의 고치, 44727 용수바람) are once-a-day abilities — which is also why
/// 166 <c>change_charge_skill_count</c> hands those four back three charges at a time and
/// 167 <c>change_charge_cooldown</c> shaves 3,000 ms off 38893 빛의 사격's recharge.
///
/// The pool is exact-count rather than a timer standing in for a count: the second charge of a
/// 2-charge skill is usable the instant the first cast finishes, and only the last one spent arms the
/// skill's own <c>cooldown_time</c>. <c>Recharge</c> is written to be evaluated lazily against a
/// supplied clock, so nothing has to tick in the background and a despawned unit cannot leak a timer.
/// </remarks>
public static class SkillChargeRules
{
    /// <summary>
    /// One skill's charge pool. <see cref="NextRechargeUtc"/> is when the next missing charge returns;
    /// <see cref="DateTime.MaxValue"/> means no recharge is scheduled (full pool, or a skill that
    /// declares no <c>charge_cooldown_time</c>).
    /// </summary>
    public readonly record struct ChargeState(int Max, int Current, DateTime NextRechargeUtc);

    /// <summary>A skill the unit has never used starts with every charge available.</summary>
    public static ChargeState Initial(int maxCharges)
        => new(maxCharges, maxCharges, DateTime.MaxValue);

    /// <summary>
    /// Returns one charge per elapsed <paramref name="rechargeTime"/> interval and reports how many came
    /// back, so a caller can tell the client. A pool that declares no recharge interval never refills.
    /// </summary>
    public static ChargeState Recharge(ChargeState state, int rechargeTime, DateTime now, out int granted)
    {
        granted = 0;
        if (state.Max <= 0 || state.Current >= state.Max || rechargeTime <= 0)
            return state with { NextRechargeUtc = DateTime.MaxValue };

        var current = state.Current;
        var next = state.NextRechargeUtc;

        // A pool that was seeded with charges but never spent one has no clock yet.
        if (next == DateTime.MaxValue)
            return state;

        while (current < state.Max && now >= next)
        {
            current++;
            granted++;
            next = next.AddMilliseconds(rechargeTime);
        }

        return new ChargeState(state.Max, current, current >= state.Max ? DateTime.MaxValue : next);
    }

    /// <summary>
    /// Spends one charge. The recharge clock only starts when the pool was full, so charges return one
    /// per interval from the moment the first one is used rather than all at once at the end.
    /// </summary>
    public static ChargeState Consume(ChargeState state, int rechargeTime, DateTime now)
    {
        if (state.Current <= 0)
            return state;

        var next = state.NextRechargeUtc;
        if (state.Current >= state.Max && rechargeTime > 0)
            next = now.AddMilliseconds(rechargeTime);

        return new ChargeState(state.Max, state.Current - 1, next);
    }

    /// <summary>
    /// Whether the cast has to arm the skill's own cooldown. While the pool still holds a charge the
    /// skill is immediately usable again — that is what the charges are for.
    /// </summary>
    public static bool ArmsCooldownAfterCast(ChargeState afterCast) => afterCast.Current <= 0;

    /// <summary>
    /// Applies special effect 166 <c>change_charge_skill_count</c>: <paramref name="delta"/> charges on
    /// top of what is left, clamped into 0..<paramref name="maxCharges"/>. A pool refilled to the top
    /// loses its recharge clock.
    /// </summary>
    public static ChargeState ChangeCount(ChargeState state, int maxCharges, int delta)
    {
        var current = Math.Clamp(state.Current + delta, 0, maxCharges);
        return new ChargeState(maxCharges, current, current >= maxCharges ? DateTime.MaxValue : state.NextRechargeUtc);
    }

    /// <summary>
    /// Applies special effect 167 <c>change_charge_cooldown</c>: a signed offset on the running recharge
    /// timer. A negative offset that reaches into the past leaves the charge due now, which
    /// <see cref="Recharge"/> then grants on the next read.
    /// </summary>
    public static ChargeState ChangeRechargeTime(ChargeState state, int deltaMilliseconds, DateTime now)
    {
        if (state.NextRechargeUtc == DateTime.MaxValue)
            return state;

        var shifted = state.NextRechargeUtc.AddMilliseconds(deltaMilliseconds);
        return state with { NextRechargeUtc = shifted < now ? now : shifted };
    }

    /// <summary>
    /// Applies special effect 158 <c>charge_cooldown</c>: (re)starts the recharge timer with a stated
    /// interval, for a pool that is missing at least one charge.
    /// </summary>
    public static ChargeState RestartRecharge(ChargeState state, int rechargeTimeMilliseconds, DateTime now)
    {
        if (state.Max <= 0 || state.Current >= state.Max || rechargeTimeMilliseconds <= 0)
            return state;

        return state with { NextRechargeUtc = now.AddMilliseconds(rechargeTimeMilliseconds) };
    }
}
