using System.IO;
using System.Numerics;

using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Readers;
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
            floorPolicyMode: () => FloorPolicyMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(210f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.LegacyNavNode);
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
            floorPolicyMode: () => FloorPolicyMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Spawn);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
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
            floorPolicyMode: () => FloorPolicyMode.Legacy);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
    }

    [Test]
    public async Task GetFloor_MatchesLegacyWorldManagerPriority_ForFixedPoints()
    {
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
                floorPolicyMode: () => FloorPolicyMode.Legacy);

            var z = floor.GetFloor(pos.X, pos.Y, pos.Z, FloorContext.Debug);
            await Assert.That(z).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task QueryFloor_WhenByZHint_OutdoorPrefersTerrainOverHighNav()
    {
        // #1425: nav vertex at 210 must not lift the unit; zHint near ground → terrain in window.
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) => null);

        var hit = floor.QueryFloor(100f, 200f, 134f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
        await Assert.That(hit.NavNodeZ).IsEqualTo(210f);
        await Assert.That(hit.DeltaNav).IsGreaterThan(70f);
    }

    [Test]
    public async Task QueryFloor_WhenByZHint_OutdoorHealsFloaterTowardTerrain()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) => null);

        var hit = floor.QueryFloor(100f, 200f, 210f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
    }

    [Test]
    public async Task QueryFloor_WhenByZHintAndNoTerrain_FallsBackToNavNode()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 0f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) => null);

        var hit = floor.QueryFloor(100f, 200f, 150f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(210f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.LegacyNavNode);
    }

    [Test]
    public async Task QueryFloor_WhenByZHint_CaveUsesNavSurfaceNearZHint()
    {
        // #1033 Ronbann-style: heightmap is the surface ~200m above; nav edge is the cave floor.
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 480f,
            terrainHeight: (_, _) => 479.2f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, zHint) => zHint is >= 280f and <= 310f ? 293.4f : null);

        var hit = floor.QueryFloor(10f, 10f, 294f, FloorContext.Move);

        await Assert.That(hit.Z).IsEqualTo(293.4f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.NavSurface);
        await Assert.That(hit.TerrainZ).IsEqualTo(479.2f);
    }

    [Test]
    public async Task QueryFloor_WhenByZHint_MultiFloorUsesNavSurfaceWithZHint()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 280f,
            terrainHeight: (_, _) => 100f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, zHint) => zHint >= 150f ? 151.2f : 100.5f);

        var upper = floor.QueryFloor(10f, 10f, 152f, FloorContext.Move);
        await Assert.That(upper.Z).IsEqualTo(151.2f);
        await Assert.That(upper.Provider).IsEqualTo(FloorProvider.NavSurface);

        var lower = floor.QueryFloor(10f, 10f, 101f, FloorContext.Move);
        await Assert.That(lower.Z).IsEqualTo(100.5f);
        await Assert.That(lower.Provider).IsEqualTo(FloorProvider.NavSurface);
    }

    [Test]
    public async Task QueryFloor_WhenByZHintAndNoSurface_FallsBackToTerrain()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) => null);

        var hit = floor.QueryFloor(10f, 10f, 140f, FloorContext.Move);
        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
    }

    [Test]
    public async Task QueryFloor_WhenByZHint_OutdoorSkipsNavSurfaceSampler()
    {
        // Greptile P2 early-out: terrain near zHint and nav near terrain → injected sampler must not run.
        var sampled = false;
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 135f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) =>
            {
                sampled = true;
                return 999f;
            });

        var hit = floor.QueryFloor(10f, 10f, 134f, FloorContext.Move);

        await Assert.That(sampled).IsFalse();
        await Assert.That(hit.Z).IsEqualTo(133.8f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
    }

    [Test]
    public async Task CanSkipNavSurfaceOutdoor_CaveDoesNotSkip()
    {
        var skip = FloorQuery.CanSkipNavSurfaceOutdoor(terrainZ: 479f, navNodeZ: 293f, zHint: 294f);
        await Assert.That(skip).IsFalse();
    }

    [Test]
    public async Task FloorResolver_PicksCloserCandidateInsideVerticalWindow()
    {
        var hit = FloorResolver.Pick(
            zHint: 150f,
            terrainZ: 148f,
            navSurfaceZ: 156f,
            navNodeZ: 200f,
            verticalSep: 12f);

        await Assert.That(hit.Z).IsEqualTo(148f);
        await Assert.That(hit.Provider).IsEqualTo(FloorProvider.Terrain);
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

    [Test]
    public async Task ApplyPathWaypointZ_UsesNavSurfaceNotRawVertexZ()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 200f,
            terrainHeight: (_, _) => 100f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (pos, _) =>
            {
                if (pos.X is >= 0f and <= 10f)
                    return 100f + pos.X * 10f;
                return null;
            });

        var rawPath = new[]
        {
            new Vector3(0, 0, 100),
            new Vector3(5, 0, 200),
            new Vector3(10, 0, 200),
        };

        var adjusted = floor.ApplyPathWaypointZ(rawPath).ToArray();

        await Assert.That(adjusted.Length).IsEqualTo(3);
        await Assert.That(adjusted[0].Z).IsEqualTo(100f);
        await Assert.That(adjusted[1].Z).IsEqualTo(150f);
        await Assert.That(adjusted[2].Z).IsEqualTo(200f);
        await Assert.That(adjusted[1].X).IsEqualTo(5f);
    }

    [Test]
    public async Task ApplyPathWaypointZ_WhenNoSurface_KeepsOriginalZ()
    {
        var floor = new FloorQuery(
            worldTemplate: null,
            geoHeight: _ => 210f,
            terrainHeight: (_, _) => 133.8f,
            geoDataEnabled: () => true,
            heightMapsEnabled: () => true,
            floorPolicyMode: () => FloorPolicyMode.ByZHint,
            navSurfaceHeight: (_, _) => null);

        var raw = new[] { new Vector3(1, 2, 55f), new Vector3(3, 4, 66f) };
        var adjusted = floor.ApplyPathWaypointZ(raw).ToArray();

        await Assert.That(adjusted[0].Z).IsEqualTo(55f);
        await Assert.That(adjusted[1].Z).IsEqualTo(66f);
    }
}

