using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

/// <summary>
/// Suite 6 - missing or broken content fails loudly: no pack row, a refused pack
/// (refresh_multiply_use='t', unknown kind_id, unparsable flag, no drawable group) and an
/// unknown refresh currency on a paid refresh all raise RandomMerchantContentException instead
/// of falling back to any default number.
/// </summary>
public class RandomShopMissingContentTests
{
    private static RandomMerchantManager ManagerOver(IReadOnlyDictionary<uint, RandomMerchantPack> content)
    {
        var manager = new RandomMerchantManager();
        manager.UseStore(new InMemoryRandomShopStore());
        manager.UseContent(content);
        return manager;
    }

    [Test]
    public async Task RefusedPacks_AreMarkedUnusableByTheBuilder()
    {
        var content = RandomShopTestContent.Build();

        await Assert.That(content[RandomShopTestContent.RefreshablePack].Usable).IsTrue();
        await Assert.That(content[RandomShopTestContent.MultiplyRefusedPack].Usable).IsFalse();
        await Assert.That(content[RandomShopTestContent.UnknownKindPack].Usable).IsFalse();
        await Assert.That(content[RandomShopTestContent.MultiplyRefusedPack].RefreshMultiplyUse).IsTrue();
        await Assert.That((object)content[RandomShopTestContent.UnknownKindPack].Currency is null).IsTrue();
    }

    [Test]
    public async Task RefreshMultiplyUseT_WindowRefusalIsLoud()
    {
        var manager = ManagerOver(RandomShopTestContent.Build());
        await Assert.That(() => manager.GetWindow(1, RandomShopTestContent.MultiplyRefusedPack,
            RandomShopTestContent.AnyMoment)).Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task UnknownPackKind_WindowRefusalIsLoud()
    {
        var manager = ManagerOver(RandomShopTestContent.Build());
        await Assert.That(() => manager.GetWindow(1, RandomShopTestContent.UnknownKindPack,
            RandomShopTestContent.AnyMoment)).Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task MissingPackRow_WindowRefusalIsLoud()
    {
        var manager = ManagerOver(RandomShopTestContent.Build());
        await Assert.That(() => manager.GetWindow(1, 9999, RandomShopTestContent.AnyMoment))
            .Throws<RandomMerchantContentException>();
        await Assert.That(() => manager.TryRefresh(1, 9999, true, RandomShopTestContent.AnyMoment))
            .Throws<RandomMerchantContentException>();
        await Assert.That(() => manager.TryPurchase(1, 9999, 0, RandomShopTestContent.AnyMoment, () => true))
            .Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task UnloadedContent_GameDataMissingIsLoudToo()
    {
        // No UseContent: the manager falls through to RandomMerchantGameData.Instance, which has
        // loaded nothing in this test process - still a loud refusal, never a synthetic pack.
        var manager = new RandomMerchantManager();
        manager.UseStore(new InMemoryRandomShopStore());
        await Assert.That(() => manager.GetWindow(1, 1, RandomShopTestContent.AnyMoment))
            .Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task UnparsableRefreshFlag_IsRefused()
    {
        var packs = RandomShopTestContent.PackRows();
        packs[0].RefreshUse = "sometimes"; // not an exact 't'/'f' content boolean

        var content = RandomMerchantContentBuilder.Build(packs, RandomShopTestContent.GroupRows(),
            RandomShopTestContent.GoodRows());
        await Assert.That(content[RandomShopTestContent.RefreshablePack].Usable).IsFalse();

        var manager = ManagerOver(content);
        await Assert.That(() => manager.GetWindow(1, RandomShopTestContent.RefreshablePack,
            RandomShopTestContent.AnyMoment)).Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task PackWithoutADrawableGroup_IsRefused()
    {
        var packs = RandomShopTestContent.PackRows();
        packs.RemoveAll(row => row.Id == RandomShopTestContent.RefreshablePack);

        var content = RandomMerchantContentBuilder.Build(packs, [], RandomShopTestContent.GoodRows());
        // No groups at all: the remaining packs cannot yield an offer either.
        await Assert.That(content.Values.All(pack => !pack.Usable)).IsTrue();

        var manager = ManagerOver(content);
        await Assert.That(() => manager.GetWindow(1, RandomShopTestContent.WeightedPack,
            RandomShopTestContent.AnyMoment)).Throws<RandomMerchantContentException>();
    }

    [Test]
    public async Task PaidRefreshWithUnknownRefreshCurrency_IsLoud()
    {
        var packs = RandomShopTestContent.PackRows();
        packs[0].RefreshCurrencyId = 42; // not any ContentCurrencyType member

        var content = RandomMerchantContentBuilder.Build(packs, RandomShopTestContent.GroupRows(),
            RandomShopTestContent.GoodRows());
        var pack = content[RandomShopTestContent.RefreshablePack];
        await Assert.That(pack.RefreshCurrency is null).IsTrue();
        // Free refreshes keep working; only the paid path refuses.
        await Assert.That(pack.Usable).IsTrue();

        var manager = ManagerOver(content);
        await Assert.That(() => manager.TryRefresh(1, RandomShopTestContent.RefreshablePack, false,
                RandomShopTestContent.AnyMoment, () => true))
            .Throws<RandomMerchantContentException>();
        await Assert.That(manager.TryRefresh(1, RandomShopTestContent.RefreshablePack, true,
                RandomShopTestContent.AnyMoment))
            .IsEqualTo(RandomShopRefreshResult.Refreshed);
    }
}
