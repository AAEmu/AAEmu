using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Recent listing fees: the post dialog's lowest / highest lines, kept after a row leaves the board.
/// </summary>
public class CraftOrderFeeStatsRulesTests
{
    [Test]
    public async Task UnitFee_IsTheListedTotalPerRun()
    {
        await Assert.That(CraftOrderFeeStatsRules.UnitFee(20_000_000, 1)).IsEqualTo(20_000_000ul);
        await Assert.That(CraftOrderFeeStatsRules.UnitFee(20_000_000, 5)).IsEqualTo(4_000_000ul);
        await Assert.That(CraftOrderFeeStatsRules.UnitFee(100, 0)).IsEqualTo(100ul);
    }

    [Test]
    public async Task FromFees_IsTheMinAndMax()
    {
        var range = CraftOrderFeeStatsRules.FromFees([10_000_000, 20_000_000, 15_000_000]);
        await Assert.That(range.Any).IsTrue();
        await Assert.That(range.Lowest).IsEqualTo(10_000_000ul);
        await Assert.That(range.Highest).IsEqualTo(20_000_000ul);
    }

    [Test]
    public async Task FromFees_EmptyIsNoRange()
    {
        await Assert.That(CraftOrderFeeStatsRules.FromFees([]).Any).IsFalse();
        await Assert.That(CraftOrderFeeStatsRules.FromFees(null).Any).IsFalse();
    }

    [Test]
    public async Task Include_OpensARangeThenWidensIt()
    {
        var first = CraftOrderFeeStatsRules.Include(0, 0, have: false, 10_000_000);
        await Assert.That(first.Lowest).IsEqualTo(10_000_000ul);
        await Assert.That(first.Highest).IsEqualTo(10_000_000ul);

        var wider = CraftOrderFeeStatsRules.Include(first.Lowest, first.Highest, have: true, 20_000_000);
        await Assert.That(wider.Lowest).IsEqualTo(10_000_000ul);
        await Assert.That(wider.Highest).IsEqualTo(20_000_000ul);

        var inside = CraftOrderFeeStatsRules.Include(wider.Lowest, wider.Highest, have: true, 15_000_000);
        await Assert.That(inside).IsEqualTo(wider);
    }

    [Test]
    public async Task Store_KeepsARangeAfterTheLiveRowIsGone()
    {
        var store = new InMemoryCraftOrderStore();
        await Assert.That(store.UpsertFeeStats(new CraftOrderFeeStat(5591, 10_000_000, 20_000_000))).IsTrue();
        await Assert.That(store.Insert(new CraftOrder { Id = 1, CraftId = 5591, Fee = 10_000_000 })).IsTrue();
        await Assert.That(store.Delete(1)).IsTrue();
        await Assert.That(store.LoadAll().Count).IsEqualTo(0);

        var stats = store.LoadFeeStats();
        await Assert.That(stats.Count).IsEqualTo(1);
        await Assert.That(stats[0].CraftId).IsEqualTo(5591u);
        await Assert.That(stats[0].Lowest).IsEqualTo(10_000_000ul);
        await Assert.That(stats[0].Highest).IsEqualTo(20_000_000ul);
    }
}
