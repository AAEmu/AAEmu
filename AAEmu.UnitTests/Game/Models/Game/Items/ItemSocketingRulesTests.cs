using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemSocketingRulesTests
{
    [Test]
    public async Task IsStoneForSkill_RequiresMatchingUseSkillId()
    {
        await Assert.That(ItemSocketingRules.IsStoneForSkill(100, 100)).IsTrue();
        await Assert.That(ItemSocketingRules.IsStoneForSkill(100, 200)).IsFalse();
        await Assert.That(ItemSocketingRules.IsStoneForSkill(0, 100)).IsFalse();
        await Assert.That(ItemSocketingRules.IsStoneForSkill(100, 0)).IsFalse();
    }

    [Test]
    public async Task ResolveExtractSlots_SingleIndex_ReturnsOnlyThatFilledSocket()
    {
        uint[] gems = [10, 20, 30, 0, 0, 0, 0, 0, 0];

        var slots = ItemSocketingRules.ResolveExtractSlots(gems, index: 2, isAll: false);

        await Assert.That(slots).IsEquivalentTo(new[] { 2 });
    }

    [Test]
    public async Task ResolveExtractSlots_EmptySocket_ReturnsNothing()
    {
        uint[] gems = [10, 20, 0, 0, 0, 0, 0, 0, 0];

        var slots = ItemSocketingRules.ResolveExtractSlots(gems, index: 2, isAll: false);

        await Assert.That(slots).IsEmpty();
    }

    [Test]
    public async Task ResolveExtractSlots_IsAll_ReturnsEveryFilledSocket()
    {
        uint[] gems = [10, 0, 30, 40, 0, 0, 0, 0, 0];

        var slots = ItemSocketingRules.ResolveExtractSlots(gems, index: 0, isAll: true);

        await Assert.That(slots).IsEquivalentTo(new[] { 0, 2, 3 });
    }

    [Test]
    public async Task ResolveExtractSlots_IndexZeroWithoutIsAll_IsSingleFirstSocket()
    {
        uint[] gems = [10, 20, 30, 0, 0, 0, 0, 0, 0];

        var slots = ItemSocketingRules.ResolveExtractSlots(gems, index: 0, isAll: false);

        await Assert.That(slots).IsEquivalentTo(new[] { 0 });
    }

    [Test]
    public async Task CompactAfterClear_MiddleSocket_PacksRemainingLeft()
    {
        uint[] gems = [10, 20, 30, 0, 0, 0, 0, 0, 0];

        var next = ItemSocketingRules.CompactAfterClear(gems, clearIndexes: [1]);

        await Assert.That(next).IsEquivalentTo(new uint[] { 10, 30, 0, 0, 0, 0, 0, 0, 0 });
    }

    [Test]
    public async Task CompactAfterClear_AllNamed_LeavesEmptyRun()
    {
        uint[] gems = [10, 20, 30, 0, 0, 0, 0, 0, 0];

        var next = ItemSocketingRules.CompactAfterClear(gems, clearIndexes: [0, 1, 2]);

        await Assert.That(next).IsEquivalentTo(new uint[9]);
    }

    [Test]
    public async Task CanFitExtractReturns_SharedFreeSlots_AreNotDoubleCounted()
    {
        // Two different gems each "fit" if free slots are counted twice; together they need 2.
        var returns = new Dictionary<uint, int> { [100] = 1, [200] = 1 };

        await Assert.That(ItemSocketingRules.CanFitExtractReturns(
            returns, _ => 0, _ => 1, freeSlots: 1)).IsFalse();
        await Assert.That(ItemSocketingRules.CanFitExtractReturns(
            returns, _ => 0, _ => 1, freeSlots: 2)).IsTrue();
    }

    [Test]
    public async Task CanFitExtractReturns_ExistingStackRoom_DoesNotSpendAFreeSlot()
    {
        var returns = new Dictionary<uint, int> { [100] = 2 };

        await Assert.That(ItemSocketingRules.CanFitExtractReturns(
            returns, id => id == 100 ? 2 : 0, _ => 20, freeSlots: 0)).IsTrue();
    }
}
