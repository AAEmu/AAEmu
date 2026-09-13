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
}
