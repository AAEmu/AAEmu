using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemEvolvingRulesTests
{
    [Test]
    public async Task TryPurchase_RejectsWhenTheLadderIsFull()
    {
        await Assert.That(ItemEvolvingRules.TryPurchase(50, 0, out var purchased)).IsFalse();
        await Assert.That(purchased).IsEqualTo(0u);
    }

    [Test]
    public async Task TryPurchase_TakesOnlyTheRemainingRoom()
    {
        await Assert.That(ItemEvolvingRules.TryPurchase(80, 25, out var purchased)).IsTrue();
        await Assert.That(purchased).IsEqualTo(25u);
    }

    [Test]
    public async Task TryPurchase_KeepsAFeedThatFits()
    {
        await Assert.That(ItemEvolvingRules.TryPurchase(40, 100, out var purchased)).IsTrue();
        await Assert.That(purchased).IsEqualTo(40u);
    }
}
