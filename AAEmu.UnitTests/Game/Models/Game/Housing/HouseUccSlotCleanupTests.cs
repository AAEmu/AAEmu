using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>Crest-slot rows must not outlive their house, because house ids are handed out again.</summary>
public sealed class HouseUccSlotCleanupTests
{
    [Test]
    public async Task APendingDelete_StaysPendingUntilItIsDeleted()
    {
        var cleanup = new HouseUccSlotCleanup();
        cleanup.MarkPending(7);
        cleanup.MarkPending(9);

        await Assert.That(cleanup.Pending()).IsEquivalentTo(new uint[] { 7, 9 });
        await Assert.That(cleanup.Pending()).IsEquivalentTo(new uint[] { 7, 9 });

        cleanup.Deleted([7]);
        await Assert.That(cleanup.Pending()).IsEquivalentTo(new uint[] { 9 });
    }

    [Test]
    public async Task ANewHouseWritingItsSlots_CancelsThePendingDeleteForThatId()
    {
        // The id was reused before the save got to it: the new house's write already replaced every row,
        // so the save must not delete the new house's crests.
        var cleanup = new HouseUccSlotCleanup();
        cleanup.MarkPending(7);

        cleanup.Written(7);

        await Assert.That(cleanup.Pending()).IsEmpty();
    }

    [Test]
    public async Task Orphans_AreTheRowHouseIdsWithNoLoadedHouse()
    {
        var loaded = new HashSet<uint> { 1, 2 };

        var orphans = HouseUccSlotCleanup.Orphans([1, 3, 3, 2, 4], loaded.Contains);

        await Assert.That(orphans).IsEquivalentTo(new uint[] { 3, 4 });
    }
}