public class NavEdgeSpatialIndexTests
{
    [Test]
    public async Task ForEachNear_OnlyVisitsEdgesInRadiusCells()
    {
        var net = new NetMissionReader(Stream.Null, 0);
        var nearA = new NodeDescriptor(net) { Id = 1, Pos = new Vector3(0, 0, 10) };
        var nearB = new NodeDescriptor(net) { Id = 2, Pos = new Vector3(8, 0, 12) };
        var farA = new NodeDescriptor(net) { Id = 3, Pos = new Vector3(200, 200, 10) };
        var farB = new NodeDescriptor(net) { Id = 4, Pos = new Vector3(210, 200, 12) };

        var index = new NavEdgeSpatialIndex(cellSize: 16f);
        index.AddEdge(nearA, nearB);
        index.AddEdge(farA, farB);

        var visited = 0;
        index.ForEachNear(4f, 0f, radius: 16f, (_, _) => visited++);

        await Assert.That(visited).IsEqualTo(1);
    }

    [Test]
    public async Task Index_MidEdgeProjection_MatchesSamplerMath()
    {
        var net = new NetMissionReader(Stream.Null, 0);
        var a = new NodeDescriptor(net) { Id = 1, Pos = new Vector3(0, 0, 100) };
        var b = new NodeDescriptor(net) { Id = 2, Pos = new Vector3(10, 0, 200) };

        var index = new NavEdgeSpatialIndex(cellSize: 16f);
        index.AddEdge(a, b);

        float? best = null;
        index.ForEachNear(5f, 0f, 16f, (na, nb) =>
        {
            if (!NavSurfaceSampler.TryProjectOnEdgeXy(5f, 0f, na.Pos, nb.Pos, out var t, out _, out _, out var distSq))
                return;
            if (distSq > 0.01f)
                return;
            best = NavSurfaceSampler.LerpEdgeZ(na, nb, t);
        });

        await Assert.That(best).IsEqualTo(150f);
    }
}
