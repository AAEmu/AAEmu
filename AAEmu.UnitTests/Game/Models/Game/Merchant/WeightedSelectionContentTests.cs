using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 1 - weighted selection honors content weights: the raw dice follow the row weights
/// (long arithmetic over the content integers), zero-weight rows are never drawn, distinct draws
/// never repeat a group, and the manager's window roll carries both properties through
/// (heavy group dominates; sale_cnt == group count ships every group exactly once).
/// </summary>
public class WeightedSelectionContentTests
{
    [Test]
    public async Task PickIndex_HonorsTheContentWeights()
    {
        var rng = new Random(20260923);
        long[] weights = [900_000, 100_000];

        var hits = new int[weights.Length];
        const int draws = 20_000;
        for (var i = 0; i < draws; i++)
            hits[WeightedSelection.PickIndex(weights, rng)]++;

        var heavyShare = hits[0] / (double)draws;
        await Assert.That(heavyShare >= 0.88 && heavyShare <= 0.92).IsTrue();
        await Assert.That(hits[1] > 0).IsTrue();
    }

    [Test]
    public async Task PickIndex_EmptyOrAllZeroPool_ReturnsMinusOne()
    {
        await Assert.That(WeightedSelection.PickIndex([], new Random(1))).IsEqualTo(-1);
        await Assert.That(WeightedSelection.PickIndex([0, 0, 0], new Random(1))).IsEqualTo(-1);
    }

    [Test]
    public async Task PickDistinctIndexes_SamplesWithoutReplacement()
    {
        var picked = WeightedSelection.PickDistinctIndexes([5, 5, 5, 5], 4, new Random(7));
        await Assert.That(picked.Count).IsEqualTo(4);
        await Assert.That(picked.Distinct().Count()).IsEqualTo(4);

        // Asking for more than the pool ships every drawable row once: never a repeat, never a wrap,
        // and a zero-weight row stays undrawn.
        var overflow = WeightedSelection.PickDistinctIndexes([3, 0, 4], 10, new Random(7));
        await Assert.That(overflow.Count).IsEqualTo(2);
        await Assert.That(overflow.Contains(1)).IsFalse();
    }

    [Test]
    public async Task WindowDraw_FollowsPackGroupWeights()
    {
        var content = RandomShopTestContent.Build();
        var manager = new RandomMerchantManager();
        manager.UseStore(new InMemoryRandomShopStore());
        manager.UseContent(content);

        var heavyGroupId = content[RandomShopTestContent.WeightedPack].EligibleGroups[0].Id;
        const int rolls = 300;
        var heavyHits = 0;
        var offerCounts = new HashSet<int>();

        for (uint characterId = 1; characterId <= rolls; characterId++)
        {
            var window = manager.GetWindow(characterId, RandomShopTestContent.WeightedPack, RandomShopTestContent.AnyMoment);
            offerCounts.Add(window.Offers.Count);
            if (window.Offers.Count == 1 && window.Offers[0].GroupId == heavyGroupId)
                heavyHits++;
        }

        // content weights 900_000 : 100_000 over sale_cnt 1 - the heavy group must dominate.
        await Assert.That(offerCounts.SetEquals([1])).IsTrue();
        await Assert.That(heavyHits > rolls * 0.85).IsTrue();
    }

    [Test]
    public async Task WindowDraw_SaleCntEqualToGroupCount_ShipsEveryGroupExactlyOnce()
    {
        var content = RandomShopTestContent.Build();
        var manager = new RandomMerchantManager();
        manager.UseStore(new InMemoryRandomShopStore());
        manager.UseContent(content);

        var pack = content[RandomShopTestContent.NoRefreshPack];
        await Assert.That(pack.SaleCnt).IsEqualTo(pack.EligibleGroups.Count);

        var window = manager.GetWindow(4242, RandomShopTestContent.NoRefreshPack, RandomShopTestContent.AnyMoment);
        await Assert.That(window.Offers.Count).IsEqualTo(pack.EligibleGroups.Count);
        await Assert.That(window.Offers.Select(offer => offer.GroupId).Distinct().Count()).IsEqualTo(pack.EligibleGroups.Count);
        // group_no order: slots follow the eligible groups' ordering.
        var slots = window.Offers.Select(offer => offer.Slot).OrderBy(slot => slot).ToArray();
        await Assert.That(slots.SequenceEqual([0, 1])).IsTrue();
    }
}
