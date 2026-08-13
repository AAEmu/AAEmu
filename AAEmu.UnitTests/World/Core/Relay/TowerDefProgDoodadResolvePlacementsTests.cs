using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class TowerDefProgDoodadResolvePlacementsTests
{
    [Test]
    public async Task ResolvePlacements_EmptyWorldOrTargets_IsEmpty()
    {
        await Assert.That(TowerDefProgDoodads.ResolvePlacements(null, [])).IsEmpty();
        await Assert.That(TowerDefProgDoodads.ResolvePlacements("main_world", null)).IsEmpty();
        await Assert.That(TowerDefProgDoodads.ResolvePlacements("main_world", [])).IsEmpty();
    }

    [Test]
    public async Task ResolvePlacements_FiltersToWantedTemplatesFromCatalog()
    {
        const string world = "main_world_resolve_test";
        try
        {
            ZoneDoodadPlacementCatalog.SeedIndexForTests(world,
            [
                new(8410, 20113.513f, 21012.532f, 102.865f, 0f),
                new(9999, 1f, 2f, 3f, 0f)
            ]);

            var targets = new List<TowerDefProgSpawnTarget>
            {
                new() { SpawnTargetId = 8410, SpawnTargetType = "DoodadAlmighty" }
            };
            var list = TowerDefProgDoodads.ResolvePlacements(world, targets);
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].TemplateId).IsEqualTo(8410u);
            await Assert.That(list[0].X).IsEqualTo(20113.513f).Within(0.001f);
            await Assert.That(list[0].Y).IsEqualTo(21012.532f).Within(0.001f);
        }
        finally
        {
            ZoneDoodadPlacementCatalog.Invalidate(world);
        }
    }
}
