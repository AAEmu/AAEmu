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
    public async Task ShouldAcceptWarmupHandoff_WaitsOutTheFlushTransientWindow()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 10f, 18.8f, 0)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 10f, 18.8f, 999)).IsFalse();
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 10f, 18.8f, 1000)).IsTrue();
    }

    [Test]
    public async Task ShouldAcceptWarmupHandoff_ZeroSpeedWhenNotExpectingCruise()
    {
        await Assert.That(BoatZoneSimRules.ShouldAcceptWarmupHandoff(13120f, 9842f, 0f, 0f, 0)).IsTrue();
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
    public async Task WarmupPoseMinAge_IsTheTunedFlushTransientWindow()
    {
        // Empirical: how long a post-arm report stays an unconsumed-body type-4. Measured live;
        // shrink only after arm→usable deltas from a real crossing justify it.
        await Assert.That(BoatZoneSimRules.WarmupPoseMinAgeMs).IsEqualTo(1000);
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
