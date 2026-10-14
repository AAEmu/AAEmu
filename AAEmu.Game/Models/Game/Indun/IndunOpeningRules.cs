namespace AAEmu.Game.Models.Game.Indun;

/// <summary>How far a matched player has been walked through a copy's opening.</summary>
public enum IndunOpeningStage
{
    /// <summary>Told it has joined: the client shows its standby banner.</summary>
    Joined,
    /// <summary>Told the copy is ready: the client counts the copy's ready window down ("Wait time").</summary>
    Ready,
    /// <summary>Told the copy has started: the client shows the instance HUD. Nothing is left to send.</summary>
    Started
}

/// <summary>What one opening step sends, and the stage the player is in afterwards.</summary>
public readonly record struct IndunOpeningStep(bool SendReady, bool SendStart, IndunOpeningStage Stage);

/// <summary>
/// The opening a matched player is walked through on arriving in a copy with a ready window: joined
/// (standby), ready (the wait countdown), start (the HUD). Each packet is accepted by the client only from
/// the stage before it, so the order is fixed and a step is never skipped.
/// </summary>
public static class IndunOpeningRules
{
    /// <summary>
    /// Whether a copy opens with that ceremony: only one whose <c>indun_zones.option</c> carries a ready
    /// window has a wait for the client to show. Any other copy is handed over as already running.
    /// </summary>
    public static bool UsesOpening(IndunZoneOption option) => option.ReadySeconds > 0;

    /// <summary>
    /// The step for a player at <paramref name="stage"/> while the copy is in <paramref name="phase"/>.
    /// A joined player is told the copy is ready on the next step, so the standby banner is seen before the
    /// countdown replaces it; a player who joined after the ready window has passed is also started at once.
    /// A ready player is started when the ready window is over.
    /// </summary>
    public static IndunOpeningStep Next(IndunOpeningStage stage, IndunInstancePhase phase)
    {
        var readyOver = phase != IndunInstancePhase.Ready;
        return stage switch
        {
            IndunOpeningStage.Joined => readyOver
                ? new IndunOpeningStep(true, true, IndunOpeningStage.Started)
                : new IndunOpeningStep(true, false, IndunOpeningStage.Ready),
            IndunOpeningStage.Ready => readyOver
                ? new IndunOpeningStep(false, true, IndunOpeningStage.Started)
                : new IndunOpeningStep(false, false, IndunOpeningStage.Ready),
            _ => new IndunOpeningStep(false, false, IndunOpeningStage.Started)
        };
    }
}
