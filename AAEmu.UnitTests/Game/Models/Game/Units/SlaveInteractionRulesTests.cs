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
    public async Task OfferedSkills_HoldsBackSeatRowsWhenTheEquipmentCannotBeLookedUp()
    {
        var rows = new[]
        {
            new SlaveInteractionSkill(36443, true, 0, 0),      // drive the cart, always offered
            new SlaveInteractionSkill(35799, true, 63, 60)     // seat 1, once a kind 60 chair is at attach point 63
        };

        // Without a way to read the vehicle's equipment the seat stays out rather than being offered blind.
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows)).IsEquivalentTo(new List<uint> { 36443 });
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, null)).IsEquivalentTo(new List<uint> { 36443 });
    }

    [Test]
    public async Task OfferedSkills_OffersASeatOnlyWhenItsAttachPointCarriesThatKind()
    {
        var rows = new[] { new SlaveInteractionSkill(35799, true, 63, 60) };

        // the chair the seat belongs to is fitted there
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, attachPoint => attachPoint == 63 ? 60u : null))
            .IsEquivalentTo(new List<uint> { 35799 });

        // something else is fitted there, nothing is fitted, or the item is not slave equipment at all
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, attachPoint => attachPoint == 63 ? 66u : null).Count).IsEqualTo(0);
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, _ => null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfferedSkills_AcceptsAnyFittedItemWhenTheRowNamesNoKind()
    {
        var rows = new[] { new SlaveInteractionSkill(35800, true, 64, 0) };

        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, attachPoint => attachPoint == 64 ? 19u : null))
            .IsEquivalentTo(new List<uint> { 35800 });
        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, _ => null).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfferedSkills_LeavesOutARowThatNamesAKindWithoutAnAttachPoint()
    {
        // Nothing says where to look for the kind, so the row can never be satisfied.
        var rows = new[] { new SlaveInteractionSkill(35799, true, 0, 60) };

        await Assert.That(SlaveInteractionRules.OfferedSkills(rows, _ => 60u).Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfferedSkills_AsksOnlyAboutTheAttachPointsItsRowsName()
    {
        var rows = new[] { new SlaveInteractionSkill(35799, true, 63, 60) };
        var asked = new List<uint>();

        SlaveInteractionRules.OfferedSkills(rows, attachPoint =>
        {
            asked.Add(attachPoint);
            return 60u;
        });

        await Assert.That(asked).IsEquivalentTo(new List<uint> { 63 });
    }

    [Test]
    public async Task OfferedSkills_IsEmptyForASlaveWithNoRows()
    {
        await Assert.That(SlaveInteractionRules.OfferedSkills([]).Count).IsEqualTo(0);
        await Assert.That(SlaveInteractionRules.OfferedSkills(null).Count).IsEqualTo(0);
    }
}
