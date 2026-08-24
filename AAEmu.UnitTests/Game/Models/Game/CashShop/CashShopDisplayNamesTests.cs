using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

public class CashShopDisplayNamesTests
{
    [Test]
    [Arguments(null, true)]
    [Arguments("", true)]
    [Arguments("   ", true)]
    [Arguments("Premium #2000050", true)]
    [Arguments("premium #1", true)]
    [Arguments("Starter Pack", false)]
    [Arguments("P2W 10g (sell to vendor)", false)]
    public async Task NeedsResolvedName_MatchesPlaceholderAndEmpty(string? name, bool expected) =>
        await Assert.That(CashShopDisplayNames.NeedsResolvedName(name)).IsEqualTo(expected);

    [Test]
    public async Task ResolveItemTemplateId_PrefersDisplayItemId()
    {
        await Assert.That(CashShopDisplayNames.ResolveItemTemplateId(100, 200)).IsEqualTo(100u);
        await Assert.That(CashShopDisplayNames.ResolveItemTemplateId(0, 200)).IsEqualTo(200u);
    }
}
