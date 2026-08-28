using System.Numerics;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class FloorQueryTests
{
    [Test]
    public async Task QueryFloor_WhenLegacyAndGeoDataOn_UsesNavNodeHeight()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorSourceMode: () => FloorSourceMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(210f);
        await Assert.That(hit.Source).IsEqualTo(FloorSource.LegacyNavNode);
        await Assert.That(hit.TerrainZ).IsEqualTo(133.8f);
        await Assert.That(hit.NavNodeZ).IsEqualTo(210f);
    }

    [Test]
    public async Task QueryFloor_WhenLegacyAndGeoReturnsZero_FallsBackToTerrain()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 0f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorSourceMode: () => FloorSourceMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Spawn);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Source).IsEqualTo(FloorSource.Terrain);
    }

    [Test]
    public async Task QueryFloor_WhenLegacyAndGeoDataOff_UsesTerrain()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => false,
            heightMapsEnabled: () => true,
            floorSourceMode: () => FloorSourceMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Source).IsEqualTo(FloorSource.Terrain);
    }

    [Test]
    public async Task GetFloor_MatchesLegacyWorldManagerPriority_ForFixedPoints()
    {
        // Golden: same priority as WorldManager.GetHeight with GeoDataMode on —
        // non-zero nav node wins over terrain.
        var samples = new (Vector3 pos, float nav, float terrain, float expected)[]
        {
            (new Vector3(10, 10, 100), 120f, 100f, 120f),
            (new Vector3(20, 20, 100), 0f, 105.5f, 105.5f),
            (new Vector3(30, 30, 100), 99.25f, 80f, 99.25f),
        };

        foreach (var (pos, nav, terrain, expected) in samples)
        {
            var floor = new FloorQuery(
                worldTemplate: null,
                geoHeight: _ => nav,
                terrainHeight: (_, _) => terrain,
                geoDataEnabled: () => true,
                heightMapsEnabled: () => true,
                floorSourceMode: () => FloorSourceMode.Legacy);

            var z = floor.GetFloor(pos.X, pos.Y, pos.Z, FloorContext.Debug);
            await Assert.That(z).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task QueryFloor_WhenTerrainFirst_PrefersTerrainOverNavNode()
    {
        // Outdoor fix: nav node at 210 must not lift the unit above terrain 133.8
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorSourceMode: () => FloorSourceMode.TerrainFirst,
            isMultiFloorWorld: () => false);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Source).IsEqualTo(FloorSource.Terrain);
        await Assert.That(hit.NavNodeZ).IsEqualTo(210f);
        await Assert.That(hit.DeltaNav).IsGreaterThan(70f);
    }

    [Test]
    public async Task QueryFloor_WhenTerrainFirstAndNoTerrain_FallsBackToNavNode()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 0f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorSourceMode: () => FloorSourceMode.TerrainFirst,
            isMultiFloorWorld: () => false);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(210f);
        await Assert.That(hit.Source).IsEqualTo(FloorSource.LegacyNavNode);
    }

    [Test]
    public async Task TryProjectOnEdgeXy_Midpoint_ReturnsHalfTAndLerpZ()
    {
        var a = new Vector3(0, 0, 10);
        var b = new Vector3(10, 0, 20);
        var ok = NavSurfaceSampler.TryProjectOnEdgeXy(5, 0, a, b, out var t, out var px, out var py, out var distSq);

        await Assert.That(ok).IsTrue();
        await Assert.That(t).IsEqualTo(0.5f);
        await Assert.That(px).IsEqualTo(5f);
        await Assert.That(py).IsEqualTo(0f);
        await Assert.That(distSq).IsEqualTo(0f);
        await Assert.That(a.Z + (b.Z - a.Z) * t).IsEqualTo(15f);
    }
}
