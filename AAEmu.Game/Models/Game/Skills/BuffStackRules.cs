namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Ceiling rule for multiple-stack buff families (testable, no side effects).
/// </summary>
/// <remarks>
/// A family that stacks lives as one instance carrying a count, because the client draws an icon per
/// instance and takes the number shown on it from the stack field of the wire. An instance per
/// application therefore paints a grid of identical icons that all report the same total, which is
/// what a two-sail hull showed: roughly sixty copies of its sail buff instead of one per sail.
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
}
