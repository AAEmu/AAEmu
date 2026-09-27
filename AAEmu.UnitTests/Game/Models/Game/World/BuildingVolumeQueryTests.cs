using AAEmu.Game.Models.Game.World;
using AAEmuGeoData.Scripts.CryEngine.Mission;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class BuildingVolumeQueryTests
{
    [Test]
    public async Task TryAsBuildingVolume_RejectsZeroBuildingIdAndHugeHeight()
    {
        var noId = new SpecialArea(1) { BuildingId = 0, Height = 10, MinZ = 100, MaxZ = 110 };
        await Assert.That(BuildingVolumeQuery.TryAsBuildingVolume(noId, out _)).IsFalse();

        var huge = new SpecialArea(1) { BuildingId = 7, Height = 500, MinZ = 0, MaxZ = 500 };
        await Assert.That(BuildingVolumeQuery.TryAsBuildingVolume(huge, out _)).IsFalse();

        var house = new SpecialArea(1) { BuildingId = 7, Height = 40, MinZ = 137, MaxZ = 177 };
        await Assert.That(BuildingVolumeQuery.TryAsBuildingVolume(house, out var volume)).IsTrue();
        await Assert.That(volume.BuildingId).IsEqualTo(7);
        await Assert.That(volume.MinZ).IsEqualTo(137f);
    }

    [Test]
    public async Task IsFalseOutdoorShell_WhenMinZOnTerrain()
    {
        var plaza = new BuildingVolume { BuildingId = 1, MinZ = 121.1f, MaxZ = 130f, Height = 9f };
        await Assert.That(BuildingVolumeQuery.IsFalseOutdoorShell(plaza, terrainZ: 121f)).IsTrue();

        var house = new BuildingVolume { BuildingId = 2, MinZ = 139f, MaxZ = 175f, Height = 36f };
        await Assert.That(BuildingVolumeQuery.IsFalseOutdoorShell(house, terrainZ: 137f)).IsFalse();

        // Deck MinZ 1 m above dirt must not count as plaza shell.
        var lowHouse = new BuildingVolume { BuildingId = 3, MinZ = 138f, MaxZ = 170f, Height = 32f };
        await Assert.That(BuildingVolumeQuery.IsFalseOutdoorShell(lowHouse, terrainZ: 137f)).IsFalse();
    }

    [Test]
    public async Task LooksLikeCave_SmallCaveNearEntrance()
    {
        // Shallow cave: feet and nav a few metres under heightmap.
        await Assert.That(BuildingVolumeQuery.LooksLikeCave(terrainZ: 150f, navNodeZ: 147f, zHint: 147.2f)).IsTrue();
        await Assert.That(BuildingVolumeQuery.LooksLikeCave(terrainZ: 150f, navNodeZ: 150.2f, zHint: 150.1f)).IsFalse();
    }

    [Test]
    public async Task ShouldUseForFloor_RejectsUndergroundMissionBox()
    {
        var caveBox = new BuildingVolume { BuildingId = 9, MinZ = 280f, MaxZ = 300f, Height = 20f };
        await Assert.That(BuildingVolumeQuery.ShouldUseForFloor(caveBox, zHint: 293f, terrainZ: 479f)).IsFalse();
    }

    [Test]
    public async Task IsInPolygonXy_Square()
    {
        var poly = new List<System.Numerics.Vector3>
        {
            new(0, 0, 0),
            new(10, 0, 0),
            new(10, 10, 0),
            new(0, 10, 0),
        };
        await Assert.That(BuildingVolumeQuery.IsInPolygonXy(5, 5, poly)).IsTrue();
        await Assert.That(BuildingVolumeQuery.IsInPolygonXy(15, 5, poly)).IsFalse();
    }

    [Test]
    public async Task StoreyHintForIdle_DoesNotPinToUpperFloor()
    {
        // Standing on 1F while home/spawn is 2F — keep 1F feet.
        await Assert.That(BuildingVolumeQuery.StoreyHintForIdle(currentZ: 139f, anchorZ: 152f)).IsEqualTo(139f);
        // Sunk a little through thin deck — lift to home deck.
        await Assert.That(BuildingVolumeQuery.StoreyHintForIdle(currentZ: 137.5f, anchorZ: 139f)).IsEqualTo(139f);
    }
}
