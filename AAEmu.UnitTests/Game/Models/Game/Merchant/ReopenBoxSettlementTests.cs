using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite - reopen-box settlement: one roll's reward is granted exactly once - the durable
/// settled 0 -&gt; 1 claim is the gate, it survives a restart, and a grant that fails releases
/// the claim so the roll stays claimable rather than stranding it.
/// </summary>
public class ReopenBoxSettlementTests
{
    private static ReopenBoxManager NewManager(out InMemoryReopenBoxStore store)
    {
        store = new InMemoryReopenBoxStore();
        var pack = ReopenBoxTestContent.Pack(id: 4, freeCount: 5, chargeCount: 5, lifeTime: 0);
        var manager = new ReopenBoxManager();
        manager.UseStore(store);
        manager.UseContent(ReopenBoxTestContent.Content(pack));
        return manager;
    }

    private static ReopenBoxManager RolledManager(out InMemoryReopenBoxStore store, out long itemId)
    {
        var manager = NewManager(out store);
        itemId = 700_001;
        var result = manager.TryRefresh(60, itemId, 4, false, ReopenBoxTestContent.Moment);
        if (result != ReopenRefreshResult.Refreshed)
            throw new InvalidOperationException($"fixture roll failed: {result}");
        return manager;
    }

    [Test]
    public async Task ClaimBeforeAnyRoll_IsRefused()
    {
        var manager = NewManager(out _);
        var result = manager.TryClaim(61, 700_002, ReopenBoxTestContent.Moment, _ => true);
        await Assert.That(result).IsEqualTo(ReopenClaimResult.NotRolled);
    }

    [Test]
    public async Task Claim_GrantsOnceThenRefusesTheSecondClaim()
    {
        var manager = RolledManager(out var store, out var itemId);
        var grants = 0;

        var first = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => { grants++; return true; });
        var second = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => { grants++; return true; });

        await Assert.That(first).IsEqualTo(ReopenClaimResult.Claimed);
        await Assert.That(second).IsEqualTo(ReopenClaimResult.AlreadySettled);
        await Assert.That(grants).IsEqualTo(1);
        await Assert.That(manager.TryGetState(60, itemId).Settled).IsTrue();
        await Assert.That(store.LoadAll().Single().Settled).IsTrue();
    }

    [Test]
    public async Task SettledClaim_SurvivesARestartFromTheStore()
    {
        var manager = RolledManager(out var store, out var itemId);
        await Assert.That(manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => true))
            .IsEqualTo(ReopenClaimResult.Claimed);

        // A fresh manager over the same durable store - the World-restart shape.
        var restarted = new ReopenBoxManager();
        restarted.UseStore(store);
        restarted.LoadFromStore();

        var grants = 0;
        var result = restarted.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => { grants++; return true; });
        await Assert.That(result).IsEqualTo(ReopenClaimResult.AlreadySettled);
        await Assert.That(grants).IsEqualTo(0);
    }

    [Test]
    public async Task RefusedGrant_ReleasesTheClaimSoTheRollStaysClaimable()
    {
        var manager = RolledManager(out var store, out var itemId);

        var refused = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => false);
        await Assert.That(refused).IsEqualTo(ReopenClaimResult.GrantFailed);
        await Assert.That(store.LoadAll().Single().Settled).IsFalse();

        var granted = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => true);
        await Assert.That(granted).IsEqualTo(ReopenClaimResult.Claimed);
    }

    [Test]
    public async Task ThrowingGrant_ReleasesTheClaimAndPropagates()
    {
        var manager = RolledManager(out var store, out var itemId);

        await Assert.That(() => manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment,
                _ => throw new InvalidOperationException("bag refused")))
            .Throws<InvalidOperationException>();

        await Assert.That(store.LoadAll().Single().Settled).IsFalse();
        var result = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => true);
        await Assert.That(result).IsEqualTo(ReopenClaimResult.Claimed);
    }

    [Test]
    public async Task TwoRacingClaims_ExactlyOneGrants()
    {
        var manager = RolledManager(out var store, out var itemId);
        var grants = 0;

        // Both racers reach the durable claim; the conditional flip lets exactly one through.
        var results = new[]
        {
            manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => { grants++; return true; }),
            manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => { grants++; return true; })
        };

        await Assert.That(results.Count(r => r == ReopenClaimResult.Claimed)).IsEqualTo(1);
        await Assert.That(results.Count(r => r == ReopenClaimResult.AlreadySettled)).IsEqualTo(1);
        await Assert.That(grants).IsEqualTo(1);
        await Assert.That(store.LoadAll().Single().Settled).IsTrue();
    }

    [Test]
    public async Task NextRoll_ClearsTheClaimFlagForTheNewDraw()
    {
        var manager = RolledManager(out _, out var itemId);
        await Assert.That(manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment, _ => true))
            .IsEqualTo(ReopenClaimResult.Claimed);

        var rerolled = manager.TryRefresh(60, itemId, 4, false, ReopenBoxTestContent.Moment.AddMinutes(1));
        await Assert.That(rerolled).IsEqualTo(ReopenRefreshResult.Refreshed);
        await Assert.That(manager.TryGetState(60, itemId).Settled).IsFalse();

        var result = manager.TryClaim(60, itemId, ReopenBoxTestContent.Moment.AddMinutes(1), _ => true);
        await Assert.That(result).IsEqualTo(ReopenClaimResult.Claimed);
    }
}
