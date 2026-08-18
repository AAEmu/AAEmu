using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class TerrainFloorTests
{
    private static float Floor(float ground) => ground + TerrainFloor.ClearanceMetres;

    [Test]
    public async Task NearBand_SitsOnFloorWithClearance()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 119.2f, ground: 119.2f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(Floor(119.2f));
    }

    [Test]
    public async Task AlreadyNearGround_UsesFloorClearance()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 102f, ground: 100f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(Floor(100f));
    }

    [Test]
    public async Task MildUnderground_LiftsWithinCap()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 114.8f, ground: 120.3f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(Floor(120.3f));
    }

    [Test]
    public async Task CliffSample_DoesNotLiftPastUpwardCap()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 122.9f, ground: 139.0f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(Floor(122.9f));
    }

    [Test]
    public async Task ShortBallDrop_SnapsToTerrain()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 180f, ground: 172f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(Floor(172f));
    }

    [Test]
    public async Task DeepAirPlot_DoesNotSnapIntoHole()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 171.7f, ground: 119.2f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(171.7f);
    }

    [Test]
    public async Task OpenWater_UsesWaterSurfaceNotSeabed()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 64.8f, ground: 37.4f, overWater: true, waterSurfaceZ: 100f))
            .IsEqualTo(100f + TerrainFloor.ClearanceMetres);
    }

    /// <summary>
    /// Lusca Aken: plot marker ~120 above OceanLevel, heightmap is seabed ~22.
    /// Without water snap, keep-raw fights Zone physics → flash/jump.
    /// </summary>
    [Test]
    public async Task LuscaPlotAboveOcean_SnapsToSurfaceNotSeabedOrRaw()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 120f, ground: 22f, overWater: true, waterSurfaceZ: 100f))
            .IsEqualTo(100f + TerrainFloor.ClearanceMetres);
    }

    [Test]
    public async Task MissingGround_KeepsRaw()
    {
        await Assert.That(TerrainFloor.ChooseUnitFloorZ(
            rawZ: 150f, ground: 0f, overWater: false, waterSurfaceZ: 0f)).IsEqualTo(150f);
    }
}
