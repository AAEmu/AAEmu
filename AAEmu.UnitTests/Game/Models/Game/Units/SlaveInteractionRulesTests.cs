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
    public async Task OfferedSkills_LeavesOutDisabledRowsAndRowsAskingForGear()
    {
        var rows = new[]
        {
            new SlaveInteractionSkill(12076, true, 0, 0),      // offered
            new SlaveInteractionSkill(13863, false, 0, 0),     // disabled
            new SlaveInteractionSkill(35799, true, 63, 60),    // needs a slot and a kind, not wired yet
            new SlaveInteractionSkill(35800, true, 64, 0)
        };

        // the two gear-gated rows stay out until the item-to-kind link exists: six shipped rows carry it
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows)).IsEquivalentTo(new List<uint> { 12076 });
    }

    [Test]
    public async Task OfferedSkills_IsEmptyForASlaveWithNoRows()
    {
        await Assert.That(SlaveInteractionRules.OfferedSkills([]).Count).IsEqualTo(0);
        await Assert.That(SlaveInteractionRules.OfferedSkills(null).Count).IsEqualTo(0);
    }
}
