namespace AAEmu.Game.Models.Game.Indun.Matching;

/// <summary>Pure timing / fill rules for Indun H-window matchmaking (testable).</summary>
public static class IndunMatchReadyRules
{
    public static bool IsQueueExpired(DateTime appliedAt, DateTime now, uint applyWaitingTimeMs)
    {
        if (applyWaitingTimeMs == 0)
            return false;
        return (now - appliedAt).TotalMilliseconds >= applyWaitingTimeMs;
    }

    public static bool IsQueueReady(DateTime oldestAppliedAt, DateTime now, int applicantCount, uint maxPlayers,
        uint minMatchingTimeMs)
    {
        if (applicantCount <= 0)
            return false;
        if (maxPlayers > 0 && applicantCount >= maxPlayers)
            return true;
        return (now - oldestAppliedAt).TotalMilliseconds >= minMatchingTimeMs;
    }

    /// <summary>
    /// How long a match may wait for its instance copy to finish building before it is given up on.
    /// Longer than the zone host ready timeout so a failing host reports itself first and this stays
    /// the backstop for a copy that never answers either way.
    /// </summary>
    public const uint PrepareTimeoutMs = 150_000;

    public static bool IsPrepareExpired(DateTime preparingSince, DateTime now) =>
        (now - preparingSince).TotalMilliseconds >= PrepareTimeoutMs;

    /// <summary>
    /// What to do with a match whose instance copy is being built. Players hold on the registered
    /// screen until the copy answers, so that the offer they get can be entered without a wait.
    /// </summary>
    public static IndunPrepareOutcome NextAfterPreparing(bool instanceReady,
        MatchingInvitationType invitationType, DateTime preparingSince, DateTime now)
    {
        if (!instanceReady)
            return IsPrepareExpired(preparingSince, now)
                ? IndunPrepareOutcome.GiveUp
                : IndunPrepareOutcome.KeepWaiting;

        return invitationType == MatchingInvitationType.Direct
            ? IndunPrepareOutcome.Enter
            : IndunPrepareOutcome.Offer;
    }

    public static bool IsInviteExpired(DateTime inviteOpenedAt, DateTime now, uint cleanupTermMs)
    {
        if (cleanupTermMs == 0)
            return false;
        return (now - inviteOpenedAt).TotalMilliseconds >= cleanupTermMs;
    }

    public static bool AllActiveAccepted(IReadOnlyList<IndunMatchApplicant> members)
    {
        var active = members.Where(m => !m.Declined).ToList();
        return active.Count > 0 && active.All(m => m.Accepted);
    }

    public static int AcceptedCount(IReadOnlyList<IndunMatchApplicant> members) =>
        members.Count(m => m.Accepted && !m.Declined);

    /// <summary>
    /// Whether a freshly built prepared copy may be published onto the session. Concurrent withdraw
    /// can finish the session while <c>PrepareInstance</c> is still running; attaching the copy
    /// after that orphans world/ZoneHost capacity with no session left to discard it.
    /// </summary>
    public static bool CanPublishPrepared(IndunMatchPhase phase, bool sessionStillRegistered) =>
        phase == IndunMatchPhase.Preparing && sessionStillRegistered;
}
