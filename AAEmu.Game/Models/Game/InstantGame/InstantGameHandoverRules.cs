namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// When a dungeon copy is handed over to the client.
/// </summary>
/// <remarks>
/// <para>
/// A copy is handed over with the instant-game packets that move the client into it: joined for a matched
/// player whose copy opens with a ready window, otherwise <c>SCInstantGameReentryPacket</c> (0x1E7). On
/// those the client raises the UI events that take down the queue's standby button and show the standby
/// banner or the instance HUD — but the client drops UI events raised while its loading screen is up, so a
/// hand-over during the load changes the client's state and leaves its UI exactly as it was.
/// </para>
/// <para>
/// The instance load therefore only marks the hand-over as owed, and it is sent in answer to the client's
/// re-entry check, which the client sends once it has left the loading screen. That check also follows
/// every later loading screen and the hand-over's own events, so the owed flag is consumed by the first
/// answer and nothing is sent outside a copy.
/// </para>
/// </remarks>
public static class InstantGameHandoverRules
{
    /// <summary>
    /// True when a re-entry check has to be answered with the copy's hand-over: one is still owed from
    /// the last instance load, and the character is still inside a dungeon copy.
    /// </summary>
    public static bool ShouldHandOverOnReentryCheck(bool handoverPending, bool insideDungeonCopy) =>
        handoverPending && insideDungeonCopy;

    /// <summary>
    /// True when the instance-load packet may unlock movement and stream the neighbourhood.
    /// A dungeon copy's loading screen is still up then: the client can start falling through a
    /// cached floor before collision exists, and the server would accept those moves. Arrival
    /// waits for the re-entry check that follows the closed loading screen.
    /// Leave-to-overworld and non-copy loads still finish here.
    /// </summary>
    public static bool CompletesArrivalOnInstanceLoaded(bool insideDungeonCopy) =>
        !insideDungeonCopy;

    /// <summary>
    /// True when the re-entry check has to finish the arrival the instance load left locked:
    /// the character is still inside a copy and movement is still refused.
    /// </summary>
    public static bool CompletesArrivalOnReentryCheck(bool positionStillLocked, bool insideDungeonCopy) =>
        positionStillLocked && insideDungeonCopy;
}
