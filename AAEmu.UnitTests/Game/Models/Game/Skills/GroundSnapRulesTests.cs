using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// move_to_ground (type 73) lands the unit on the floor at the XY it already holds, through the shared
/// <see cref="TerrainFloor"/> policy. No heightmap sample means no floor, and the unit must not be moved.
/// </summary>
public class GroundSnapRulesTests
{
    [Test]
    public async Task NoHeightmap_ThereIsNoFloorToMoveTo()
    {
        // Heightmaps off (or a missing cell) answer 0. Moving a unit there would drop it into the sea.
        await Assert.That(GroundSnapRules.TryGetFloorZ(
            rawZ: 120f, sampledGround: 0f, overWater: false, waterSurfaceZ: 0f, out var z)).IsFalse();
        await Assert.That(z).IsEqualTo(120f);
    }

    [Test]
    public async Task GroundUnderTheUnit_SnapsWithClearance()
    {
        await Assert.That(GroundSnapRules.TryGetFloorZ(
            rawZ: 100.5f, sampledGround: 100f, overWater: false, waterSurfaceZ: 0f, out var z)).IsTrue();
        await Assert.That(z).IsEqualTo(100f + TerrainFloor.ClearanceMetres).Within(0.001f);
    }

    [Test]
    public async Task UnitBuriedInTerrain_IsLiftedOntoTheFloor()
    {
        await Assert.That(GroundSnapRules.TryGetFloorZ(
            rawZ: 95f, sampledGround: 100f, overWater: false, waterSurfaceZ: 0f, out var z)).IsTrue();
        await Assert.That(z).IsEqualTo(100f + TerrainFloor.ClearanceMetres).Within(0.001f);
    }

    [Test]
    public async Task OverWater_TheSurfaceWinsOverTheSeabed()
    {
        // Open ocean samples the seabed; dropping a teleporting character onto it is visibly wrong.
        await Assert.That(GroundSnapRules.TryGetFloorZ(
            rawZ: 120f, sampledGround: 60f, overWater: true, waterSurfaceZ: 100f, out var z)).IsTrue();
        await Assert.That(z).IsEqualTo(100f + TerrainFloor.ClearanceMetres).Within(0.001f);
    }

    [Test]
    public async Task AWildDrop_KeepsTheRawZ()
    {
        // Further than the drop cap: a bad sample or a cliff, and the raw Z is kept rather than trusted.
        var raw = 200f;
        await Assert.That(GroundSnapRules.TryGetFloorZ(
            rawZ: raw, sampledGround: 100f, overWater: false, waterSurfaceZ: 0f, out var z)).IsTrue();
        await Assert.That(z).IsEqualTo(raw);
    }
}
