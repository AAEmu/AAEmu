using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Ceiling rule for multiple-stack buff families (testable, no side effects).
/// </summary>
/// <remarks>
/// A family that stacks lives as one instance carrying a count, because the client draws an icon per
/// instance and takes the number shown on it from the stack field of the wire. An instance per
/// application therefore paints a grid of identical icons that all report the same total, which is
/// what a two-sail hull showed: roughly sixty copies of its sail buff instead of one per sail.
/// <para>
/// The per-caster rules (<see cref="BuffStackRule.Independent"/>, <see cref="BuffStackRule.Multiple"/>
/// and <see cref="BuffStackRule.MultipleDecreaseOne"/>) narrow that to one instance per
/// <em>caster</em>: a second caster gets a second instance and therefore a second icon, which is what
/// "independent" and "multiple" are for, while one caster's repeat applications still collapse into a
/// single instance rather than one icon per application. <see cref="WireStack"/> is what keeps the two
/// honest — a per-caster instance reports the applications it represents, never the family total, or
/// every icon of the family would claim the others' stacks.
/// </para>
/// </remarks>
public static class BuffStackRules
{
    /// <summary>
    /// Whether another application fits into an instance already carrying <paramref name="currentStack"/>.
    /// </summary>
    /// <param name="currentStack">Applications the live instance already represents.</param>
    /// <param name="maxStack">The template ceiling. Zero or one means the family does not stack.</param>
    public static bool CanGrow(int currentStack, int maxStack) =>
        maxStack > 1 && currentStack < maxStack;

    /// <summary>
    /// A family with <paramref name="transformBuffId"/> replaces itself once the live count
    /// reaches the ceiling (tension 5793 → line-broken 5794 at 20).
    /// </summary>
    public static bool ShouldTransform(int stack, int maxStack, uint transformBuffId) =>
        transformBuffId != 0 && maxStack > 1 && stack >= maxStack;

    /// <summary>
    /// Flat or linear-level modifier after the instance's stack is applied.
    /// </summary>
    /// <remarks>
    /// One instance carries the whole family, so a single +6 row at sixty stacks is +360, not a
    /// one-shot +36% dumped on the first application. Stack 0 is treated as 1 (the wire never
    /// claims zero applications).
    /// </remarks>
    public static long ScaledModifier(long value, float linearLevelBonus, uint abLevel, int stack)
    {
        var n = Math.Max(1, stack);
        return (long)Math.Round((value + linearLevelBonus * (abLevel / 100f)) * n);
    }

    /// <summary>
    /// A duration-0 family has no expire timer. Refresh must not replace the live
    /// instance: <c>OverwriteWith</c> used to schedule a dispel from
    /// <c>GetTimeLeft() == -1</c>, which is "already due", so the buff vanished
    /// on the second apply. Fishing 4053 (pose / anim action) is that family —
    /// auto-reuse re-applies it and the throw then starts from default idle.
    /// </summary>
    public static bool ShouldOverwriteOnRefresh(int incomingDurationMs, int existingDurationMs) =>
        incomingDurationMs > 0 || existingDurationMs > 0;

    /// <summary>
    /// Only timed or ticking buffs get a dispel task. Permanent (duration 0, no
    /// tick) must not be queued at -1 ms or they finish on the next tick.
    /// </summary>
    public static bool ShouldScheduleDispel(int durationMs, double tickMs) =>
        durationMs > 0 || tickMs > 0;

    /// <summary>
    /// Whether a live instance of this rule belongs to the caster that applied it, so two casters keep
    /// two instances side by side.
    /// </summary>
    /// <remarks>
    /// <see cref="BuffStackRule.Independent"/> (9,942 rows): 9,527 of them author <c>max_stack</c> 1, so
    /// the family cannot accumulate; what it is for is two casters' copies of the same effect — the
    /// ticking heals 6286 체력 회복, 24478 하얀 숲의 치유, 29209 투코의 치유, and the debuffs 28645 동결 /
    /// 24032 동상 — each of which used to collapse onto whichever instance arrived first.
    /// <see cref="BuffStackRule.Multiple"/> (729 rows) and <see cref="BuffStackRule.MultipleDecreaseOne"/>
    /// (28 rows) are keyed the same way; see those members for what differs.
    /// </remarks>
    public static bool IsCasterScoped(BuffStackRule rule) =>
        rule is BuffStackRule.Independent or BuffStackRule.Multiple or BuffStackRule.MultipleDecreaseOne;

