using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Board persistence: expiry is a unix comparison, load keeps only live rows, and a sweep
/// removes a lapsed listing from both memory and the store.
/// </summary>
public class CraftOrderPersistRulesTests
{
    private static CraftOrder Order(ulong id, long expiresUnix, uint ownerId = 8) => new()
    {
        Id = id,
        OwnerId = ownerId,
        OwnerName = "Tester",
        CraftId = 5591,
        ItemId = 27902,
        Count = 1,
        Fee = 100_009,
        ExpiresUnix = expiresUnix,
        PostedUnix = expiresUnix - 172_800
    };

    [Test]
    public async Task Expired_IsAtOrBeforeNow()
    {
        await Assert.That(CraftOrderPersistRules.IsExpired(100, 100)).IsTrue();
        await Assert.That(CraftOrderPersistRules.IsExpired(99, 100)).IsTrue();
        await Assert.That(CraftOrderPersistRules.IsExpired(101, 100)).IsFalse();
        await Assert.That(CraftOrderPersistRules.IsExpired(null, 100)).IsFalse();
    }

    [Test]
    public async Task NextId_IsOnePastTheHighestLoadedRow()
    {
        await Assert.That(CraftOrderPersistRules.NextId([])).IsEqualTo(1ul);
        await Assert.That(CraftOrderPersistRules.NextId(null)).IsEqualTo(1ul);
        await Assert.That(CraftOrderPersistRules.NextId([1, 4, 2])).IsEqualTo(5ul);
    }

    [Test]
    public async Task KeepOnLoad_DropsRowsThatHaveLapsed()
    {
        var live = Order(2, expiresUnix: 200);
        var dead = Order(1, expiresUnix: 100);
        var kept = CraftOrderPersistRules.KeepOnLoad([dead, live], nowUnix: 150);

        await Assert.That(kept.Count).IsEqualTo(1);
        await Assert.That(kept[0].Id).IsEqualTo(2ul);
    }

    [Test]
    public async Task InMemoryStore_RoundTripsARow()
    {
        var store = new InMemoryCraftOrderStore();
        var order = Order(3, expiresUnix: 500);
        await Assert.That(store.Insert(order)).IsTrue();
        await Assert.That(store.LoadAll().Count).IsEqualTo(1);
        await Assert.That(store.Delete(3)).IsTrue();
        await Assert.That(store.LoadAll().Count).IsEqualTo(0);
    }

    [Test]
    public async Task NextSweepDelay_RetriesPastDueInsteadOfArmingImmediately()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);
        await Assert.That(CraftOrderPersistRules.NextSweepDelay(now, null)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(CraftOrderPersistRules.NextSweepDelay(now, [])).IsEqualTo(TimeSpan.Zero);
        await Assert.That(CraftOrderPersistRules.NextSweepDelay(now, [900])).IsEqualTo(CraftOrderPersistRules.ExpireRetry);
        await Assert.That(CraftOrderPersistRules.NextSweepDelay(now, [900, 1_100]))
            .IsEqualTo(CraftOrderPersistRules.ExpireRetry);
        await Assert.That(CraftOrderPersistRules.NextSweepDelay(now, [1_100]))
            .IsEqualTo(TimeSpan.FromSeconds(100));
        await Assert.That(CraftOrderPersistRules.ExpireRetry).IsEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task Sweep_RemovesAnExpiredRowFromMemoryAndStore()
    {
        var manager = CraftOrderManager.Instance;
        manager.SkipExpiredMail = true;
        manager.Clear();
        manager.UseStore(new InMemoryCraftOrderStore());

        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);
        manager.ImportForTest(Order(1, expiresUnix: 900));
        manager.ImportForTest(Order(2, expiresUnix: 1_100));

        manager.SweepExpired(now);

        await Assert.That(manager.Orders.Count).IsEqualTo(1);
        await Assert.That(manager.Orders.Single().Id).IsEqualTo(2ul);

        manager.Clear();
        manager.SkipExpiredMail = false;
    }
}
