using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatZoneKeyStabilityTests
{
    [Test]
    public async Task Resolve_KeepsCurrentKeyUntilSampleIsStable()
    {
        const uint boat = 42;
        const uint zoneA = 149;
        const uint zoneB = 218;

        BoatZoneKeyStability.Clear(boat);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneA, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneB);
    }

    [Test]
    public async Task Resolve_OscillatingSamplesNeverCommit()
    {
        const uint boat = 99;
        const uint zoneA = 149;
        const uint zoneB = 218;

        BoatZoneKeyStability.Clear(boat);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneA, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneA, zoneA)).IsEqualTo(zoneA);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, zoneB, zoneA)).IsEqualTo(zoneA);
    }

    [Test]
    public async Task ForceCommit_AcceptsSampleImmediately()
    {
        const uint boat = 7;
        BoatZoneKeyStability.Clear(boat);
        await Assert.That(BoatZoneKeyStability.ForceCommit(boat, 218)).IsEqualTo(218u);
        await Assert.That(BoatZoneKeyStability.Resolve(boat, 149, 218)).IsEqualTo(218u);
    }
}
