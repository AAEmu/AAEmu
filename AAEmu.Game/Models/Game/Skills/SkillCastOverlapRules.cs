namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Hold-to-repeat and plot-only combo hits with a tiny custom GCD.
/// The client starts each hit. Instant follow-ups skip the 150 ms anti-spam
/// and must not cancel the in-flight plot.
/// </summary>
public static class SkillCastOverlapRules
{
    /// <summary>
    /// Instant plot hits with a tiny custom GCD (0 ms / 10 ms class).
    /// </summary>
    public static bool IsInstantComboHit(int castingTime, int customGcd) =>
        castingTime <= 0 && customGcd > 0 && customGcd <= 50;

    /// <summary>
    /// Combo hits skip the shared SkillLastUsed anti-spam. They still honor the
    /// shared GCD (first hit 1000 ms, follow-ups 10 ms). They must not write
    /// SkillLastUsed, or the next parent press is blocked.
    /// </summary>
    public static bool BypassesSharedCastGate(int castingTime, int customGcd) =>
        IsInstantComboHit(castingTime, customGcd);

    /// <summary>
    /// Each hit arms its own custom_gcd. Follow-ups are 10 ms — the reduced
    /// combo GCD, not a second 1000 ms lock. A skill that only declares
    /// <c>default_gcd</c> still arms the server default: 29054 skills carry
    /// custom_gcd 0 with default_gcd set.
    /// </summary>
    public static bool ArmsSharedGlobalCooldown(int castingTime, int customGcd, bool defaultGcd) =>
        customGcd > 0 || defaultGcd;

    /// <summary>
    /// A new cast takes over a plot that is still winding up, or one that is waiting on a channel.
    /// A plot that has already fired keeps running: its projectile is in the air and its cooldown
    /// is already armed, and the next skill must not erase the hit.
    /// </summary>
    public static bool PreviousPlotMustYield(bool isCasting, bool hasFired, bool waitingOnChannel)
    {
        if (waitingOnChannel)
            return true;
        return isCasting && !hasFired;
    }

    /// <summary>
    /// A new plot cancels a busy one, except combo hits and a repeat of the same skill
    /// (hold / GCD next). Those must finish their own Fired/Damaged.
    /// Fishing hold swap still goes through <paramref name="sportFishWouldCancel"/>.
    /// </summary>
    /// <param name="incomingIsComboFollowUpOfPrevious">
    /// True when the new skill is the Combo next-hit of the busy plot. Instant-combo only
    /// covers custom_gcd ≤ 50; longer Combo links still must not cancel their parent.
    /// </param>
    public static bool ShouldCancelPreviousPlot(
        bool previousWasBusy,
        bool incomingIsInstantCombo,
        bool incomingSameSkill,
        bool sportFishWouldCancel,
        bool previousIsComboFollowUpOfIncoming = false,
        bool incomingIsComboFollowUpOfPrevious = false)
    {
        if (incomingIsInstantCombo || incomingSameSkill || previousIsComboFollowUpOfIncoming ||
            incomingIsComboFollowUpOfPrevious)
            return false;
        return sportFishWouldCancel;
    }
}
