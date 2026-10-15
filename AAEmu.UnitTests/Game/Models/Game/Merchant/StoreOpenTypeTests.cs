using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.UnitTests.Game.Models.Game.Merchant;

public sealed class StoreOpenTypeTests
{
    [Test]
    [Arguments(StoreOpenType.LivingPoint, StoreOpenType.LivingPointShopConfig)]
    [Arguments(StoreOpenType.HonorPoint, StoreOpenType.HonorPointShopConfig)]
    [Arguments(StoreOpenType.Contribution, StoreOpenType.ContributionShopConfig)]
    [Arguments(StoreOpenType.Battlefield, StoreOpenType.BattlefieldShopConfig)]
    public async Task ConfigNameFor_MapsDirectOpenShopKinds(byte openType, string expected)
    {
        await Assert.That(StoreOpenType.ConfigNameFor(openType)).IsEqualTo(expected);
    }

    [Test]
    public async Task ConfigNameFor_IgnoresNormalAndUnknownOpenTypes()
    {
        await Assert.That(StoreOpenType.ConfigNameFor(StoreOpenType.Normal)).IsNull();
        await Assert.That(StoreOpenType.ConfigNameFor(255)).IsNull();
    }
}
