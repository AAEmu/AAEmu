namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Pure cooldown reduction arithmetic shared by the gameplay effect path.
/// </summary>
public static class CooldownReductionRules
{
    /// <summary>
    /// Selects the authored percent branch when present, otherwise the flat branch. Percent is
    /// calculated from the original duration. A negative authored reduction extends the remaining
    /// time, bounded by the original duration.
    /// </summary>
    public static TimeSpan CalculateRemaining(TimeSpan originalDuration, TimeSpan remaining,
        int flatMilliseconds, int percent)
    {
        if (originalDuration <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var originalMilliseconds = originalDuration.TotalMilliseconds;
        var currentMilliseconds = Math.Clamp(remaining.TotalMilliseconds, 0d, originalMilliseconds);
        var requestedReduction = percent != 0
            ? originalMilliseconds * percent / 100d
            : flatMilliseconds;
        var remainingAfterReduction = Math.Clamp(
            currentMilliseconds - requestedReduction,
            0d,
            originalMilliseconds);

        return TimeSpan.FromMilliseconds(remainingAfterReduction);
    }
}
