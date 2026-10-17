namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whether a cancelled cast should clear the skill / GCD timers that were armed at accept.
/// </summary>
/// <remarks>
/// Cast-time and plot-only skills arm their cooldown (and often the shared GCD) when the cast is
/// accepted so the client grey bar matches the server gate. A voluntary stop or interrupt before
/// the cast commits must clear those timers — otherwise the client shows casting stopped while the
/// skill stays unusable for the full cooldown.
/// </remarks>
public static class SkillCooldownCancelRules
{
    /// <summary>
    /// True when this cast armed a skill cooldown and/or the shared GCD at accept and the cast is
    /// being cancelled, so those clocks should be refunded.
    /// </summary>
    public static bool ShouldRefundOnCancel(bool skillCooldownArmed, bool gcdArmed) =>
        skillCooldownArmed || gcdArmed;
}
