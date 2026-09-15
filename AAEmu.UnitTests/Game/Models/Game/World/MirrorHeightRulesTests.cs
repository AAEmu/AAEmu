using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Whether a spawn's Z is grounded. The dedicate lifts its spawns, and a client paints the spawn Z
/// until the unit's first movement record - which a unit that never moves never sends, so the lift
/// would leave it hovering for good.
/// </summary>
public class MirrorHeightRulesTests
{
    [Test]
    public async Task ASmallGapOverTerrain_IsTheSpawnLiftAndIsSnapped()
    {
        // The dedicate's own lift is around 0.4 m; 0.4 m over the ground is not a ledge.
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 100.4f, terrainZ: 100f)).IsTrue();
    }

    [Test]
    public async Task AUnitStandingOnAProp_IsLeftWhereItStands()
    {
        // The height read is the raw heightmap: no porch, dock or stair is in it. A unit 0.6 m up on one
        // of those is legitimately above the terrain, and snapping across that would pull it into the prop.
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 100.6f, terrainZ: 100f)).IsFalse();
    }

    [Test]
    public async Task AGapWiderThanTheLift_IsLeftAlone()
    {
        // A spawn over a cliff edge or a rift fly-in is a real altitude; snapping drops it through.
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 140f, terrainZ: 100f)).IsFalse();
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 101.5f, terrainZ: 100f)).IsFalse();
    }

    [Test]
    public async Task ASpawnFilePosition_KeepsTheMetreItAlwaysHad()
    {
        // Spawn-file Z is authored against the terrain and can sit up to a metre off the bilinear read;
        // that is a different question from a zone record, so the tolerance is the caller's.
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 100.9f, terrainZ: 100f,
            maxSnapMetres: MirrorHeightRules.MaxSpawnFileSnapMetres)).IsTrue();
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 100.9f, terrainZ: 100f)).IsFalse();
    }

    [Test]
    public async Task FliersAndSwimmers_KeepTheirOwnAltitude()
    {
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: true, spawnZ: 100.4f, terrainZ: 100f)).IsFalse();
    }

    [Test]
    public async Task WithoutATerrainSample_NothingIsSnapped()
    {
        // Zero is the "no sample here" convention the height lookups use.
        await Assert.That(MirrorHeightRules.ShouldSnapToTerrain(
            isOffGround: false, spawnZ: 0.4f, terrainZ: 0f)).IsFalse();
    }
}
