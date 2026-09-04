using System.Numerics;

using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class BoatSpawnPlacementTests
{
    [Test]
    public async Task ForwardDot_AheadIsPositive()
    {
        var caster = new Vector3(0f, 0f, 100f);
        // yaw 0 → forward is +Y
        var ahead = new Vector3(0f, 10f, 100f);
        await Assert.That(SlaveManager.BoatSpawnForwardDot(caster, 0f, ahead)).IsGreaterThan(0.9f);
    }

    [Test]
    public async Task ForwardDot_BehindIsNegative()
    {
        var caster = new Vector3(0f, 0f, 100f);
        var behind = new Vector3(0f, -10f, 100f);
        await Assert.That(SlaveManager.BoatSpawnForwardDot(caster, 0f, behind)).IsLessThan(-0.9f);
    }

    [Test]
    public async Task SurfaceAboveCaster_RejectedAsSkyLake()
    {
        await Assert.That(SlaveManager.IsBoatSurfaceAllowed(
            casterZ: 120f, surfaceZ: 200f, floorZ: 150f, minDepth: 5f)).IsFalse();
    }

    [Test]
    public async Task DeepWaterNearCaster_Allowed()
    {
        await Assert.That(SlaveManager.IsBoatSurfaceAllowed(
            casterZ: 120f, surfaceZ: 100f, floorZ: 80f, minDepth: 5f)).IsTrue();
    }

    [Test]
    public async Task OceanWhileSlightlyUnderSurface_Allowed()
    {
        // Swimming: caster Z can sit below the ocean plane.
        await Assert.That(SlaveManager.IsBoatSurfaceAllowed(
            casterZ: 95f, surfaceZ: 100f, floorZ: 80f, minDepth: 5f)).IsTrue();
    }

    [Test]
    public async Task ShallowWater_Rejected()
    {
        await Assert.That(SlaveManager.IsBoatSurfaceAllowed(
            casterZ: 120f, surfaceZ: 100f, floorZ: 98f, minDepth: 5f)).IsFalse();
    }

    [Test]
    public async Task Score_PrefersAheadOverBehind()
    {
        var ahead = SlaveManager.ScoreBoatSpawnCandidate(1f, 10f, 10f);
        var behind = SlaveManager.ScoreBoatSpawnCandidate(-1f, 10f, 10f);
        await Assert.That(ahead).IsGreaterThan(behind);
    }
}
