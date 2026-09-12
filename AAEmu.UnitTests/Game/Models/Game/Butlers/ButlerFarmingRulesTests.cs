using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public class ButlerFarmingRulesTests
{
    [Test]
    public async Task CycleSeconds_MatchesNativeRoundedMillisecondFormula()
    {
        var harvest = Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false);

        var baseAccepted = ButlerFarmingRules.TryCalculateCycleSeconds(
            harvest, 0, out var baseSeconds, out var baseFailure);
        var reducedAccepted = ButlerFarmingRules.TryCalculateCycleSeconds(
            harvest, 20, out var reducedSeconds, out var reducedFailure);

        await Assert.That(baseAccepted).IsTrue();
        await Assert.That(baseFailure).IsEqualTo(ButlerFarmingRuleFailure.None);
        await Assert.That(baseSeconds).IsEqualTo((uint)720);
        await Assert.That(reducedAccepted).IsTrue();
        await Assert.That(reducedFailure).IsEqualTo(ButlerFarmingRuleFailure.None);
        await Assert.That(reducedSeconds).IsEqualTo((uint)576);
    }

    [Test]
    public async Task CycleSeconds_RejectsModifierThatRemovesTheWholeCycle()
    {
        var accepted = ButlerFarmingRules.TryCalculateCycleSeconds(
            Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false),
            100,
            out _,
            out var failure);

        await Assert.That(accepted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerFarmingRuleFailure.InvalidGrowthModifier);
    }

    private static readonly ButlerTemplate Farmhand = new()
    {
        Id = 1,
        OverworkProductionCostMul = 200
    };

    [Test]
    public async Task Potato_CalculatesLandCostsFromVerifiedContent()
    {
        // game_decrypted.sqlite3 butler_harvests id 7: Potato Eyes (item 15659).
        var harvest = Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, harvest.ConsumeLp!.Value, 3, false, out var costs, out var failure);

        await Assert.That(success).IsTrue();
        await Assert.That(failure).IsEqualTo(ButlerFarmingRuleFailure.None);
        await Assert.That(costs).IsEqualTo(new ButlerHarvestRegistrationCosts(3, 3, 3, 3, 0, 3, false));
    }

    [Test]
    public async Task Lamb_CalculatesRepeatedLivestockCostsFromVerifiedContent()
    {
        // game_decrypted.sqlite3 butler_harvests id 33: Lamb (item 16226).
        var harvest = Harvest(33, 16226, 34560000, 5, 5, 5, 10, 13408, 100, 13500, false);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, harvest.ConsumeLp!.Value, 2, false, out var costs, out _);

        await Assert.That(success).IsTrue();
        await Assert.That(costs).IsEqualTo(new ButlerHarvestRegistrationCosts(2, 20, 10, 50, 0, 50, false));
    }

    [Test]
    public async Task ActiveTradeJob_AddsRoundedUpOverworkVigor()
    {
        var harvest = Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, harvest.ConsumeLp!.Value, 3, true, out var costs, out _);

        await Assert.That(success).IsTrue();
        await Assert.That(costs.BaseVigor).IsEqualTo(3u);
        await Assert.That(costs.OverworkVigor).IsEqualTo(1u);
        await Assert.That(costs.TotalVigor).IsEqualTo(4u);
    }

    [Test]
    public async Task UnderwaterHarvest_UsesTheWaterCapacityPool()
    {
        // game_decrypted.sqlite3 butler_harvests id 104: Antler Coral Polyp (item 18829).
        var harvest = Harvest(104, 18829, 111096000, 10, 1, 9, 4, 13760, 100, 13819, true);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, harvest.ConsumeLp!.Value, 2, false, out var costs, out _);

        await Assert.That(success).IsTrue();
        await Assert.That(costs.IsUnderWater).IsTrue();
        await Assert.That(costs.GardenSize).IsEqualTo(20u);
        await Assert.That(costs.TotalVigor).IsEqualTo(20u);
    }

    [Test]
    public async Task ZeroAmount_FailsClosed()
    {
        var harvest = Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, harvest.ConsumeLp!.Value, 0, false, out _, out var failure);

        await Assert.That(success).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerFarmingRuleFailure.InvalidAmount);
    }

    [Test]
    public async Task MissingRequiredContent_FailsClosed()
    {
        var harvest = Harvest(7, 15659, 720000, 1, 1, 6, 1, 13379, 100, 13471, false);
        harvest.ConsumeLp = null;

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, 1, 1, false, out _, out var failure);

        await Assert.That(success).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerFarmingRuleFailure.MissingContent);
    }

    [Test]
    public async Task NoResources_FailsAdmission()
    {
        var costs = new ButlerHarvestRegistrationCosts(1, 1, 1, 1, 0, 1, false);

        var admitted = ButlerFarmingRules.HasSufficientResources(costs, default);

        await Assert.That(admitted).IsFalse();
    }

    [Test]
    public async Task WaterHarvest_UsesOnlyWaterCapacityForAdmission()
    {
        var costs = new ButlerHarvestRegistrationCosts(1, 1, 10, 1, 0, 1, true);
        var landOnly = new ButlerFarmingResources(1, 1, 10, 0, 1);
        var waterOnly = new ButlerFarmingResources(1, 1, 0, 10, 1);

        await Assert.That(ButlerFarmingRules.HasSufficientResources(costs, landOnly)).IsFalse();
        await Assert.That(ButlerFarmingRules.HasSufficientResources(costs, waterOnly)).IsTrue();
    }

    [Test]
    public async Task Overflow_FailsClosed()
    {
        var harvest = Harvest(7, 15659, 720000, uint.MaxValue, uint.MaxValue, 6, uint.MaxValue,
            13379, 100, 13471, false);

        var success = ButlerFarmingRules.TryCalculateRegistrationCosts(
            harvest, Farmhand, uint.MaxValue, short.MaxValue, true, out _, out var failure);

        await Assert.That(success).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerFarmingRuleFailure.ArithmeticOverflow);
    }

    [Test]
    public async Task Cancellation_ReleasesCapacityWithoutRefundingResources()
    {
        var costs = new ButlerHarvestRegistrationCosts(2, 20, 10, 50, 10, 60, false);

        var cancellation = ButlerFarmingRules.GetCancellation(costs);

        await Assert.That(cancellation.ReleasedGardenSize).IsEqualTo(10u);
        await Assert.That(cancellation.IsUnderWater).IsFalse();
        await Assert.That(cancellation.RefundedItemCount).IsEqualTo(0u);
        await Assert.That(cancellation.RefundedLaborPower).IsEqualTo(0u);
        await Assert.That(cancellation.RefundedVigor).IsEqualTo(0u);
    }

    private static ButlerHarvest Harvest(
        uint id,
        uint itemId,
        uint growthTime,
        uint size,
        uint repeatCount,
        uint actabilityGroupId,
        uint consumeLp,
        uint lootPackId,
        uint bonusRatio,
        uint bonusLootPackId,
        bool isUnderWater) =>
        new()
        {
            Id = id,
            ItemId = itemId,
            GrowthTime = growthTime,
            Size = size,
            RepeatCount = repeatCount,
            ActabilityGroupId = actabilityGroupId,
            ConsumeLp = consumeLp,
            LootPackId = lootPackId,
            BonusRatio = bonusRatio,
            BonusLootPackId = bonusLootPackId,
            ButlerHarvestGradeId = isUnderWater ? 2u : 1u,
            IsUnderWater = isUnderWater
        };
}
