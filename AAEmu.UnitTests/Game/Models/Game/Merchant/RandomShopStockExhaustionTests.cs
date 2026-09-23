using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 2 - stock and limit exhaustion: every offer slot sells exactly once (the store's
/// sold 0 -&gt; 1 claim is the gate), refresh allowances stop at the pack's content max for the
/// period, a refresh_use='f' pack never refreshes, and a failed payment gives both the claim and
/// the allowance back instead of leaking them.
/// </summary>
public class RandomShopStockExhaustionTests
{
    private static RandomMerchantManager NewManager(out InMemoryRandomShopStore store)
    {
        store = new InMemoryRandomShopStore();
        var manager = new RandomMerchantManager();
        manager.UseStore(store);
        manager.UseContent(RandomShopTestContent.Build());
        return manager;
    }

    [Test]
    public async Task BuyingEveryOffer_SellsEachSlotOnceThenRefuses()
    {
        var manager = NewManager(out var store);
        var window = manager.GetWindow(10, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        await Assert.That(window.Offers.Count).IsEqualTo(3);

        var paid = 0;
        foreach (var offer in window.Offers)
        {
            var result = manager.TryPurchase(10, RandomShopTestContent.RefreshablePack, offer.Slot,
                RandomShopTestContent.AnyMoment, () => { paid++; return true; });
            await Assert.That(result).IsEqualTo(RandomShopPurchaseResult.Purchased);
        }
        await Assert.That(paid).IsEqualTo(3);

        // Second sale of a sold slot: refused without charging again.
        var reSold = manager.TryPurchase(10, RandomShopTestContent.RefreshablePack, window.Offers[0].Slot,
            RandomShopTestContent.AnyMoment, () => { paid++; return true; });
        await Assert.That(reSold).IsEqualTo(RandomShopPurchaseResult.AlreadySold);
        await Assert.That(paid).IsEqualTo(3);

        // A slot that does not exist in the window is not a sale either.
        var missing = manager.TryPurchase(10, RandomShopTestContent.RefreshablePack, 99,
            RandomShopTestContent.AnyMoment, () => { paid++; return true; });
        await Assert.That(missing).IsEqualTo(RandomShopPurchaseResult.OfferNotFound);
        await Assert.That(paid).IsEqualTo(3);

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.Offers.All(offer => offer.Sold)).IsTrue();
    }

    [Test]
    public async Task FreeRefreshBudget_StopsAtThePackMaxForThePeriod()
    {
        var manager = NewManager(out var store);

        await Assert.That(manager.TryRefresh(11, RandomShopTestContent.RefreshablePack, true,
            RandomShopTestContent.AnyMoment)).IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(11, RandomShopTestContent.RefreshablePack, true,
            RandomShopTestContent.AnyMoment)).IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(11, RandomShopTestContent.RefreshablePack, true,
            RandomShopTestContent.AnyMoment)).IsEqualTo(RandomShopRefreshResult.CounterExhausted);

        var window = manager.GetWindow(11, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        await Assert.That(window.FreeUsed).IsEqualTo(2);
        await Assert.That(store.LoadAll().Single().FreeUsed).IsEqualTo(2);
    }

    [Test]
    public async Task PaidRefreshBudget_StopsAtThePackMaxAndOnlyChargesOnSuccess()
    {
        var manager = NewManager(out _);

        var charged = 0;
        Func<bool> charge = () => { charged++; return true; };

        await Assert.That(manager.TryRefresh(12, RandomShopTestContent.RefreshablePack, false,
            RandomShopTestContent.AnyMoment, charge)).IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(12, RandomShopTestContent.RefreshablePack, false,
            RandomShopTestContent.AnyMoment, charge)).IsEqualTo(RandomShopRefreshResult.CounterExhausted);

        // The exhausted attempt must fail before the charge callback runs.
        await Assert.That(charged).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshNotAllowedPack_NeverSpendsAnAllowance()
    {
        var manager = NewManager(out var store);
        var result = manager.TryRefresh(13, RandomShopTestContent.NoRefreshPack, true,
            RandomShopTestContent.AnyMoment);

        await Assert.That(result).IsEqualTo(RandomShopRefreshResult.RefreshNotAllowed);
        var persisted = store.LoadAll();
        await Assert.That(persisted.Count).IsEqualTo(0); // no window was even opened
    }

    [Test]
    public async Task FailedPayment_ReleasesTheClaimAndTheAllowance()
    {
        var manager = NewManager(out _);
        var window = manager.GetWindow(14, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        var slot = window.Offers[0].Slot;

        var refused = manager.TryPurchase(14, RandomShopTestContent.RefreshablePack, slot,
            RandomShopTestContent.AnyMoment, () => false);
        await Assert.That(refused).IsEqualTo(RandomShopPurchaseResult.PaymentFailed);

        // The released claim lets a later, funded attempt sell the slot exactly once.
        var charged = 0;
        var accepted = manager.TryPurchase(14, RandomShopTestContent.RefreshablePack, slot,
            RandomShopTestContent.AnyMoment, () => { charged++; return true; });
        await Assert.That(accepted).IsEqualTo(RandomShopPurchaseResult.Purchased);
        await Assert.That(charged).IsEqualTo(1);

        // Same shape for the paid refresh allowance: a refused charge must not burn a use.
        var refreshCharge = 0;
        await Assert.That(manager.TryRefresh(14, RandomShopTestContent.RefreshablePack, false,
            RandomShopTestContent.AnyMoment, () => { refreshCharge++; return false; }))
            .IsEqualTo(RandomShopRefreshResult.PaymentFailed);
        await Assert.That(manager.TryRefresh(14, RandomShopTestContent.RefreshablePack, false,
            RandomShopTestContent.AnyMoment, () => { refreshCharge++; return true; }))
            .IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(refreshCharge).IsEqualTo(2);
        var windowNow = manager.GetWindow(14, RandomShopTestContent.RefreshablePack, RandomShopTestContent.AnyMoment);
        await Assert.That(windowNow.ChargeUsed).IsEqualTo(1);
    }
}
