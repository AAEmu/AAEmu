using AAEmu.Game.Models.Game.Slaves;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SlaveLandVehiclePlantRulesTests
{
    [Test]
    public async Task ChoosePlantZ_PrefersHeightmapNearOwnerFeet()
    {
        await Assert.That(SlaveLandVehiclePlantRules.ChoosePlantZ(
            ownerZ: 110.0f, heightmapZ: 109.8f, geoDataZ: 111.5f)).IsEqualTo(109.8f);
    }

    [Test]
    public async Task ChoosePlantZ_RejectsGeoDataHoverAboveOwner()
    {
        // Old path preferred GeoData when |geo-owner|<=2 and planted ~1.5 m high — wheels lose contact.
        await Assert.That(SlaveLandVehiclePlantRules.ChoosePlantZ(
            ownerZ: 110.0f, heightmapZ: 0f, geoDataZ: 111.5f)).IsEqualTo(110.0f);
    }

    [Test]
    public async Task ChoosePlantZ_FallsBackToOwnerWhenHeightmapFarFromFeet()
    {
        await Assert.That(SlaveLandVehiclePlantRules.ChoosePlantZ(
            ownerZ: 110.0f, heightmapZ: 125.0f, geoDataZ: 111.0f)).IsEqualTo(110.0f);
    }

    [Test]
    public async Task ChoosePlantZ_UsesHeightmapWhenOwnerMissing()
    {
        await Assert.That(SlaveLandVehiclePlantRules.ChoosePlantZ(
            ownerZ: 0f, heightmapZ: 119.2f, geoDataZ: 121f)).IsEqualTo(119.2f);
    }

    [Test]
    public async Task ClientSeedZBand_RejectsSkyPlantsNearOwner()
    {
        await Assert.That(SlaveSummonSeedRules.IsWithinValidZ(
            110f, 110f, SlaveLandVehiclePlantRules.ClientSeedZBandMetres)).IsTrue();
        await Assert.That(SlaveSummonSeedRules.IsWithinValidZ(
            110f, 124.9f, SlaveLandVehiclePlantRules.ClientSeedZBandMetres)).IsTrue();
        await Assert.That(SlaveSummonSeedRules.IsWithinValidZ(
            110f, 125.1f, SlaveLandVehiclePlantRules.ClientSeedZBandMetres)).IsFalse();
        await Assert.That(SlaveSummonSeedRules.IsWithinValidZ(
            110f, 90f, SlaveLandVehiclePlantRules.ClientSeedZBandMetres)).IsFalse();
    }
}
