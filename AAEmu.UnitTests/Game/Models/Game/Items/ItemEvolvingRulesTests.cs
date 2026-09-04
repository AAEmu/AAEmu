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

    [Test]
    public async Task TryTakeFeed_StopsBeforeASlotThatWouldBePureOverflow()
    {
        await Assert.That(ItemEvolvingRules.TryTakeFeed([50, 50], 40, out var purchased, out var takeCount))
            .IsTrue();
        await Assert.That(purchased).IsEqualTo(40u);
        await Assert.That(takeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TryTakeFeed_KeepsTheLastSlotThatStillBuysRoom()
    {
        await Assert.That(ItemEvolvingRules.TryTakeFeed([30, 30], 50, out var purchased, out var takeCount))
            .IsTrue();
        await Assert.That(purchased).IsEqualTo(50u);
        await Assert.That(takeCount).IsEqualTo(2);
    }

    [Test]
    public async Task TryTakeFeed_RejectsAFullLadder()
    {
        await Assert.That(ItemEvolvingRules.TryTakeFeed([40, 40], 0, out var purchased, out var takeCount))
            .IsFalse();
        await Assert.That(purchased).IsEqualTo(0u);
        await Assert.That(takeCount).IsEqualTo(0);
    }
}
