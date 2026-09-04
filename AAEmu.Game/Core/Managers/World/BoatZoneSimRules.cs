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
    /// Dropping A at Create forced the client onto a frozen plant (1 s stop) and the
    /// interpolator fight at 186→149. A stays until follow switches.
    /// </summary>
    public const bool DropOldAtTransfer = false;

    /// <summary>
    /// After helm-on on B, World still streams A at least this long so B can consume the
    /// seed and take a closed-loop shortfall. Follow then waits for B's cruise (or the
    /// fail-safe). 200 ms at cruise is ~3.6 m.
    /// </summary>
    public const int ReplantSettleMs = 200;

    /// <summary>
    /// Longest overlap when B stays short of cruise and A is still talking. Derived:
    /// settle plus the follow backstop.
    /// </summary>
    public static int OverlapFollowFailSafeMs => ReplantSettleMs + FollowBackstopMs;

    /// <summary>
    /// A has not published a type-4 for this long: treat it as silent (end of world or
    /// dropped) and switch to B even if the settle window is not done.
    /// </summary>
    public const int OldSimSilentMs = 200;

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
    /// How long after the closed-loop impulse World waits for the new body to publish the restored
    /// speed before follow may switch. The impulse is a velocity change; the next type-4 is what
    /// proves it landed.
    /// </summary>
    public const int ImpulseSettleMs = 200;

    /// <summary>
    /// If the incoming body reaches cruise but never the bridged plant (reversed helm, a
    /// body that walks away from the plant), follow anyway so the client is not stuck on
    /// the snapshot. Counted from the first behind-bridge cruise pose, not from arm.
    /// </summary>
    public const int FollowBackstopMs = 400;

    /// <summary>
    /// Historical flush-window figure. Follow no longer waits this out.
    /// </summary>
    public const int WarmupPoseMinAgeMs = 1000;

    /// <summary>
    /// Speed follow and the closed-loop impulse wait for. Arm already declines a target when
    /// the helm is at rest; warmup must not put the snapshot's leftover way back in as a
    /// cruise the new body has to match. That is what froze the 218→186 coasting cross
    /// (target 9.9, B at 0.3–2.0, no impulse) for 4.7 s.
    /// </summary>
    public static float ExpectedCruiseForWarmup(float armedTarget, float snapshotSpeed, sbyte liveThrottle)
    {
        if (armedTarget >= BoatSeamImpulse.MinCruiseSpeed)
            return armedTarget;
        if (liveThrottle != 0 && snapshotSpeed >= BoatSeamImpulse.MinCruiseSpeed)
            return snapshotSpeed;
        return 0f;
    }

    /// <summary>
    /// A body slower than this is still the Create flush spinning up, not a measurement of the
    /// seeded hull. 186→218 at 01:32:41 reported 2.3 m/s at 266 ms; impulsing the 15.2 m/s
    /// shortfall overshot to 22.8. The historical real shortfall was ~8 m/s on a consumed body.
    /// </summary>
    public const float ConsumedWarmupSpeed = 6f;

    /// <summary>
    /// After <see cref="ReplantSettleMs"/>, a body that has kept this fraction of the
    /// crossing's way is consumed even if it is under <see cref="ConsumedWarmupSpeed"/>.
    /// Reverse 218→186 live 14:01:42: 4.8 / 8.8 = 0.55. Flush 2.3 / 16.9 = 0.14.
    /// </summary>
    public const float ConsumedWarmupRatio = 0.35f;

    /// <summary>
    /// True when the new body has taken the seed but is still short of cruise — the moment to
    /// fire the closed-loop impulse. A flush spin-up is not that body. The client must not
    /// follow this pose; it is the speed bump.
    /// </summary>
    /// <param name="replantAgeMs">
    /// Age of the overlap seed, or the arm elapsed for a non-overlap warmup. Below
    /// <see cref="ReplantSettleMs"/> only the absolute consumed floor counts, so a 2.3
    /// flush is not patched. After settle, a reverse body that held ~half of an 8.8
    /// crossing is patched too.
    /// </param>
    public static bool ShouldImpulseWarmup(
        float x, float y, float reportedSpeed, float expectedCruiseSpeed, long replantAgeMs = -1)
    {
        if (!IsInsideShipWorld(x, y))
            return false;
        if (expectedCruiseSpeed < BoatSeamImpulse.MinCruiseSpeed)
            return false;
        if (reportedSpeed + BoatSeamImpulse.MinCorrectionDeficit >= expectedCruiseSpeed)
            return false;

        if (reportedSpeed >= ConsumedWarmupSpeed)
            return true;

        if (replantAgeMs < ReplantSettleMs)
            return false;
        if (reportedSpeed < BoatSeamImpulse.MinCruiseSpeed)
            return false;
        return reportedSpeed >= expectedCruiseSpeed * ConsumedWarmupRatio;
    }

    /// <summary>
    /// Follow may consider a warmup pose only when it is the restored cruise. An unconsumed
    /// or short-of-cruise report never wins — that is the speed bump. The caller still has
    /// to wait until that body has reached the client-bridge plant
    /// (<see cref="BoatSeamHandoffRules.HasReachedClientBridge"/>); cruise at the Create xyz
    /// is the rewind.
    /// </summary>
    public static bool ShouldAcceptWarmupHandoff(
        float x, float y, float reportedSpeed, float expectedCruiseSpeed, long elapsedMs,
        long msSinceImpulse = -1)
    {
        _ = (elapsedMs, msSinceImpulse);
        if (!IsInsideShipWorld(x, y))
            return false;
        if (expectedCruiseSpeed <= 0f)
            return true;
        return reportedSpeed + BoatSeamImpulse.MinCorrectionDeficit >= expectedCruiseSpeed;
    }

    /// <summary>
    /// Overlap follow: stay on A while B is still short of cruise. A silent still
    /// switches (after a just-fired impulse lands). A live fail-safe onto a short
    /// body was the reverse hitch: 218→186 at 8.8 followed 186 at 4.8.
    /// </summary>
    public static bool ShouldFinishOverlapSeam(
        bool oldSimSilent,
        long replantAgeMs,
        float x,
        float y,
        float reportedSpeed,
        float expectedCruise,
        long msSinceImpulse,
        float alongTrackMetres = 0f,
        long msSinceCatchUp = -1)
    {
        var shortOfCruise = expectedCruise >= BoatSeamImpulse.MinCruiseSpeed &&
            !ShouldAcceptWarmupHandoff(x, y, reportedSpeed, expectedCruise, replantAgeMs, msSinceImpulse);
        var impulseSettling = msSinceImpulse >= 0 && msSinceImpulse < ImpulseSettleMs;

        // A silent still waits out a just-fired shortfall. Switching on that tick is the
        // 17.6 → 6.8 dip (186 dies at the 218 edge; B's first consumed pose is short).
        if (oldSimSilent)
            return !shortOfCruise || !impulseSettling;
        if (!IsInsideShipWorld(x, y))
            return false;
        if (replantAgeMs < ReplantSettleMs)
            return false;
        if (shortOfCruise)
            return false;
        // B at cruise but still behind the body the client is watching: switching now steps
        // the hull back by that gap (live 1.3–1.4 m on both 218↔186 crossings). While A is
        // still talking there is no reason to take that step; the catch-up impulse closes it,
        // and a catch-up in flight gets its window before the fail-safe.
        if (IsBehindStreamedBody(alongTrackMetres) &&
            (replantAgeMs < OverlapFollowFailSafeMs || CatchUpInFlight(msSinceCatchUp)))
            return false;

        return true;
    }

    /// <summary>
    /// Speed to remove at the follow switch after a catch-up. The pulse has partly bled off by
    /// then (thrust law above cruise), so taking the whole of it back dropped the hull under
    /// cruise (live 18:43:42: 12.6 reported → 8.9 against 9.1). Remove only what is still above
    /// cruise, never more than was added, and nothing below the correction floor.
    /// </summary>
    public static float CatchUpTakeBack(float catchUpAdded, float reportedSpeed, float expectedCruise)
    {
        if (catchUpAdded <= 0f)
            return 0f;
        if (expectedCruise < BoatSeamImpulse.MinCruiseSpeed || reportedSpeed <= 0f)
            return catchUpAdded;
        var excess = MathF.Min(catchUpAdded, reportedSpeed - expectedCruise);
        return excess >= BoatSeamImpulse.MinCorrectionDeficit ? excess : 0f;
    }

    /// <summary>
    /// A catch-up impulse has been sent and has not yet had <see cref="CatchUpSeconds"/> to land.
    /// </summary>
    public static bool CatchUpInFlight(long msSinceCatchUp) =>
        msSinceCatchUp >= 0 && msSinceCatchUp < (long)(CatchUpSeconds * 1000f);

    /// <summary>
    /// Negative along-track beyond the tolerance a followed body may lag the streamed one.
    /// </summary>
    public static bool IsBehindStreamedBody(float alongTrackMetres) =>
        alongTrackMetres < -BoatSeamHandoffRules.CatchUpMetres;

    /// <summary>
    /// Time the catch-up impulse is given to close the along-track gap. Half the follow
    /// backstop, so one catch-up fits inside the fail-safe window.
    /// </summary>
    public const float CatchUpSeconds = 0.5f;

    /// <summary>
    /// Most a catch-up may add: what a hull can credibly gain in <see cref="CatchUpSeconds"/>
    /// (<see cref="BoatSeamHandoffRules.MaxCredibleAccelMetresPerSecondSquared"/>). A gap that
    /// needs more than this is a different plant, not a lag, and is left to the fail-safe.
    /// </summary>
    public static float MaxCatchUpSpeed =>
        BoatSeamHandoffRules.MaxCredibleAccelMetresPerSecondSquared * CatchUpSeconds;

    /// <summary>
    /// Forward speed to add so a body at cruise that is <paramref name="alongTrackMetres"/> behind
    /// the streamed one closes the gap in <see cref="CatchUpSeconds"/>. Zero when not behind.
    /// </summary>
    public static float CatchUpSpeed(float alongTrackMetres)
    {
        if (!IsBehindStreamedBody(alongTrackMetres))
            return 0f;
        var gap = -alongTrackMetres;
        return MathF.Min(gap / CatchUpSeconds, MaxCatchUpSpeed);
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
