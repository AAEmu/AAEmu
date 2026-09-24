using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite - reopen-box selection: every draw comes from the pack's own two-stage weighted
/// content (tier by group weight, then good inside the tier), the caller's injected rng is the
/// only randomness, and an unusable or missing pack draws nothing instead of falling back.
/// </summary>
public class ReopenBoxSelectionTests
{
    private static ReopenBoxManager NewManager(out InMemoryReopenBoxStore store, params MerchantReopenPack[] packs)
    {
        store = new InMemoryReopenBoxStore();
        var manager = new ReopenBoxManager();
        manager.UseStore(store);
        manager.UseContent(ReopenBoxTestContent.Content(packs));
        return manager;
    }

    [Test]
    public async Task Draw_AlwaysLandsInsideTheRequestedPackContent()
    {
        var pack = ReopenBoxTestContent.Pack(id: 7, groupCount: 3, goodsPerGroup: 3);
        var allowedGroupIds = pack.Groups.Select(group => group.Id).ToHashSet();
        var allowedGoods = pack.Groups.SelectMany(group => group.Goods).ToList();

        for (var i = 0; i < 200; i++)
        {
            var good = MerchantReopenPackGameData.Roll(pack, Random.Shared);
            await Assert.That(good).IsNotNull();
            await Assert.That(allowedGoods.Contains(good)).IsTrue();
        }
    }

    [Test]
    public async Task Refresh_RollsAWeightedDrawAndPersistsItsIdentity()
    {
        var pack = ReopenBoxTestContent.Pack(id: 3);
        var manager = NewManager(out var store, pack);

        var result = manager.TryRefresh(40, 9001, 3, false, ReopenBoxTestContent.Moment);

        await Assert.That(result).IsEqualTo(ReopenRefreshResult.Refreshed);
        var state = manager.TryGetState(40, 9001);
        await Assert.That(state).IsNotNull();
        await Assert.That(state.GoodId != 0).IsTrue();
        await Assert.That(pack.Groups.Any(group => group.Id == state.GroupId)).IsTrue();
        await Assert.That(state.Settled).IsFalse();

        var persisted = store.LoadAll().Single();
        await Assert.That(persisted.GoodId).IsEqualTo(state.GoodId);
        await Assert.That(persisted.GroupId).IsEqualTo(state.GroupId);
    }

    [Test]
    public async Task UnusablePack_IsRefusedLoudlyAndDrawsNothing()
    {
        var pack = ReopenBoxTestContent.Pack(id: 5, usable: false);
        var manager = NewManager(out _, pack);

        await Assert.That(() => manager.TryRefresh(41, 9002, 5, false, ReopenBoxTestContent.Moment))
            .Throws<RandomMerchantContentException>();
        await Assert.That(MerchantReopenPackGameData.Roll(pack, Random.Shared)).IsNull();
    }

    [Test]
    public async Task MissingPackRow_IsRefusedLoudly()
    {
        var manager = NewManager(out _, ReopenBoxTestContent.Pack(id: 1));

        await Assert.That(() => manager.TryRefresh(42, 9003, 99, false, ReopenBoxTestContent.Moment))
            .Throws<RandomMerchantContentException>();
    }
}
