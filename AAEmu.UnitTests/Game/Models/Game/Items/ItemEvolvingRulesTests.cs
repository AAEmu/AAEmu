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

    [Test]
    public async Task ExpRoomToLadderCap_AtTopStillBuysTheCeilingBar()
    {
        // Exalted Hiram-style: Mythic 11 costs 2.7M to leave, Eternal 12 still has a 10.5M bar that
        // fills for stats and never tips. Sitting at Eternal 0% must not report room 0.
        uint GradeExp(byte g) => g switch { 11 => 2_777_625u, 12 => 10_554_974u, _ => 0u };

        await Assert.That(ItemEvolvingRules.ExpRoomToLadderCap(12, 12, 0, GradeExp)).IsEqualTo(10_554_974u);
        await Assert.That(ItemEvolvingRules.ExpRoomToLadderCap(12, 12, 1_000_000, GradeExp))
            .IsEqualTo(9_554_974u);
        await Assert.That(ItemEvolvingRules.ExpRoomToLadderCap(12, 12, 10_554_974, GradeExp)).IsEqualTo(0u);
    }

    [Test]
    public async Task ExpRoomToLadderCap_BelowTopIncludesTheCeilingBar()
    {
        uint GradeExp(byte g) => g switch { 11 => 2_777_625u, 12 => 10_554_974u, _ => 0u };

        await Assert.That(ItemEvolvingRules.ExpRoomToLadderCap(11, 12, 0, GradeExp))
            .IsEqualTo(2_777_625u + 10_554_974u);
    }

    [Test]
    public async Task ExpRoomToLadderCap_PastTopBuysNothing()
    {
        await Assert.That(ItemEvolvingRules.ExpRoomToLadderCap(12, 11, 0, _ => 100u)).IsEqualTo(0u);
    }

    [Test]
    public async Task StepCost_IsTheCurrentGradesBarNotTheNext()
    {
        // Typical weapon pool: current 9494, next 16139 (~1.7×). Spending the next cost is what
        // left a full bar stuck until ~170%. ExpRoomToLadderCap only decides how much of a feed
        // is billed; this step rule is what keeps the drawn bar from climbing past 100% before
        // the grade moves — do not regress it when widening room at the ceiling.
        await Assert.That(ItemEvolvingRules.TryGetStepCost(9494, 16139, false, out var needed)).IsTrue();
        await Assert.That(needed).IsEqualTo(9494u);
        await Assert.That(needed).IsNotEqualTo(16139u);
    }

    [Test]
    public async Task ExpRoomToLadderCap_DoesNotChangeWhenAStepIsDue()
    {
        // Mid-ladder with the classic 1.7× spacing: room may span several rungs, but a step is
        // still owed exactly when sectionExp covers the *current* bar (9494), not the next (16139).
        uint GradeExp(byte g) => g switch { 8 => 9494u, 9 => 16139u, _ => 0u };
        var room = ItemEvolvingRules.ExpRoomToLadderCap(8, 9, 9494, GradeExp);
        await Assert.That(room).IsEqualTo(16139u); // only the next bar left after a full current
        await Assert.That(ItemEvolvingRules.TryGetStepCost(9494, 16139, false, out var needed)).IsTrue();
        await Assert.That(needed).IsEqualTo(9494u);
    }

    [Test]
    public async Task StepCost_FreeStepStillPaysByFillingTheGradeBelow()
    {
        await Assert.That(ItemEvolvingRules.TryGetStepCost(10000, 0, allowsFreeStepIntoNext: true, out var needed))
            .IsTrue();
        await Assert.That(needed).IsEqualTo(10000u);
    }

    [Test]
    public async Task StepCost_StopsAtAnUnpricedNextWithoutAFreeStep()
    {
        await Assert.That(ItemEvolvingRules.TryGetStepCost(10000, 0, allowsFreeStepIntoNext: false, out _))
            .IsFalse();
    }

    [Test]
    public async Task StepCost_RejectsAnUnpricedCurrentGrade()
    {
        await Assert.That(ItemEvolvingRules.TryGetStepCost(0, 1624, false, out _))
            .IsFalse();
    }
}
