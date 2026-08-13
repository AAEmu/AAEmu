using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

public class TowerDefProgDoodadPlacementMatcherTests
{
    [Test]
    public async Task Match_ReturnsOnlyWantedTemplates()
    {
        var configured = new List<TowerDefProgDoodadPlacement>
        {
            new() { TemplateId = 8414, X = 1, Y = 2, Z = 3, Yaw = 90 },
            new() { TemplateId = 8411, X = 4, Y = 5, Z = 6 },
            new() { TemplateId = 9999, X = 0, Y = 0, Z = 0 }
        };

        var matched = TowerDefProgDoodadPlacementMatcher.Match(configured, [8414u, 8411u]);

        await Assert.That(matched.Count).IsEqualTo(2);
        await Assert.That(matched.Select(p => p.TemplateId)).IsEquivalentTo(new uint[] { 8414, 8411 });
        await Assert.That(matched[0].X).IsEqualTo(1f);
        await Assert.That(matched[0].Yaw).IsEqualTo(90f);
    }

    [Test]
    public async Task Match_EmptyConfigOrWanted_ReturnsEmpty()
    {
        await Assert.That(TowerDefProgDoodadPlacementMatcher.Match(null, [8414u])).IsEmpty();
        await Assert.That(TowerDefProgDoodadPlacementMatcher.Match([], [8414u])).IsEmpty();
        await Assert.That(
            TowerDefProgDoodadPlacementMatcher.Match(
                [new TowerDefProgDoodadPlacement { TemplateId = 8414 }],
                Array.Empty<uint>())).IsEmpty();
    }
}
