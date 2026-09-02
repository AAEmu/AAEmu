using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatWaterlineRulesTests
{
    // ship_models 14 (boxship / Ostera) and 9 (pirate / Growling).
    private const float OsteraMassCenterZ = -2f;
    private const float OsteraKeelHeight = 0.2f;
    private const float OsteraTubeLength = 0f;
    private const float OsteraTubeRadius = 0f;
    private const float GrowlingMassCenterZ = -4f;
    private const float GrowlingKeelHeight = 0.2f;
    private const float GrowlingTubeLength = 19f;
    private const float GrowlingTubeRadius = 5f;

    [Test]
    public async Task HasBuoyancyTube_WhenEitherColumnIsSet()
    {
        await Assert.That(BoatWaterlineRules.HasBuoyancyTube(GrowlingTubeLength, GrowlingTubeRadius)).IsTrue();
        await Assert.That(BoatWaterlineRules.HasBuoyancyTube(0f, 2f)).IsTrue();
        await Assert.That(BoatWaterlineRules.HasBuoyancyTube(19f, 0f)).IsTrue();
    }

    [Test]
    public async Task HasBuoyancyTube_FalseWhenBothColumnsAreZero()
    {
        await Assert.That(BoatWaterlineRules.HasBuoyancyTube(OsteraTubeLength, OsteraTubeRadius)).IsFalse();
    }

    [Test]
    public async Task ShouldApplyKeelPlant_StandaloneOnlyWithoutATube_ZoneAuthorityNever()
    {
        await Assert.That(BoatWaterlineRules.ShouldApplyKeelPlant(OsteraTubeLength, OsteraTubeRadius)).IsTrue();
        await Assert.That(BoatWaterlineRules.ShouldApplyKeelPlant(GrowlingTubeLength, GrowlingTubeRadius)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldApplyKeelPlant(zoneAuthority: true)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldApplyKeelPlant(zoneAuthority: false)).IsTrue();
    }

    [Test]
    public async Task KeelPlantOffset_MatchesStandaloneMassCenterAndKeel()
    {
        await Assert.That(BoatWaterlineRules.KeelPlantOffset(OsteraMassCenterZ, OsteraKeelHeight))
            .IsEqualTo(-1.2f);
        await Assert.That(BoatWaterlineRules.KeelPlantOffset(GrowlingMassCenterZ, GrowlingKeelHeight))
            .IsEqualTo(-2.2f);
        await Assert.That(BoatWaterlineRules.KeelPlantOffset(0.3f, 0.1f)).IsEqualTo(-0.1f);
    }

    [Test]
    public async Task RecoverZ_NeverBelowTheSurfaceOrAFloatingHull()
    {
        await Assert.That(BoatWaterlineRules.RecoverZ(100f, 96.2f)).IsEqualTo(100f);
        await Assert.That(BoatWaterlineRules.RecoverZ(100f, 100.8f)).IsEqualTo(100.8f);
        await Assert.That(BoatWaterlineRules.RecoverZ(100f, 100f)).IsEqualTo(100f);
    }

    [Test]
    public async Task ShouldHoldSimOff_Never_ZoneSimIsThePath()
    {
        // The flag is the zone's whole ship simulation switch. With it off the hull is driven
        // from the network movement controller, so a parked hull is pinned to the last pose World
        // sent and stops moving entirely -- measured on a tube hull as much as a prefab-buoy one,
        // with no heave or list until the helm was taken. An unmanned hull that rides bow-up is
        // the simulation diverging, and withholding this only trades that for a dead boat.
        await Assert.That(BoatWaterlineRules.ShouldHoldSimOff(false, hasDriver: false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldHoldSimOff(true, hasDriver: false)).IsFalse();

        await Assert.That(BoatWaterlineRules.ShouldHoldSimOff(false, hasDriver: true)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldHoldSimOff(true, hasDriver: true)).IsFalse();
    }

    [Test]
    public async Task ShouldResumeHeldSim_Always_TubeOrPrefabBuoy()
    {
        await Assert.That(BoatWaterlineRules.ShouldResumeHeldSim(false)).IsTrue();
        await Assert.That(BoatWaterlineRules.ShouldResumeHeldSim(true)).IsTrue();
    }

    [Test]
    public async Task ShouldRecover_Never_ZoneSimOwnsTheWaterline()
    {
        // Live Ostera #1109, 2026-08-31 18:14:36: Z=98.1 sog=2.8 after 15 s of
        // quiet sim. Sunk recover then cycled 0/1. Leave that to the dedicate.
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 98.1f,
            speedOverGround: 2.8f,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 98.5f,
            speedOverGround: 0f,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_GrowlingSittingOnTheSurfaceDoesNot()
    {
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 100.4f,
            speedOverGround: 0.1f,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: true)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_MovingOnTheSurfaceIsNotRestomped_TubeOrPrefabBuoy()
    {
        // Ostera (no tube) making ≥ 2 m/s at/above the surface used to recover and
        // cycle WZShipControlChange 0/1. Prefab buoys need that sim left alone.
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 100.8f,
            speedOverGround: BoatSeamImpulse.MinCruiseSpeed,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 100.0f,
            speedOverGround: BoatSeamImpulse.MinCruiseSpeed,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 100.8f,
            speedOverGround: BoatSeamImpulse.MinCruiseSpeed,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: true)).IsFalse();

        // Live first recover, 2026-08-31 12:01:21, Ostera tpl=75 obj=1549:
        // Z=99.7 (0.3 m down, under SinkBand 0.5) sog=2.1. That was unmannedDrift.
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 99.7f,
            speedOverGround: 2.1f,
            throttle: 0,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_NotWhileTheHelmIsHeld()
    {
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 98.5f,
            speedOverGround: 8f,
            throttle: 127,
            hasDriver: false,
            hasBuoyancyTube: false)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_NotWhileADriverIsSeated()
    {
        // Occupied Ostera below the band with A/D only (throttle 0). Recover would
        // cycle control and flicker both the rider and the rudder.
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 98.5f,
            speedOverGround: 1.8f,
            throttle: 0,
            hasDriver: true,
            hasBuoyancyTube: false)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_DriverCoastingOnTheSurfaceIsLeftAlone()
    {
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            inSeam: false,
            armedAgeMs: BoatZoneSimRules.ReplantSettleMs,
            recoverAgeMs: -1,
            surfaceZ: 100f,
            hullZ: 100f,
            speedOverGround: 8f,
            throttle: 0,
            hasDriver: true,
            hasBuoyancyTube: false)).IsFalse();
    }

    [Test]
    public async Task ShouldRecover_NotDuringASeamOrBeforeSettleOrInsideCooldown()
    {
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            true, BoatZoneSimRules.ReplantSettleMs, -1, 100f, 98.5f, 0f, 0, false, false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            false, BoatZoneSimRules.ReplantSettleMs - 1, -1, 100f, 98.5f, 0f, 0, false, false)).IsFalse();
        await Assert.That(BoatWaterlineRules.ShouldRecover(
            false, BoatZoneSimRules.ReplantSettleMs, 0, 100f, 98.5f, 0f, 0, false, false)).IsFalse();
    }
}
