using AAEmu.Game.Models.Game;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class TowerDefProgDoodadZoneOwnershipTests
{
    [Test]
    public async Task ResolvePlacementZoneId_PrefersConfiguredZoneId()
    {
        var place = new TowerDefProgDoodadPlacement
        {
            TemplateId = 8411,
            X = 1,
            Y = 2,
            Z = 3,
            ZoneId = 257
        };
        var zone = TowerDefProgDoodads.ResolvePlacementZoneId(null, place, fallbackZoneId: 184);
        await Assert.That(zone).IsEqualTo(257u);
    }

    [Test]
    public async Task ResolvePlacementZoneId_FallsBackWhenUnresolved()
    {
        var place = new TowerDefProgDoodadPlacement { TemplateId = 8411, ZoneId = 0 };
        var zone = TowerDefProgDoodads.ResolvePlacementZoneId(null, place, fallbackZoneId: 184);
        await Assert.That(zone).IsEqualTo(184u);
    }

    [Test]
    public async Task DistinctWorldsForHosts_EmptyInput_IsEmpty()
    {
        var worlds = TowerDefProgDoodads.DistinctWorldsForHosts([]);
        await Assert.That(worlds.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DistinctWorldKeys_TwoHostZonesSameWorld_CollapsesToOne()
    {
        // Abyssal-style: multiple host zone keys can share one WorldInstance.
        var distinct = TowerDefProgDoodads.DistinctWorldKeysForHosts(
            [213u, 184u],
            zoneId => zoneId is 213 or 184 ? 1u : null);

        await Assert.That(distinct.Count).IsEqualTo(1);
        await Assert.That(distinct[0].WorldId).IsEqualTo(1u);
        await Assert.That(distinct[0].FallbackZoneId).IsEqualTo(213u);
    }

    [Test]
    public async Task BuildSpawnPosition_SetsZoneIdBeforeSpawn()
    {
        var place = new TowerDefProgDoodadPlacement
        {
            TemplateId = 8410,
            X = 20113.513f,
            Y = 21012.532f,
            Z = 102.865f,
            Yaw = -90f,
            ZoneId = 213
        };
        var owning = TowerDefProgDoodads.ResolvePlacementZoneId(null, place, fallbackZoneId: 184);
        var pos = TowerDefProgDoodads.BuildSpawnPosition(place, owning);

        await Assert.That(owning).IsEqualTo(213u);
        await Assert.That(pos.ZoneId).IsEqualTo(213u);
        await Assert.That(pos.X).IsEqualTo(20113.513f).Within(0.001f);
        // Degrees from level packs → radians for WorldSpawnPosition.
        await Assert.That(pos.Yaw).IsEqualTo((-90f) * (MathF.PI / 180f)).Within(0.0001f);
    }
}
