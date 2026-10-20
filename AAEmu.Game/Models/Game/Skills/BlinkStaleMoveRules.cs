namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// After a soft blink (<c>SCBlinkUnit</c>), the client can still deliver <c>CSMoveUnit</c> from the
/// cast stand. Those must not rewrite Transform — but the hop itself stays a blink (no teleport /
/// load screen).
/// </summary>
public static class BlinkStaleMoveRules
{
    /// <summary>
    /// How long after the blink to drop moves that are still far from the landing.
    /// Cast-time skills (Grasping Void ~1.2 s) keep sending stand CSMoveUnit after the hop;
    /// a short window lets the zone get those stand reports and undo WZBlink.
    /// </summary>
    public static readonly TimeSpan SuppressWindow = TimeSpan.FromMilliseconds(2500);

    /// <summary>Client landing jitter / first post-hop step counts as arrived.</summary>
    public const float AcceptRadiusMetres = 8f;

    public static bool IsStaleAwayFromLanding(
        float moveX, float moveY,
        float landX, float landY,
        float acceptRadiusMetres = AcceptRadiusMetres)
    {
        var dx = moveX - landX;
        var dy = moveY - landY;
        var r = acceptRadiusMetres;
        return dx * dx + dy * dy > r * r;
    }

    /// <summary>
    /// Picks the move XY that must be compared to a world-space blink landing.
    /// Carrier-local packets already converted by the caller pass <paramref name="worldMoveX"/> /
    /// <paramref name="worldMoveY"/>; otherwise the raw packet XY is world.
    /// </summary>
    public static (float x, float y) MoveXyForLandingCompare(
        float packetX, float packetY,
        bool positionIsCarrierLocal,
        float worldMoveX, float worldMoveY)
        => positionIsCarrierLocal ? (worldMoveX, worldMoveY) : (packetX, packetY);

    public static bool ShouldDrop(
        DateTime utcNow,
        DateTime suppressUntilUtc,
        float moveX, float moveY,
        float landX, float landY,
        float acceptRadiusMetres = AcceptRadiusMetres)
    {
        if (utcNow >= suppressUntilUtc)
            return false;
        return IsStaleAwayFromLanding(moveX, moveY, landX, landY, acceptRadiusMetres);
    }
}
