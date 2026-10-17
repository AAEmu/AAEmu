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

    /// <summary>
    /// A cancelled plot refunds only while it has not committed. Once a node has applied an effect,
    /// finished its cast, or entered a channel, the cooldown stays with that cast.
    /// </summary>
    public static bool ShouldRefundCancelledPlot(bool hasFired) => !hasFired;

    /// <summary>
    /// True when this node is the point a later cancel must not refund: the cast's fire moment,
    /// a channel edge, or any node that applied effects without opening a further cast bar.
    /// A cast-bar opener may carry setup effects and still refund if the player stops during the bar.
    /// </summary>
    public static bool PlotCommitted(bool appliedEffects, bool openedCastBar, bool fireMoment, bool enteredChannel) =>
        fireMoment || enteredChannel || (appliedEffects && !openedCastBar);

    /// <summary>
    /// The cancelled cast may clear a shared timer only when the token it wrote is still the current one.
    /// A later cast replaces the token; the older cancel must leave that newer timer alone.
    /// </summary>
    public static bool StillOwnsCooldown(long armedToken, long currentToken) =>
        armedToken != 0 && armedToken == currentToken;
}
