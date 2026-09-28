namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a client that asked to stop casting still has to be told, when the timeline it quoted is one this
/// server holds nothing for.
/// </summary>
/// <remarks>
/// The client names the timeline it is giving up in the stop request, and it keeps whatever it is showing
/// until the server closes that timeline. A stop for a cast the server already let go of - the graph ran
/// its course, the cast ended, the timeline id was recycled - therefore has to be answered with that
/// timeline's own end, or the client sits on a bar nothing will ever close and keeps asking to stop it.
/// </remarks>
public static class CastReleaseRules
{
    /// <summary>
    /// Whether the player still has a cast running on <paramref name="requestedTlId"/>.
    /// </summary>
    /// <remarks>
    /// A zero request names no timeline, and a running cast on a different timeline is a newer one the
    /// client is not asking about. The id is not compared against anything else: the request only ever
    /// concerns a cast the player pressed, and the only cast of the player's that can still be open is its
    /// own task.
    /// </remarks>
    public static bool ServerHoldsCast(ushort requestedTlId, ushort openTaskTlId) =>
        requestedTlId != 0 && requestedTlId == openTaskTlId;

    /// <summary>
    /// Whether the stop has to be answered with the end of a timeline the server is not holding.
    /// </summary>
    /// <remarks>
    /// Asked for a timeline the player has no running cast on: the cast is over as far as this server is
    /// concerned, so the client's end is the only thing left that can take it off that timeline. When the
    /// request names no timeline there is nothing to release.
    /// </remarks>
    public static bool ShouldReleaseOrphanedSkillTimeline(ushort requestedTlId, ushort openTaskTlId) =>
        requestedTlId != 0 && !ServerHoldsCast(requestedTlId, openTaskTlId);
}
