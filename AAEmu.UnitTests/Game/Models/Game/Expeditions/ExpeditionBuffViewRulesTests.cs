using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class ExpeditionBuffViewRulesTests
{
    [Test]
    public async Task Resolve_PrefersRequestedListedGuildId() =>
        await Assert.That(ExpeditionBuffViewRules.ResolveExpeditionId(1002, 1005)).IsEqualTo(1002u);

    [Test]
    public async Task Resolve_FallsBackToViewerGuildWhenTypeIsZero() =>
        await Assert.That(ExpeditionBuffViewRules.ResolveExpeditionId(0, 1005)).IsEqualTo(1005u);

    [Test]
    public async Task Resolve_UnguildedViewerWithListedId_StillResolvesListing() =>
        await Assert.That(ExpeditionBuffViewRules.ResolveExpeditionId(1002, 0)).IsEqualTo(1002u);
}
