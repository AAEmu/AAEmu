namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// When ship simulation is handed to a zone, and when that hand-over is still valid by the time it is
/// sent. See <see cref="AAEmu.Game.Core.Managers.SlaveManager.EnableBoatSimInZone"/>.
/// </summary>
/// <remarks>
/// Simulation stays on for the life of the hull. A zone change creates and arms the new dedicate
/// while the old one is still simulating, then drops the old unit without sending a sim-off.
/// </remarks>
public static class BoatZoneSimRules
{
    /// <param name="zoneKey">Zone that is taking the hull.</param>
    /// <param name="armedFor">Zone whose simulation is already live; 0 when none.</param>
    /// <returns>False when that zone already owns the live simulation, so mounting the helm re-sends nothing.</returns>
    public static bool ShouldArm(uint zoneKey, uint armedFor) => zoneKey != 0 && zoneKey != armedFor;

    /// <summary>
    /// The previous dedicate keeps simulating until World starts following the new one, so the hull
    /// does not freeze at the seam.
    /// </summary>
    public static bool ShouldOverlapOldSim(uint oldZone, uint newZone) =>
        oldZone != 0 && newZone != 0 && oldZone != newZone;

    /// <summary>
    /// True when <paramref name="sourceZone"/> is the newly armed simulator World is not following yet.
    /// Its first reports are that zone's outbound type-4 for a body the arm-time flush has not
    /// settled yet — not a measurement of the followed hull — and must not be streamed to clients.
    /// </summary>
    public static bool IsWarmupSource(uint sourceZone, uint announcedTo, uint pendingFor) =>
        pendingFor != 0 && sourceZone == pendingFor && sourceZone != announcedTo;

    /// <summary>
    /// Create does not place the ship rigid body. A type-4 pose is accepted only after that body
    /// exists, and helm-on after that. Two TaskManager ticks (50 ms each) is the wait after Create
    /// for first summon and for a seam — overlap keeps the old simulator running in the meantime.
    /// </summary>
    public static readonly TimeSpan FirstSummonSimArmDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The dedicate treats hulls with X or Y below this as off the map (origin after an unplaced Create).
    /// </summary>
    public const float ShipWorldEdgeMetres = 16f;

    /// <summary>
    /// True when this arm follows a Create (first summon or seam). Helm remount does not reach here.
    /// </summary>
    public static bool ShouldDeferSimArm(uint announcedTo, uint zoneKey) =>
        zoneKey != 0 && announcedTo != 0;

    /// <summary>
    /// Tuned age a post-arm report must reach before World trusts it as a body velocity. The
    /// arm-time flush applies the seed to the body once; reports younger than this are the new
    /// zone's outbound type-4 for a body still settling. Empirical — the dedicate's
    /// net_ship_controller_smooth_time cvar has no loader in the shipped binary and drives nothing.
    /// </summary>
    public const int WarmupPoseMinAgeMs = 1000;

    /// <summary>
    /// A warmup pose is usable only when the body is inside the world and, if the hull crossed
    /// under way, the report is a real body sample — not the new zone's outbound type-4 for an
    /// unconsumed or at-rest body (those sit at 0–0.2 m/s). Origin stays rejected. While the
    /// flush transient is still settling, keep the old simulator unless the new body has already
    /// reached cruise.
    /// </summary>
    public static bool ShouldAcceptWarmupHandoff(
        float x, float y, float reportedSpeed, float expectedCruiseSpeed, long elapsedMs)
    {
        if (!IsInsideShipWorld(x, y))
            return false;
        if (expectedCruiseSpeed <= 0f)
            return true;
        if (reportedSpeed < BoatSeamImpulse.MinCruiseSpeed)
            return false;
        if (reportedSpeed + BoatSeamImpulse.MinCorrectionDeficit >= expectedCruiseSpeed)
            return true;
        return elapsedMs >= WarmupPoseMinAgeMs;
    }

    public static bool IsInsideShipWorld(float x, float y) =>
        x >= ShipWorldEdgeMetres && y >= ShipWorldEdgeMetres;

    /// <param name="zoneKey">Zone the hand-over was scheduled for.</param>
    /// <param name="announcedTo">Zone World still follows (the live simulator during an overlap).</param>
    /// <param name="pendingFor">Zone waiting for the delayed enable.</param>
    /// <returns>
    /// False when the hull was withdrawn, or a later seam won the pending slot, so a stale enable
    /// never reaches a zone that no longer has the hull.
    /// </returns>
    public static bool ShouldSendEnable(uint zoneKey, uint announcedTo, uint pendingFor) =>
        zoneKey != 0 && announcedTo != 0 && pendingFor == zoneKey;

    /// <summary>
    /// A create was sent to <paramref name="pendingFor"/> and then the hull sailed somewhere else
    /// (or back) before that zone was armed — that in-between dedicate must drop the ghost hull.
    /// </summary>
    public static bool ShouldDropStalePending(uint pendingFor, uint liveZone, uint newZone) =>
        pendingFor != 0 && pendingFor != liveZone && pendingFor != newZone;
}
