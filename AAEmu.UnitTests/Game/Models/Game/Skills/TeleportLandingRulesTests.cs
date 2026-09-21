using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class TeleportLandingRulesTests
{
    [Test]
    public async Task LoadedDestination_IsAllowed()
    {
        await Assert.That(TeleportLandingRules.CanLandInZone(
            zoneAuthority: true, isZoneLoaded: _ => true, destinationZoneId: 186)).IsTrue();
    }

    [Test]
    public async Task UnloadedDestination_IsRefused()
    {
        await Assert.That(TeleportLandingRules.CanLandInZone(
            zoneAuthority: true, isZoneLoaded: _ => false, destinationZoneId: 186)).IsFalse();
    }

    [Test]
    public async Task WithoutZoneAuthority_OrWithoutAProbe_NothingIsRefused()
    {
        await Assert.That(TeleportLandingRules.CanLandInZone(
            zoneAuthority: false, isZoneLoaded: _ => false, destinationZoneId: 186)).IsTrue();
        await Assert.That(TeleportLandingRules.CanLandInZone(
            zoneAuthority: true, isZoneLoaded: null, destinationZoneId: 186)).IsTrue();
    }

    [Test]
    public async Task TheProbe_IsAskedAboutTheDestinationZone()
    {
        uint? asked = null;
        var allowed = TeleportLandingRules.CanLandInZone(
            zoneAuthority: true, isZoneLoaded: zoneId => { asked = zoneId; return true; }, destinationZoneId: 288);

        await Assert.That(allowed).IsTrue();
        await Assert.That(asked).IsEqualTo(288u);
    }

    [Test]
    public async Task SameZoneWithAuthority_RelaysTheBlink()
    {
        await Assert.That(TeleportLandingRules.RelaysSameZoneBlink(stayInZone: true, zoneAuthority: true)).IsTrue();
    }

    [Test]
    public async Task CrossZoneOrNoAuthority_DoesNotBlinkTheOldZone()
    {
        await Assert.That(TeleportLandingRules.RelaysSameZoneBlink(stayInZone: false, zoneAuthority: true)).IsFalse();
        await Assert.That(TeleportLandingRules.RelaysSameZoneBlink(stayInZone: true, zoneAuthority: false)).IsFalse();
        await Assert.That(TeleportLandingRules.RelaysSameZoneBlink(stayInZone: false, zoneAuthority: false)).IsFalse();
    }

    [Test]
    public async Task Classify_SameWorldOrSameInstance_IsAWorldLanding()
    {
        await Assert.That(TeleportLandingRules.Classify(true, 0, 1, destHasDungeon: true))
            .IsEqualTo(TeleportLandingKind.World);
        await Assert.That(TeleportLandingRules.Classify(false, 0, 0, destHasDungeon: true))
            .IsEqualTo(TeleportLandingKind.World);
        await Assert.That(TeleportLandingRules.StaysInZone(183, 183, 0, 0)).IsTrue();
        await Assert.That(TeleportLandingRules.StaysInZone(133, 183, 0, 0)).IsFalse();
    }

    [Test]
    public async Task Classify_DifferentWorld_UsesDungeonEnterOrOtherInstanceLoad()
    {
        await Assert.That(TeleportLandingRules.Classify(false, 0, 5, destHasDungeon: true))
            .IsEqualTo(TeleportLandingKind.InstanceDungeon);
        await Assert.That(TeleportLandingRules.Classify(false, 0, 5, destHasDungeon: false))
            .IsEqualTo(TeleportLandingKind.InstanceOther);
        await Assert.That(TeleportLandingRules.Classify(false, 5, 0, destHasDungeon: false))
            .IsEqualTo(TeleportLandingKind.InstanceOther);
    }
}
