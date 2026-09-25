using AAEmu.Game.Models.Game.OpenPortal;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class OpenPortalReagentRulesTests
{
    [Test]
    public async Task SelectsOnlyTheRequestedEffectInContentPriorityOrder()
    {
        var reagents = new[]
        {
            Row(30, effectId: 2, priority: 1),
            Row(10, effectId: 1, priority: 2),
            Row(20, effectId: 1, priority: 1),
            Row(40, effectId: 3, priority: 0)
        };

        var selected = OpenPortalReagentRules.ForEffect(reagents, openPortalEffectId: 1);

        await Assert.That(selected.Select(reagent => reagent.Id)).IsEquivalentTo(new[] { 20u, 10u });
    }

    [Test]
    public async Task UsesRowIdAsAStableTieBreaker()
    {
        var reagents = new[]
        {
            Row(9, effectId: 1, priority: 1),
            Row(8, effectId: 1, priority: 1),
            Row(7, effectId: 1, priority: 1)
        };

        var selected = OpenPortalReagentRules.ForEffect(reagents, openPortalEffectId: 1);

        await Assert.That(selected.Select(reagent => reagent.Id)).IsEquivalentTo(new[] { 7u, 8u, 9u });
    }

    [Test]
    public async Task ReturnsEmptyForAnEffectWithNoReagentRows()
    {
        var selected = OpenPortalReagentRules.ForEffect(
            [Row(1, effectId: 1, priority: 1)], openPortalEffectId: 99);

        await Assert.That(selected).IsEmpty();
    }

    private static OpenPortalReagents Row(uint id, uint effectId, int priority) => new()
    {
        Id = id,
        OpenPortalEffectId = effectId,
        ItemId = id + 1000,
        Amount = 1,
        Priority = priority
    };
}
