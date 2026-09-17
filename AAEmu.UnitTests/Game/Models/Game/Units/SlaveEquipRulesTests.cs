using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveEquipRulesTests
{
    [Test]
    public async Task PositionTakesKind_TakesOnlyTheKindsItLists()
    {
        var kinds = new HashSet<uint> { 20, 25 };

        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 20)).IsTrue();
        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 25)).IsTrue();
        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 19)).IsFalse();
        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 4)).IsFalse();
    }

    [Test]
    public async Task PositionTakesKind_SaysNothingWhenThePositionListsNothing()
    {
        // a position with no rows in slave_equip_kind_lists is a position the tables do not restrict
        await Assert.That(SlaveEquipRules.PositionTakesKind([], 20)).IsTrue();
        await Assert.That(SlaveEquipRules.PositionTakesKind(null, 20)).IsTrue();
    }

    [Test]
    public async Task PositionTakesKind_TurnsAwayAnItemTheTablesGiveNoKind()
    {
        // an item with no kind is not slave equipment, so a position that lists kinds does not take it
        var kinds = new HashSet<uint> { 20 };

        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 0)).IsFalse();
    }
}
