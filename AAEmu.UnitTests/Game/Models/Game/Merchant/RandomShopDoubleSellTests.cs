using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 4 - concurrent double-sell refusal: parallel buys of one slot race the durable
/// sold 0 -&gt; 1 claim outside the window lock, exactly one wins, exactly one payment callback
/// runs, and the counter spend caps at the content max under contention.
/// </summary>
public class RandomShopDoubleSellTests
{
    [Test]
    public async Task ParallelBuysOfOneSlot_ChargeExactlyOnce()
    {
        var store = new InMemoryRandomShopStore();
        var manager = new RandomMerchantManager();
        manager.UseStore(store);
        manager.UseContent(RandomShopTestContent.Build());

        var window = manager.GetWindow(30, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        var slot = window.Offers[0].Slot;

        var charges = 0;
        var racers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            var result = manager.TryPurchase(30, RandomShopTestContent.RefreshablePack, slot,
                RandomShopTestContent.AnyMoment,
                () =>
                {
                    Interlocked.Increment(ref charges);
                    return true;
                });
            return result;
        })).ToArray();

        var results = await Task.WhenAll(racers);

        await Assert.That(results.Count(result => result == RandomShopPurchaseResult.Purchased)).IsEqualTo(1);
        await Assert.That(results.Count(result => result == RandomShopPurchaseResult.AlreadySold)).IsEqualTo(15);
        await Assert.That(charges).IsEqualTo(1);
        await Assert.That(window.Offers.First(offer => offer.Slot == slot).Sold).IsTrue();

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.Offers.Single(offer => offer.Slot == slot).Sold).IsTrue();
    }

    [Test]
    public async Task ParallelBuysOfDifferentSlots_EachSellExactlyOnce()
    {
        var manager = new RandomMerchantManager();
        manager.UseStore(new InMemoryRandomShopStore());
        manager.UseContent(RandomShopTestContent.Build());

        var window = manager.GetWindow(31, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        var slots = window.Offers.Select(offer => offer.Slot).ToArray();

        var charges = 0;
        var racers = slots.SelectMany(slot => Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            manager.TryPurchase(31, RandomShopTestContent.RefreshablePack, slot,
                RandomShopTestContent.AnyMoment,
                () =>
                {
                    Interlocked.Increment(ref charges);
                    return true;
                })))).ToArray();

        var results = await Task.WhenAll(racers);

        await Assert.That(results.Count(result => result == RandomShopPurchaseResult.Purchased)).IsEqualTo(slots.Length);
        await Assert.That(charges).IsEqualTo(slots.Length);
        await Assert.That(window.Offers.All(offer => offer.Sold)).IsTrue();
    }

    [Test]
    public async Task ParallelRefreshSpend_CapsAtTheContentMax()
    {
        var store = new InMemoryRandomShopStore();
        var manager = new RandomMerchantManager();
        manager.UseStore(store);
        manager.UseContent(RandomShopTestContent.Build());
        var window = manager.GetWindow(32, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);

        var allowed = RandomShopTestContent.PackRows()
            .Single(row => row.Id == RandomShopTestContent.RefreshablePack).RefreshFreeCnt;
        await Assert.That(allowed).IsEqualTo(2);

        var racers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            store.TrySpendRefresh(32, RandomShopTestContent.RefreshablePack, window.PeriodStart, true, allowed)))
            .ToArray();
        var results = await Task.WhenAll(racers);

        await Assert.That(results.Count(won => won)).IsEqualTo(allowed);
        await Assert.That(store.LoadAll().Single().FreeUsed).IsEqualTo(allowed);
    }
}
