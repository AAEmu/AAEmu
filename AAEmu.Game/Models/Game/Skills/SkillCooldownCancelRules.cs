using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whether a cancelled cast should clear the skill / GCD timers that were armed at accept.
/// </summary>
/// <remarks>
/// Cast-time and plot-only skills arm their cooldown (and often the shared GCD) when the cast is
/// accepted so the client grey bar matches the server gate. A voluntary stop or interrupt before
/// the cast commits must clear those timers — otherwise the client shows casting stopped while the
/// skill stays unusable for the full cooldown.
///
/// A plot that ends on its own without committing (Bladefall's range gate after accept) must refund
/// the same way: the press armed the skill CD, the blades never left, and a later cast's cooldown
/// snapshot was what finally greyed the icon.
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
    /// A plot that never committed refunds whether it was cancelled or simply ran out of edges
    /// (range / combat-resource fail with no fail child). Once committed, cancel keeps the timers.
    /// </summary>
    public static bool ShouldRefundPlotEnd(bool hasFired) => !hasFired;

    /// <summary>
    /// A cancelled plot refunds only while it has not committed. Once a node has applied an effect,
    /// finished its cast, or entered a channel, the cooldown stays with that cast.
    /// </summary>
    public static bool ShouldRefundCancelledPlot(bool hasFired) => ShouldRefundPlotEnd(hasFired);

    /// <summary>
    /// <see cref="Skill.Stop"/> refunds only while neither a plot nor a non-plot cast has committed.
    /// A no-plot channel whose <c>Cast()</c> already fired must keep its cooldown.
    /// </summary>
    public static bool ShouldRefundOnStop(bool plotHasFired, bool castCommitted) =>
        !plotHasFired && !castCommitted;

    /// <summary>
    /// Setup-only specials on a plot start node (GCD, auto-attack arm, stealth cancel, SetVariable)
    /// must not lock the skill CD. Bladefall starts with those, then a Range gate can still fail.
    /// </summary>
    public static bool IsSetupOnlySpecial(SpecialType type) =>
        type is SpecialType.GlobalCooldown
            or SpecialType.AutoAttack
            or SpecialType.CancelStealth
            or SpecialType.CancelOngoingBuff
            or SpecialType.SetVariable;

    /// <summary>
    /// Whether this plot effect counts toward <see cref="PlotCommitted"/>'s applied-effects flag.
    /// Non-special effects (damage, buff, controller, …) always count; specials use
    /// <see cref="IsSetupOnlySpecial"/>.
    /// </summary>
    public static bool CountsTowardPlotCommit(string actualType, SpecialType? specialType)
    {
        if (string.IsNullOrEmpty(actualType))
            return false;
        if (!string.Equals(actualType, "SpecialEffect", StringComparison.Ordinal))
            return true;
        return specialType is { } type && !IsSetupOnlySpecial(type);
    }

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
