using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatZoneSimRulesTests
{
    private const uint ZoneA = 186;
    private const uint ZoneB = 218;
    private const uint ZoneC = 219;
    private const uint None = 0;

    [Test]
    public async Task ShouldArm_FirstZoneToTakeTheHull()
    {
        await Assert.That(BoatZoneSimRules.ShouldArm(ZoneA, None)).IsTrue();
    }

    [Test]
    public async Task ShouldArm_SkipsTheZoneThatAlreadySimulates()
    {
        // Mounting the helm of a hull the zone already simulates must not re-enter its simulation.
        await Assert.That(BoatZoneSimRules.ShouldArm(ZoneA, ZoneA)).IsFalse();
    }

    [Test]
    public async Task ShouldArm_ArmsTheZoneTheHullSailedInto()
    {
        await Assert.That(BoatZoneSimRules.ShouldArm(ZoneB, ZoneA)).IsTrue();
    }

    [Test]
    public async Task ShouldArm_UnknownZoneKeyDoesNothing()
    {
        await Assert.That(BoatZoneSimRules.ShouldArm(0, None)).IsFalse();
    }

    [Test]
    public async Task ShouldOverlapOldSim_WhenLeavingALiveSimulator()
    {
        await Assert.That(BoatZoneSimRules.ShouldOverlapOldSim(ZoneA, ZoneB)).IsTrue();
    }

    [Test]
    public async Task ShouldOverlapOldSim_NotOnFirstSummon()
    {
        await Assert.That(BoatZoneSimRules.ShouldOverlapOldSim(None, ZoneA)).IsFalse();
    }

    [Test]
    public async Task ShouldSendEnable_WhenTheHullIsStillThere()
    {
        await Assert.That(BoatZoneSimRules.ShouldSendEnable(ZoneA, ZoneA, ZoneA)).IsTrue();
    }

    [Test]
    public async Task ShouldSendEnable_DuringOverlapWhileWorldStillFollowsTheOldZone()
    {
        await Assert.That(BoatZoneSimRules.ShouldSendEnable(ZoneB, ZoneA, ZoneB)).IsTrue();
    }

    [Test]
    public async Task ShouldSendEnable_NotAfterTheHullSailedOn()
    {
        await Assert.That(BoatZoneSimRules.ShouldSendEnable(ZoneA, ZoneB, ZoneB)).IsFalse();
    }

    [Test]
    public async Task ShouldSendEnable_NotAfterWithdrawal()
    {
        await Assert.That(BoatZoneSimRules.ShouldSendEnable(ZoneA, None, None)).IsFalse();
    }

    [Test]
    public async Task IsWarmupSource_WhileWorldStillFollowsTheOldZone()
    {
        await Assert.That(BoatZoneSimRules.IsWarmupSource(ZoneB, ZoneA, ZoneB)).IsTrue();
    }

    [Test]
    public async Task IsWarmupSource_NotTheZoneWorldAlreadyFollows()
    {
        await Assert.That(BoatZoneSimRules.IsWarmupSource(ZoneA, ZoneA, ZoneB)).IsFalse();
        await Assert.That(BoatZoneSimRules.IsWarmupSource(ZoneA, ZoneA, None)).IsFalse();
    }

    [Test]
    public async Task IsWarmupSource_NotAfterTheFollowSwitch()
    {
        await Assert.That(BoatZoneSimRules.IsWarmupSource(ZoneB, ZoneB, None)).IsFalse();
    }

    [Test]
    public async Task ShouldDeferSimArm_FirstSummonAfterTheAnnounceStamp()
    {
        await Assert.That(BoatZoneSimRules.ShouldDeferSimArm(ZoneA, ZoneA)).IsTrue();
    }

    [Test]
    public async Task ShouldDeferSimArm_OnASeamAfterCreate()
    {
        await Assert.That(BoatZoneSimRules.ShouldDeferSimArm(ZoneA, ZoneB)).IsTrue();
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_PlacedAndAlreadyAtCruise()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 10.9f, 10.9f, 0)).IsTrue();
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_NotAtWorldOrigin()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(0f, 0f, 10.9f, 10.9f, 1000)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(8f, 9842f, 10.9f, 10.9f, 1000)).IsFalse();
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_RejectsAnUnconsumedBodyReport()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0f, 18.8f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0.2f, 18.8f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0.2f, 18.8f, 1000)).IsFalse();
    }

    [Test]
    public async Task ShouldImpulseWarmup_OnlyAConsumedBodyThatIsStillShort()
    {
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13120f, 9842f, 2.3f, 16.9f)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13120f, 9842f, 8.1f, 13.5f)).IsTrue();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13120f, 9842f, 16.9f, 16.9f)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13120f, 9842f, 0.2f, 16.9f)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(0f, 0f, 8.1f, 13.5f)).IsFalse();
        // Live 12:59:40 186→218: seed 17.6, first B pose 6.8.
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(12957f, 9906f, 6.8f, 17.6f)).IsTrue();
        // Reverse 218→186 14:01:42: 4.8 / 8.8 is consumed after settle, flush 2.3 / 16.9 is not.
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13056f, 10168f, 4.8f, 8.8f)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13056f, 10168f, 4.8f, 8.8f, 200)).IsTrue();
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13120f, 9842f, 2.3f, 16.9f, 266)).IsFalse();
    }

    [Test]
    public async Task ShouldFinishOverlapSeam_WaitsOnAShortConsumedBody()
    {
        // Same hop: do not follow at settle while B is 6.8 and an impulse just fired.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 200, 12957f, 9906f, 6.8f, 17.6f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 200, 12957f, 9906f, 17.6f, 17.6f, 200)).IsTrue();
        // 186 dies at the 218 edge: still wait out the just-fired shortfall.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            true, 50, 12957f, 9906f, 6.8f, 17.6f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            true, 250, 12957f, 9906f, 6.8f, 17.6f, 200)).IsTrue();
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            true, 250, 12957f, 9906f, 17.6f, 17.6f, 200)).IsTrue();
        // A still talking: do not fail-safe onto a short reverse body (8.8 → 4.8 hitch).
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, BoatZoneSimRules.OverlapFollowFailSafeMs, 13056f, 10168f, 4.8f, 8.8f, -1))
            .IsFalse();
    }

    [Test]
    public async Task ShouldFinishOverlapSeam_WaitsWhileBIsBehindTheStreamedBody()
    {
        // Live 18:00:57 / 18:01:47: B at cruise after the impulse but 1.3–1.4 m behind A's
        // streamed body; switching there stepped the hull back by that gap.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 281, 13074f, 10114f, 10.9f, 9.9f, 100, alongTrackMetres: -1.4f)).IsFalse();
        // Within tolerance: switch.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 281, 13074f, 10114f, 10.9f, 9.9f, 100, alongTrackMetres: -0.3f)).IsTrue();
        // Ahead: switch.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 281, 13074f, 10114f, 10.9f, 9.9f, 100, alongTrackMetres: 0.6f)).IsTrue();
        // Fail-safe: a gap the catch-up did not close does not hold the client on A forever.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, BoatZoneSimRules.OverlapFollowFailSafeMs, 13074f, 10114f, 10.9f, 9.9f, 300,
            alongTrackMetres: -1.4f)).IsTrue();
        // A silent: the gap cannot be closed against a dead body; switch as before.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            true, 281, 13074f, 10114f, 10.9f, 9.9f, 300, alongTrackMetres: -1.4f)).IsTrue();
    }

    [Test]
    public async Task ShouldFinishOverlapSeam_CatchUpInFlightOutlivesTheFailSafe()
    {
        // Live 18:24:56: catch-up sent at 437 ms, fail-safe cut the wait at 625 ms with 1.9 m left.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 625, 13085f, 9979f, 14.9f, 9.9f, 190, alongTrackMetres: -1.9f, msSinceCatchUp: 190)).IsFalse();
        // Catch-up window over and still behind: give up and switch.
        await Assert.That(BoatZoneSimRules.ShouldFinishOverlapSeam(
            false, 950, 13085f, 9979f, 14.9f, 9.9f, 520, alongTrackMetres: -1.0f, msSinceCatchUp: 520)).IsTrue();
        await Assert.That(BoatZoneSimRules.CatchUpInFlight(-1)).IsFalse();
        await Assert.That(BoatZoneSimRules.CatchUpInFlight(499)).IsTrue();
        await Assert.That(BoatZoneSimRules.CatchUpInFlight(500)).IsFalse();
    }

    [Test]
    public async Task CatchUpTakeBack_RemovesOnlyWhatIsStillAboveCruise()
    {
        // Live 18:43:42: added 4.3, body reports 12.6 against cruise 9.1 → remove 3.5, not 4.3.
        await Assert.That(BoatZoneSimRules.CatchUpTakeBack(4.3f, 12.6f, 9.1f)).IsEqualTo(3.5f).Within(0.01f);
        // Never more than was added.
        await Assert.That(BoatZoneSimRules.CatchUpTakeBack(2f, 15f, 9f)).IsEqualTo(2f);
        // Already bled back to cruise: nothing to remove.
        await Assert.That(BoatZoneSimRules.CatchUpTakeBack(4.3f, 9.3f, 9.1f)).IsEqualTo(0f);
        await Assert.That(BoatZoneSimRules.CatchUpTakeBack(0f, 12f, 9f)).IsEqualTo(0f);
        // No reference: fall back to the full pulse.
        await Assert.That(BoatZoneSimRules.CatchUpTakeBack(4.3f, 0f, 9.1f)).IsEqualTo(4.3f);
    }

    [Test]
    public async Task CatchUpSpeed_ClosesTheGapInHalfASecondAndIsCapped()
    {
        await Assert.That(BoatZoneSimRules.CatchUpSpeed(0.2f)).IsEqualTo(0f);
        await Assert.That(BoatZoneSimRules.CatchUpSpeed(-0.3f)).IsEqualTo(0f);
        await Assert.That(BoatZoneSimRules.CatchUpSpeed(-1.4f)).IsEqualTo(2.8f).Within(0.01f);
        await Assert.That(BoatZoneSimRules.CatchUpSpeed(-40f)).IsEqualTo(BoatZoneSimRules.MaxCatchUpSpeed);
        await Assert.That(BoatZoneSimRules.MaxCatchUpSpeed).IsEqualTo(5f);
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_WaitsForTheRestoredCruise()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 2.3f, 16.9f, 94)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 10f, 18.8f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 16.5f, 16.5f, 200)).IsTrue();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 14.5f, 16.5f, 250, 200)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 2.3f, 16.9f, 400)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0.2f, 18.8f, 1000)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13140f, 9842f, 16.9f, 16.9f, 200)).IsTrue();
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_ZeroSpeedWhenNotExpectingCruise()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0f, 0f, 0)).IsTrue();
    }

    [Test]
    public async Task ExpectedCruiseForWarmup_KeepsTheArmedDriveTarget()
    {
        await Assert.That(BoatZoneSimRules.ExpectedCruiseForWarmup(17.3f, 17.1f, 127))
            .IsEqualTo(17.3f);
    }

    [Test]
    public async Task ExpectedCruiseForWarmup_FillsFromSnapshotOnlyWhileTheHelmIsHeld()
    {
        await Assert.That(BoatZoneSimRules.ExpectedCruiseForWarmup(0f, 9.9f, 127))
            .IsEqualTo(9.9f);
        await Assert.That(BoatZoneSimRules.ExpectedCruiseForWarmup(0f, 9.9f, 0))
            .IsEqualTo(0f);
    }

    [Test]
    public async Task ExpectedCruiseForWarmup_CoastingCrossDoesNotWaitForLeftoverWay()
    {
        // 04:00:44 218→186: Arm left the target at 0 (throttle 0). Snapshot still had 9.9.
        // Follow must accept a consumed body, not wait for a restore the helm is not asking for.
        var expected = BoatZoneSimRules.ExpectedCruiseForWarmup(0f, 9.9f, 0);
        await Assert.That(expected).IsEqualTo(0f);
        await Assert.That(BoatZoneSimRules.ShouldImpulseWarmup(13064.1f, 10176.6f, 0.3f, expected))
            .IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13064.1f, 10176.6f, 0.3f, expected, 15))
            .IsTrue();
    }

    [Test]
    public async Task IsInsideShipWorld_MatchesTheWorldEdgeBand()
    {
        await Assert.That(BoatZoneSimRules.ShipWorldEdgeMetres).IsEqualTo(16f);
        await Assert.That(BoatZoneSimRules.IsInsideShipWorld(16f, 16f)).IsTrue();
        await Assert.That(BoatZoneSimRules.IsInsideShipWorld(15.9f, 16f)).IsFalse();
    }

    [Test]
    public async Task ShouldDeferSimArm_NotBeforeTheHullIsAnnounced()
    {
        await Assert.That(BoatZoneSimRules.ShouldDeferSimArm(None, ZoneA)).IsFalse();
    }

    [Test]
    public async Task FirstSummonSimArmDelay_IsTwoTaskManagerTicks()
    {
        await Assert.That(BoatZoneSimRules.FirstSummonSimArmDelay).IsEqualTo(TimeSpan.FromMilliseconds(100));
    }

    [Test]
    public async Task WarmupPoseMinAge_RemainsTheNamedFlushWindowFigure()
    {
        await Assert.That(BoatZoneSimRules.WarmupPoseMinAgeMs).IsEqualTo(1000);
    }

    [Test]
    public async Task FollowBackstop_IsTheCatchUpFailSafe()
    {
        await Assert.That(BoatZoneSimRules.FollowBackstopMs).IsEqualTo(400);
    }

    [Test]
    public async Task DropOldAtTransfer_StaysOffSoTheClientRidesA()
    {
        await Assert.That(BoatZoneSimRules.DropOldAtTransfer).IsFalse();
    }

    [Test]
    public async Task ReplantSettle_IsTheOverlapSwitchWindow()
    {
        await Assert.That(BoatZoneSimRules.ReplantSettleMs).IsEqualTo(200);
        await Assert.That(BoatZoneSimRules.OldSimSilentMs).IsEqualTo(200);
    }

    [Test]
    public async Task ShouldDropStalePending_WhenALaterSeamWon()
    {
        await Assert.That(BoatZoneSimRules.ShouldDropStalePending(ZoneB, ZoneA, ZoneC)).IsTrue();
    }

    [Test]
    public async Task ShouldDropStalePending_NotTheLiveOrDestinationZone()
    {
        await Assert.That(BoatZoneSimRules.ShouldDropStalePending(ZoneB, ZoneA, ZoneB)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldDropStalePending(ZoneA, ZoneA, ZoneB)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldDropStalePending(None, ZoneA, ZoneB)).IsFalse();
    }
}
