using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 5 - persistence across restart: everything a window is (period, rolled-at, refresh
/// counters, offers with their cost snapshot, sold flags) comes back from the store's LoadAll
/// through a fresh manager instance, and a reloaded sold slot cannot be bought again.
/// </summary>
public class RandomShopPersistenceTests
{
    [Test]
    public async Task WindowSaleAndRefreshSurviveARestart()
    {
        var store = new InMemoryRandomShopStore();
        var content = RandomShopTestContent.Build();

        var day = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc);
        var rolledAt = day.AddMinutes(1);
        var first = new RandomMerchantManager();
        first.UseStore(store);
        first.UseContent(content);

        var window = first.GetWindow(40, RandomShopTestContent.RefreshablePack, rolledAt);
        await Assert.That(first.TryRefresh(40, RandomShopTestContent.RefreshablePack, true, rolledAt))
            .IsEqualTo(RandomShopRefreshResult.Refreshed);

        var slot = window.Offers[0].Slot;
        var costSnapshot = window.Offers[0].Cost;
        var boughtAt = rolledAt.AddMinutes(2);
        await Assert.That(first.TryPurchase(40, RandomShopTestContent.RefreshablePack, slot, boughtAt, () => true))
            .IsEqualTo(RandomShopPurchaseResult.Purchased);

        // Restart: a brand-new manager over the same durable store.
        var restarted = new RandomMerchantManager();
        restarted.UseStore(store);
        restarted.UseContent(content);
        restarted.LoadFromStore();

        var reloaded = restarted.GetWindow(40, RandomShopTestContent.RefreshablePack, boughtAt);
        await Assert.That(reloaded.PeriodStart).IsEqualTo(day.Date);
        await Assert.That(reloaded.RolledAt).IsEqualTo(rolledAt); // no re-roll: the stored window was reused
        await Assert.That(reloaded.FreeUsed).IsEqualTo(1);
        await Assert.That(reloaded.Offers.Count).IsEqualTo(window.Offers.Count);
        await Assert.That(reloaded.Offers.Single(offer => offer.Slot == slot).Sold).IsTrue();
        await Assert.That(reloaded.Offers.Single(offer => offer.Slot == slot).Cost).IsEqualTo(costSnapshot);

        // The sold slot stays sold: the restarted manager refuses without charging.
        var charged = 0;
        await Assert.That(restarted.TryPurchase(40, RandomShopTestContent.RefreshablePack, slot, boughtAt,
                () => { charged++; return true; }))
            .IsEqualTo(RandomShopPurchaseResult.AlreadySold);
        await Assert.That(charged).IsEqualTo(0);

        // An unsold slot is still for sale after the restart.
        var openSlot = reloaded.Offers.First(offer => !offer.Sold).Slot;
        await Assert.That(restarted.TryPurchase(40, RandomShopTestContent.RefreshablePack, openSlot, boughtAt,
                () => true))
            .IsEqualTo(RandomShopPurchaseResult.Purchased);
    }

    [Test]
    public async Task RestartIntoANewDay_LoadsTheStaleWindowAndRollsItLazilyOnFirstUse()
    {
        var store = new InMemoryRandomShopStore();
        var content = RandomShopTestContent.Build();

        var day1 = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        var first = new RandomMerchantManager();
        first.UseStore(store);
        first.UseContent(content);
        first.GetWindow(41, RandomShopTestContent.RefreshablePack, day1);
        first.TryRefresh(41, RandomShopTestContent.RefreshablePack, true, day1);

        var day2 = day1.AddDays(1);
        var restarted = new RandomMerchantManager();
        restarted.UseStore(store);
        restarted.UseContent(content);
        restarted.LoadFromStore();

        var reloaded = restarted.GetWindow(41, RandomShopTestContent.RefreshablePack, day2);
        await Assert.That(reloaded.PeriodStart).IsEqualTo(day2.Date);
        await Assert.That(reloaded.FreeUsed).IsEqualTo(0);
        await Assert.That(reloaded.RolledAt).IsEqualTo(day2);
        await Assert.That(store.LoadAll().Single().PeriodStart).IsEqualTo(day2.Date);
        await Assert.That(store.LoadAll().Single().FreeUsed).IsEqualTo(0);
    }

    [Test]
    public async Task StoreRoundTrip_KeepsCountersAndOfferQuotes()
    {
        var store = new InMemoryRandomShopStore();
        var manager = new RandomMerchantManager();
        manager.UseStore(store);
        manager.UseContent(RandomShopTestContent.Build());

        var moment = new DateTime(2026, 9, 23, 15, 0, 0, DateTimeKind.Utc);
        var window = manager.GetWindow(43, RandomShopTestContent.RefreshablePack, moment);
        manager.TryRefresh(43, RandomShopTestContent.RefreshablePack, true, moment);

        var roundTripped = store.LoadAll().Single();
        await Assert.That(roundTripped.CharacterId).IsEqualTo(43u);
        await Assert.That(roundTripped.PackId).IsEqualTo(RandomShopTestContent.RefreshablePack);
        await Assert.That(roundTripped.PeriodStart).IsEqualTo(moment.Date);
        await Assert.That(roundTripped.FreeUsed).IsEqualTo(1);
        await Assert.That(roundTripped.ChargeUsed).IsEqualTo(0);

        // Every quote comes from the content rows: cost snapshot and currency ride the offer.
        await Assert.That(roundTripped.Offers.Count).IsEqualTo(3);
        await Assert.That(roundTripped.Offers.All(offer => offer.Cost > 0)).IsTrue();
        await Assert.That(roundTripped.Offers.All(offer => offer.Currency == window.Offers[0].Currency)).IsTrue();

        // The durable copy is a deep copy: mutating the live window cannot rewrite history.
        window.FreeUsed = 99;
        await Assert.That(store.LoadAll().Single().FreeUsed).IsEqualTo(1);
    }
}
