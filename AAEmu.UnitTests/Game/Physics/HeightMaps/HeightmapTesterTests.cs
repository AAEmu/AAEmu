using AAEmu.Game.Physics.HeightMaps;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Physics.HeightMaps;

/// <summary>
/// Tests for HeightmapTester: terrain lies on the horizontal X,Y plane with height on Z.
/// </summary>
public class HeightmapTesterTests
{
    private static HeightmapTester CreateTester(Func<int, int, float> height)
    {
        var heights = new float[20, 20];
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 20; y++)
            heights[x, y] = height(x, y);
        return new HeightmapTester(new Heightmap(heights));
    }

    [Test]
    public async Task Constructor_WithHeights_PutsHeightRangeOnZ()
    {
        // Arrange & Act
        var tester = CreateTester((_, _) => 10f);

        // Assert
        await Assert.That(tester.WorldBoundingBox.Min.Z).IsEqualTo(10f);
        await Assert.That(tester.WorldBoundingBox.Max.Z).IsEqualTo(10f);
        await Assert.That(tester.WorldBoundingBox.Max.X).IsEqualTo(19f);
        await Assert.That(tester.WorldBoundingBox.Max.Y).IsEqualTo(19f);
    }

    [Test]
    public async Task RayCast_DownOntoFlatTerrain_HitsAtTerrainHeightWithUpNormal()
    {
        // Arrange
        var tester = CreateTester((_, _) => 10f);
        var origin = new JVector(5.3f, 5.6f, 30f);
        var direction = new JVector(0.05f, 0.03f, -1f);

        // Act
        var hit = tester.RayCast(origin, direction, out var normal, out var lambda);
        var hitPoint = origin + lambda * direction;

        // Assert
        await Assert.That(hit).IsTrue();
        await Assert.That(hitPoint.Z).IsEqualTo(10f).Within(0.001f);
        await Assert.That(normal.Z).IsEqualTo(1f).Within(0.001f);
    }

    [Test]
    public async Task RayCast_OntoTerrainRisingAlongX_NormalTiltsTowardNegativeX()
    {
        // Arrange: raw heights rise with x (Heightmap samples every 2 units, so x = 5..6 is a rising quad)
        var tester = CreateTester((x, _) => x);
        var origin = new JVector(5.4f, 5.5f, 30f);
        var direction = new JVector(0.01f, 0.01f, -1f);

        // Act
        var hit = tester.RayCast(origin, direction, out var normal, out _);

        // Assert
        await Assert.That(hit).IsTrue();
        await Assert.That(normal.X).IsLessThan(0f);
        await Assert.That(normal.Z).IsGreaterThan(0f);
    }
}
