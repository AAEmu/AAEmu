namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// A jury invitation and the chair promised when it is accepted are only good while the bench is
/// still gathering. The clients keep both dialogs on screen until they answer them, so the calls
/// outlive the phase by design; these rules say when one of them still counts.
/// </summary>
public static class TrialJuryCallRules
{
    /// <summary>True while the trial is still gathering jurors, the only phase a call is good in.</summary>
    public static bool IsCallLive(TrialState state) => state == TrialState.WaitingJury;

    /// <summary>
    /// True when a phase change ends the gathering window, which is when the pending invitations and
    /// promised chairs have to be forgotten: an unanswered call must not seat anyone later.
    /// </summary>
    public static bool ShouldDropPendingCalls(TrialState from, TrialState to) =>
        from == TrialState.WaitingJury && to != TrialState.WaitingJury;
}
