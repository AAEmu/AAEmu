using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 3 - refresh resets state: a refresh spends one allowance, re-rolls the offers (sold
/// flags cleared, record time advanced) and persists both, and the next UTC day resets the
/// window without any refresh (ServerCalendar daily period, persisted period_start).
/// </summary>
public class RandomShopRefreshStateTests
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
    public async Task Refresh_RerollsOffersClearsSoldFlagsAndPersistsTheCounters()
    {
        var manager = NewManager(out var store);
        var rolledAt = RandomShopTestContent.AnyMoment;
        var window = manager.GetWindow(20, RandomShopTestContent.RefreshablePack, rolledAt);
        var soldSlot = window.Offers[0].Slot;
        var offerKey = (window.Offers[0].GroupId, window.Offers[0].GoodId);

        var bought = manager.TryPurchase(20, RandomShopTestContent.RefreshablePack, soldSlot,
            rolledAt, () => true);
        await Assert.That(bought).IsEqualTo(RandomShopPurchaseResult.Purchased);

        var refreshedAt = rolledAt.AddMinutes(5);
        var result = manager.TryRefresh(20, RandomShopTestContent.RefreshablePack, true, refreshedAt);
        await Assert.That(result).IsEqualTo(RandomShopRefreshResult.Refreshed);

        await Assert.That(window.FreeUsed).IsEqualTo(1);
        await Assert.That(window.RolledAt).IsEqualTo(refreshedAt);
        await Assert.That(window.Offers.All(offer => !offer.Sold)).IsTrue();
        await Assert.That(window.Offers.Count).IsEqualTo(3);

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.FreeUsed).IsEqualTo(1);
        await Assert.That(persisted.RolledAt).IsEqualTo(refreshedAt);
        await Assert.That(persisted.Offers.All(offer => !offer.Sold)).IsTrue();
        await Assert.That(persisted.Offers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task NextUtcDay_ResetsTheWindowWithoutARefresh()
    {
        var manager = NewManager(out var store);
        var day1 = new DateTime(2026, 9, 23, 21, 30, 0, DateTimeKind.Utc);
        var window = manager.GetWindow(21, RandomShopTestContent.RefreshablePack, day1);
        var soldSlot = window.Offers[0].Slot;
        manager.TryPurchase(21, RandomShopTestContent.RefreshablePack, soldSlot, day1, () => true);
        manager.TryRefresh(21, RandomShopTestContent.RefreshablePack, true, day1);

        await Assert.That(window.PeriodStart).IsEqualTo(day1.Date);
        await Assert.That(window.FreeUsed).IsEqualTo(1);

        var day2 = day1.AddDays(1);
        var reloaded = manager.GetWindow(21, RandomShopTestContent.RefreshablePack, day2);

        await Assert.That(reloaded.PeriodStart).IsEqualTo(day2.Date);
        await Assert.That(reloaded.FreeUsed).IsEqualTo(0);
        await Assert.That(reloaded.ChargeUsed).IsEqualTo(0);
        await Assert.That(reloaded.RolledAt).IsEqualTo(day2);
        await Assert.That(reloaded.Offers.All(offer => !offer.Sold)).IsTrue();
        await Assert.That(reloaded.Offers.Count).IsEqualTo(3);

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.PeriodStart).IsEqualTo(day2.Date);
        await Assert.That(persisted.FreeUsed).IsEqualTo(0);
        await Assert.That(persisted.Offers.All(offer => !offer.Sold)).IsTrue();

        // The fresh day's allowance budget is spendable again (2 free refreshes per period).
        await Assert.That(manager.TryRefresh(21, RandomShopTestContent.RefreshablePack, true, day2))
            .IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(21, RandomShopTestContent.RefreshablePack, true, day2))
            .IsEqualTo(RandomShopRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(21, RandomShopTestContent.RefreshablePack, true, day2))
            .IsEqualTo(RandomShopRefreshResult.CounterExhausted);
    }

    [Test]
    public async Task WindowPeriod_IsTheUtcMidnightOfTheRequestedMoment()
    {
        var manager = NewManager(out _);
        var lateEvening = new DateTime(2026, 9, 23, 23, 59, 59, DateTimeKind.Utc);
        var window = manager.GetWindow(22, RandomShopTestContent.RefreshablePack, lateEvening);

        await Assert.That(window.PeriodStart.Date).IsEqualTo(new DateTime(2026, 9, 23));
        await Assert.That(window.PeriodStart.Kind).IsEqualTo(DateTimeKind.Utc);
    }
}
