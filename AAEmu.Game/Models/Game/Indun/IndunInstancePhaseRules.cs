namespace AAEmu.Game.Models.Game.Indun;

/// <summary>The phase a scripted instance copy is in, from its <c>indun_zones.option</c> budget.</summary>
public enum IndunInstancePhase
{
    /// <summary>No phase budget (and no tower-def): a plain instance with no scripted clock.</summary>
    None,
    /// <summary>The <c>ready_time</c> countdown before the copy starts ("Wait time").</summary>
    Ready,
    /// <summary>The <c>play_time</c> main phase (zone group 130's "Until Dawn").</summary>
    Play,
    /// <summary>The <c>end_time</c> wrap-up window after the play phase.</summary>
    End,
    /// <summary>The whole budget has elapsed.</summary>
    Finished
}

/// <summary>
/// Pure clock for a scripted instance copy: which phase it is in and how many whole seconds that phase
/// has left, from the copy's start time and its <see cref="IndunZoneOption"/> budget. Times are seconds.
/// </summary>
/// <remarks>
/// A phase with a zero budget is skipped, so a copy with <c>ready_time = 0</c> starts straight in
/// <see cref="IndunInstancePhase.Play"/>. A copy with no budget at all is <see cref="IndunInstancePhase.None"/>
/// and is never scripted by this clock.
/// </remarks>
public static class IndunInstancePhaseRules
{
    public static IndunInstancePhase PhaseAt(IndunZoneOption option, DateTime startUtc, DateTime nowUtc)
    {
        var (phase, _) = StateAt(option, startUtc, nowUtc);
        return phase;
    }

    /// <summary>Whole seconds left in the current phase; 0 for <see cref="IndunInstancePhase.None"/> / <see cref="IndunInstancePhase.Finished"/>.</summary>
    public static int SecondsRemaining(IndunZoneOption option, DateTime startUtc, DateTime nowUtc)
    {
        var (_, remaining) = StateAt(option, startUtc, nowUtc);
        return remaining;
    }

    /// <summary>The phase and the whole seconds left in it, computed in one pass.</summary>
    public static (IndunInstancePhase Phase, int SecondsRemaining) StateAt(
        IndunZoneOption option, DateTime startUtc, DateTime nowUtc)
    {
        var ready = Math.Max(0, option.ReadySeconds);
        var play = Math.Max(0, option.PlaySeconds);
        var end = Math.Max(0, option.EndSeconds);
        if (ready + play + end <= 0)
            return (IndunInstancePhase.None, 0);

        var elapsed = (nowUtc - startUtc).TotalSeconds;
        if (elapsed < 0d)
            elapsed = 0d;

        var readyBoundary = (double)ready;
        var playBoundary = readyBoundary + play;
        var endBoundary = playBoundary + end;

        if (elapsed < readyBoundary)
            return (IndunInstancePhase.Ready, Remaining(readyBoundary - elapsed));
        if (elapsed < playBoundary)
            return (IndunInstancePhase.Play, Remaining(playBoundary - elapsed));
        if (elapsed < endBoundary)
            return (IndunInstancePhase.End, Remaining(endBoundary - elapsed));

        return (IndunInstancePhase.Finished, 0);
    }

    /// <summary>
    /// Whether a scripted copy's run may start now: its content is loaded, its run has not started yet, it
    /// carries a tower-def, and it has left the ready ("wait time") window. A copy with a ready budget
    /// keeps its players waiting that long before the script begins; a copy with no budget starts at once.
    /// </summary>
    /// <remarks>
    /// The ready window is what the copy's own "wait time" counts down. Starting the script at the end of
    /// it is what makes the wait meaningful — the first wave of an instance lands when the wait is over,
    /// not while players are still arriving.
    /// </remarks>
    public static bool ShouldStartScript(
        IndunZoneOption option, bool contentLoaded, bool alreadyStarted, DateTime startUtc, DateTime nowUtc)
    {
        if (option.TowerDefId == 0 || !contentLoaded || alreadyStarted)
            return false;

        return PhaseAt(option, startUtc, nowUtc)
            is IndunInstancePhase.None or IndunInstancePhase.Play or IndunInstancePhase.End;
    }

    /// <summary>
    /// The origin a copy's clock runs from: the moment its first player arrived inside it.
    /// </summary>
    /// <remarks>
    /// A copy nobody has entered has no clock at all, so nothing may run yet — no phase to report, no
    /// countdown to spend, and no script to start. Anchoring on the arrival and not on the copy's creation
    /// is what keeps an unaccepted entry invite from burning the copy's ready and play budgets while the
    /// instance sits empty.
    /// </remarks>
    public static bool TryGetClockOrigin(DateTime? firstArrivalUtc, out DateTime origin)
    {
        if (firstArrivalUtc is { } arrival)
        {
            origin = arrival;
            return true;
        }

        origin = default;
        return false;
    }

    private static int Remaining(double seconds) => (int)Math.Ceiling(seconds);
}
