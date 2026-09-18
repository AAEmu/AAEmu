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

    [Test]
    public async Task PositionTakesKind_LeavesAGroupListedPositionOpen()
    {
        // slot 467 lists groups 3, 4, 10, 1; figurehead 43737 is kind 20 and would otherwise be refused
        var kinds = new HashSet<uint> { 3, 4, 10, 1 };

        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 20)).IsTrue();
        await Assert.That(SlaveEquipRules.PositionTakesKind(kinds, 0)).IsFalse();
    }

    [Test]
    public async Task PackAllowed_TakesOnlyThePacksTheSlaveLists()
    {
        var allowed = new HashSet<uint> { 8, 17 };

        await Assert.That(SlaveEquipRules.PackAllowed(8, allowed)).IsTrue();
        await Assert.That(SlaveEquipRules.PackAllowed(17, allowed)).IsTrue();
        await Assert.That(SlaveEquipRules.PackAllowed(20, allowed)).IsFalse();
    }

    [Test]
    public async Task PackAllowed_TakesAnyPackOnTheItemsList()
    {
        // 다용 함포 42563 lists packs 12, 13, 15, 16; slave 620 allows 12
        var allowed = new HashSet<uint> { 12 };

        await Assert.That(SlaveEquipRules.PackAllowed([14, 12, 13], allowed)).IsTrue();
        await Assert.That(SlaveEquipRules.PackAllowed([14], allowed)).IsFalse();
    }

    [Test]
    public async Task PackAllowed_SaysNothingForAnItemInNoPackOrASlaveWithNoList()
    {
        // an item that belongs to no pack, or a slave allow_to_equip_slaves says nothing about, is left to
        // the position's own rule rather than turned away here
        await Assert.That(SlaveEquipRules.PackAllowed(0, new HashSet<uint> { 8 })).IsTrue();
        await Assert.That(SlaveEquipRules.PackAllowed(8, [])).IsTrue();
        await Assert.That(SlaveEquipRules.PackAllowed(8, null)).IsTrue();
    }
}