    /// <summary>
    /// Whether a caster's repeat application adds to the count its live instance carries, rather than
    /// replacing that instance. Only <see cref="BuffStackRule.Multiple"/> accumulates: its ceilings run
    /// to 10,000 (25024 따뜻한 히라마 스튜 재료, 25389 범람하는 영혼), 9,999 (24702 향연수호전 자원사용함), 600
    /// (24701 저승 공헌도) and 500 (29594 고대 유적의 촛불) on counter families, and to 60 on the
    /// permanent sail-wind family (20860 해풍 응용). Those are counts on one icon, not that many live
    /// instances — one instance per application would put up to ten thousand Buff records and icons on
    /// the unit, which is the grid the remarks above forbid.
    /// </summary>
    public static bool AccumulatesApplications(BuffStackRule rule) =>
        rule == BuffStackRule.Multiple;

    /// <summary>
    /// Whether every application gets its own live instance, so an expiry takes exactly one of them.
    /// </summary>
    /// <remarks>
    /// <see cref="BuffStackRule.MultipleDecreaseOne"/> is the rule whose name says what it does: several
    /// instances (25041 바이라바 증폭의 불씨 250, 25866 칼리디스 다후타의 저주 100, 28644 동상 10,
    /// 32704 생산력 2), each with its own timer, so they fall off one at a time. Contrast
    /// <see cref="BuffStackRule.Multiple"/>: same name prefix, but its applications share one timer and
    /// end together.
    /// </remarks>
    public static bool IsInstancePerApplication(BuffStackRule rule) =>
        rule == BuffStackRule.MultipleDecreaseOne;

    /// <summary>
    /// Ceiling on how many live instances one caster may hold, from the template's <c>max_stack</c>.
    /// </summary>
    /// <remarks>
    /// Every buff of rules 3-7 authors a positive <c>max_stack</c>; a missing or zero one is read as a
    /// single instance rather than none, or the application would be dropped instead of landing.
    /// </remarks>
    public static int InstanceCeiling(int maxStack) => Math.Max(1, maxStack);

    /// <summary>
    /// Whether one more application fits under the instance ceiling.
    /// </summary>
    public static bool CanAddInstance(int liveInstances, int maxStack) =>
        liveInstances < InstanceCeiling(maxStack);

    /// <summary>
    /// <see cref="BuffStackRule.Extend"/>: the incoming application adds its own duration to what is
    /// left of the live instance instead of replacing it (that is what
    /// <see cref="BuffStackRule.Refresh"/> does).
    /// </summary>
    /// <remarks>
    /// <paramref name="remainingMs"/> comes from <c>Buff.GetTimeLeft()</c>, which answers -1 for a
    /// permanent instance (duration 0). Adding that sentinel would shorten the buff by a millisecond,
    /// so it is floored at zero — an expired or permanent instance contributes nothing and the incoming
    /// duration stands on its own. Callers still gate the overwrite on
    /// <see cref="ShouldOverwriteOnRefresh"/> so a permanent instance is never replaced at all.
    /// </remarks>
    public static int ExtendedDuration(int incomingDurationMs, double remainingMs) =>
        incomingDurationMs + (int)Math.Max(0, remainingMs);

    /// <summary>
    /// <see cref="BuffStackRule.ChargeExtend"/>: the incoming charge is summed into the live instance's
    /// charge and the total is held at the template's <c>max_charge</c>.
    /// </summary>
    /// <remarks>
    /// 35 of the 39 charge_extend rows author a ceiling — 864 근성 5,000, 22574 보호막 20,000, 899 누적
    /// 피해 1,000 — so the sum is clamped there rather than left unbounded. The four without one (35
    /// 차원의 틈, 767 충전테스트, 23473 무모함, 26828 느려짐) ship <c>max_charge</c> 0, which is "no
    /// ceiling authored", not "a ceiling of zero"; clamping those would throw the incoming charge away.
    /// </remarks>
    public static int SummedCharge(int liveCharge, int incomingCharge, int maxCharge)
    {
        var sum = Math.Max(0, liveCharge) + Math.Max(0, incomingCharge);
        return maxCharge > 0 ? Math.Min(sum, maxCharge) : sum;
    }

    /// <summary>
    /// The number both wire forms report for one live instance: the applications that instance
    /// represents when instances are per caster, and the family total for the one-instance rules.
    /// </summary>
    /// <remarks>
    /// Instances share a <c>buffId</c>, so the family total is the same on every one of them. That is
    /// right for a family that lives as one instance, and wrong the moment two casters hold their own
    /// copies: two independent DoTs would both print the other's stacks on their icon, which is exactly
    /// the "grid of identical icons that all report the same total" the remarks describe.
    /// </remarks>
    public static uint WireStack(BuffStackRule rule, int instanceStack, int familyTotal) =>
        IsCasterScoped(rule)
            ? (uint)Math.Max(1, instanceStack)
            : (uint)Math.Max(1, familyTotal);
}
