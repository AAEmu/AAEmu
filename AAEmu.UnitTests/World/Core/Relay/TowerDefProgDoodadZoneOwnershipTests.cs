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
}
