using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite - reopen-box limits: every roll spends one allowance from the pack's content budgets
/// (free_count / charge_count), the life_time cooldown blocks the next roll until it elapses,
/// and a refused payment or a refused roll gives the allowance back instead of leaking it.
/// </summary>
public class ReopenBoxLimitTests
{
    private static ReopenBoxManager NewManager(
        out InMemoryReopenBoxStore store, out AAEmu.Game.GameData.MerchantReopenPack pack)
    {
        store = new InMemoryReopenBoxStore();
        pack = ReopenBoxTestContent.Pack(id: 2, freeCount: 2, chargeCount: 1, lifeTime: 60);
        var manager = new ReopenBoxManager();
        manager.UseStore(store);
        manager.UseContent(ReopenBoxTestContent.Content(pack));
        return manager;
    }

    [Test]
    public async Task FreeBudget_SpendsToTheContentMaxThenRefuses()
    {
        var manager = NewManager(out var store, out _);
        var now = ReopenBoxTestContent.Moment;

        await Assert.That(manager.TryRefresh(50, 8001, 2, false, now))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        await Assert.That(manager.TryGetState(50, 8001).FreeUsed).IsEqualTo(0);

        var later = now.AddMinutes(1);
        await Assert.That(manager.TryRefresh(50, 8001, 2, false, later))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        var later2 = later.AddMinutes(1);
        await Assert.That(manager.TryRefresh(50, 8001, 2, false, later2))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(50, 8001, 2, false, later2.AddMinutes(1)))
            .IsEqualTo(ReopenRefreshResult.CounterExhausted);

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.FreeUsed).IsEqualTo(2);
        await Assert.That(persisted.ChargeUsed).IsEqualTo(0);
    }

    [Test]
    public async Task PaidBudget_SpendsToTheContentMaxThenRefuses()
    {
        var manager = NewManager(out _, out _);
        var now = ReopenBoxTestContent.Moment;
        var later = now.AddMinutes(1);

        await Assert.That(manager.TryRefresh(51, 8002, 2, true, now, () => true))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        await Assert.That(manager.TryRefresh(51, 8002, 2, true, later, () => true))
            .IsEqualTo(ReopenRefreshResult.CounterExhausted);
    }

    [Test]
    public async Task RefusedPayment_ReleasesTheAllowanceAndKeepsThePreviousRoll()
    {
        var manager = NewManager(out var store, out _);
        // Two paid opens, so the second spend is allowed and the refused payment can give it back.
        manager.UseContent(ReopenBoxTestContent.Content(
            ReopenBoxTestContent.Pack(id: 2, freeCount: 2, chargeCount: 2, lifeTime: 60)));
        var now = ReopenBoxTestContent.Moment;
        await Assert.That(manager.TryRefresh(52, 8003, 2, true, now, () => true))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        var firstGood = manager.TryGetState(52, 8003).GoodId;

        var result = manager.TryRefresh(52, 8003, 2, true, now.AddMinutes(1), () => false);

        await Assert.That(result).IsEqualTo(ReopenRefreshResult.PaymentFailed);
        await Assert.That(manager.TryGetState(52, 8003).GoodId).IsEqualTo(firstGood);
        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.ChargeUsed).IsEqualTo(1); // the refused spend was released
    }

    [Test]
    public async Task LifeTime_ClosesTheBoxInsteadOfSpacingRolls()
    {
        var manager = NewManager(out var store, out _);
        var now = ReopenBoxTestContent.Moment;
        await Assert.That(manager.TryRefresh(53, 8004, 2, false, now))
            .IsEqualTo(ReopenRefreshResult.Refreshed);

        var delivered = new List<ReopenBoxState>();
        await Assert.That(manager.TryRefresh(53, 8004, 2, false, now.AddMinutes(59)))
            .IsEqualTo(ReopenRefreshResult.Refreshed);
        var beforeExpiry = manager.TryGetState(53, 8004).GoodId;
        await Assert.That(manager.TryRefresh(53, 8004, 2, false, now.AddMinutes(60)))
            .IsEqualTo(ReopenRefreshResult.Expired);
        await Assert.That(manager.TryGetState(53, 8004).GoodId).IsEqualTo(beforeExpiry);

        await Assert.That(manager.TryRefresh(53, 8004, 2, false, now.AddMinutes(60),
                settleExpired: expired =>
                {
                    delivered.Add(expired);
                    return false;
                }))
            .IsEqualTo(ReopenRefreshResult.Expired);
        await Assert.That(delivered.Count).IsEqualTo(1);
        await Assert.That(store.LoadAll().Any(row => row.ItemId == 8004)).IsFalse();
    }

    [Test]
    public async Task WrongPack_ForTheSameBoxItem_IsRefusedLoudly()
    {
        var manager = NewManager(out _, out _);
        var now = ReopenBoxTestContent.Moment;
        await Assert.That(manager.TryRefresh(54, 8005, 2, false, now))
            .IsEqualTo(ReopenRefreshResult.Refreshed);

        await Assert.That(() => manager.TryRefresh(54, 8005, 99, false, now.AddMinutes(1)))
            .Throws<RandomMerchantContentException>();
    }
}
