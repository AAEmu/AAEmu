using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemBatchRulesTests
{
    [Test]
    public async Task Opening_MultipliesSourceAndEverySelectedReward()
    {
        var effect = new SelectiveItemEffectTemplate
        {
            SelectCount = 2, ConsumeItemCount = 2,
            Elems = [new() { ItemId = 10, Count = 3, GradeId = 2 }, new() { ItemId = 20, Count = 1 }]
        };
        var valid = SelectiveItemOpening.TryBuildRewards(effect, 5, [1, 2], out var burn, out var rewards);
        await Assert.That(valid).IsTrue();
        await Assert.That(burn).IsEqualTo(10);
        await Assert.That(rewards).IsEquivalentTo(new[]
        {
            new ItemAcquisitionRequest(10, 15, 2), new ItemAcquisitionRequest(20, 5, 0)
        });
    }

    [Test]
    public async Task Opening_RejectsZeroOverflowDuplicatesAndInvalidOptions()
    {
        var effect = new SelectiveItemEffectTemplate
        {
            SelectCount = 2, ConsumeItemCount = 2, Elems = [new() { ItemId = 10, Count = 2 }]
        };
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, 0, [1], out _, out _)).IsFalse();
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, uint.MaxValue, [1], out _, out _)).IsFalse();
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, int.MaxValue, [1], out _, out _)).IsFalse();
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, 5, [1, 1], out _, out _)).IsFalse();
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, 5, [0], out _, out _)).IsFalse();
        await Assert.That(SelectiveItemOpening.TryBuildRewards(effect, 5, [2], out _, out _)).IsFalse();
    }

    [Test]
    public async Task SocketBatch_PricesEveryFollowingSocket()
    {
        var seen = new List<uint>();
        var error = ItemSocketingRules.PlanInstallCosts(3, 5, 4, 9, used =>
        {
            seen.Add(used);
            return 10L << (int)used;
        }, out var costs, out var total);
        await Assert.That(error).IsNull();
        await Assert.That(seen).IsEquivalentTo(new uint[] { 4, 5, 6 });
        await Assert.That(costs).IsEquivalentTo(new long[] { 160, 320, 640 });
        await Assert.That(total).IsEqualTo(1120L);
    }

    [Test]
    public async Task SocketBatch_RejectsMissingGemsFullGearAndOverflowBeforeAnyAttempt()
    {
        await Assert.That(ItemSocketingRules.PlanInstallCosts(5, 4, 0, 9, _ => 10, out _, out _))
            .IsEqualTo((ErrorMessageType?)ErrorMessageType.NotEnoughItem);
        await Assert.That(ItemSocketingRules.PlanInstallCosts(3, 5, 7, 9, _ => 10, out _, out _))
            .IsEqualTo((ErrorMessageType?)ErrorMessageType.ItemCannotUse);
        await Assert.That(ItemSocketingRules.PlanInstallCosts(2, 5, 0, 9, _ => long.MaxValue, out _, out _))
            .IsEqualTo((ErrorMessageType?)ErrorMessageType.ItemCannotUse);
        await Assert.That(ItemSocketingRules.PlanInstallCosts(1, 5, 0, 9, _ => -1, out _, out _))
            .IsEqualTo((ErrorMessageType?)ErrorMessageType.ItemCannotUse);
    }

    [Test]
    public async Task SocketCount_ReadsTheUnalignedIntegerWithoutTruncatingIt()
    {
        await Assert.That(ItemSocketingRules.RequestedInstallAttempts(new SkillObject())).IsEqualTo(1);
        await Assert.That(ItemSocketingRules.RequestedInstallAttempts(
            new SkillObjectExtraValues { Values = [0x00000701, 0] })).IsEqualTo(7);
        await Assert.That(ItemSocketingRules.RequestedInstallAttempts(
            new SkillObjectExtraValues { Values = [0x00010000, 0] })).IsEqualTo(256);
        await Assert.That(ItemSocketingRules.PlanInstallCosts(256, 500, 0, 9, _ => 0, out _, out _))
            .IsEqualTo((ErrorMessageType?)ErrorMessageType.ItemCannotUse);
    }

    [Test]
    public async Task BatchLabor_IsChargedForEveryAttempt()
    {
        var skill = new Skill(new SkillTemplate { ConsumeLaborPower = 500 }) { LaborUnits = 7 };
        await Assert.That(skill.GetLaborCost(null)).IsEqualTo(3500);
        await Assert.That(SkillLaborRules.CanAfford(skill.GetLaborCost(null), 3499, 0)).IsFalse();
        await Assert.That(SkillLaborRules.CanAfford(skill.GetLaborCost(null), 3000, 500)).IsTrue();
    }
}
