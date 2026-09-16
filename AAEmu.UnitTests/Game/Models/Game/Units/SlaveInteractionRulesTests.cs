using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveInteractionRulesTests
{
    [Test]
    public async Task OfferedSkills_KeepsTheTablesOrderAndDropsRepeats()
    {
        var rows = new[]
        {
            new SlaveInteractionSkill(36443, true, 0, 0),
            new SlaveInteractionSkill(50762, true, 0, 0),
            new SlaveInteractionSkill(36443, true, 0, 0) // same skill twice in the table
        };

        await Assert.That(SlaveInteractionRules.OfferedSkills(rows))
            .IsEquivalentTo(new List<uint> { 36443, 50762 });
    }

    [Test]
    public async Task OfferedSkills_LeavesOutDisabledRows()
    {
        var rows = new[]
        {
            new SlaveInteractionSkill(12076, true, 0, 0),      // offered
            new SlaveInteractionSkill(13863, false, 0, 0)      // disabled
        };

        await Assert.That(SlaveInteractionRules.OfferedSkills(rows)).IsEquivalentTo(new List<uint> { 12076 });
    }

    [Test]
    public async Task OfferedSkills_HoldsBackGearGatedRowsWhenSlotsCannotBeLookedUp()
    {
        var rows = new[]
        {
            new SlaveInteractionSkill(36443, true, 0, 0),
            new SlaveInteractionSkill(35799, true, 63, 60)     // needs slot 63 to hold kind 60
        };

        // no way to look the slot up: the gated row stays out rather than being offered blind
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows)).IsEquivalentTo(new List<uint> { 36443 });
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, null)).IsEquivalentTo(new List<uint> { 36443 });
    }

    [Test]
    public async Task OfferedSkills_OffersAGatedRowOnlyWhenTheSlotHoldsThatKind()
    {
        var rows = new[] { new SlaveInteractionSkill(35799, true, 63, 60) };

        // the slot holds the kind the row asks for
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, slot => slot == 63 ? 60u : null))
            .IsEquivalentTo(new List<uint> { 35799 });

        // the wrong kind, an empty slot, or an item the table does not describe
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, slot => slot == 63 ? 66u : null).Count).IsEqualTo(0);
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, _ => null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfferedSkills_AcceptsAnyItemInASlotWhenTheRowNamesNoKind()
    {
        var rows = new[] { new SlaveInteractionSkill(35800, true, 64, 0) };

        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, slot => slot == 64 ? 19u : null))
            .IsEquivalentTo(new List<uint> { 35800 });
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, _ => null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfferedSkills_IsEmptyForASlaveWithNoRows()
    {
        await Assert.That(SlaveInteractionRules.OfferedSkills([]).Count).IsEqualTo(0);
        await Assert.That(SlaveInteractionRules.OfferedSkills(null).Count).IsEqualTo(0);
    }
}
