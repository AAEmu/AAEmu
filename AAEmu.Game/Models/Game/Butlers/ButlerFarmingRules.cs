namespace AAEmu.Game.Models.Game.Butlers;

public enum ButlerFarmingRuleFailure
{
    None,
    InvalidAmount,
    MissingContent,
    InvalidGrowthModifier,
    ArithmeticOverflow
}

public readonly record struct ButlerHarvestRegistrationCosts(
    uint ItemCount,
    uint LaborPower,
    uint GardenSize,
    uint BaseVigor,
    uint OverworkVigor,
    uint TotalVigor,
    bool IsUnderWater);

public readonly record struct ButlerHarvestCancellation(
    uint ReleasedGardenSize,
    bool IsUnderWater,
    uint RefundedItemCount,
    uint RefundedLaborPower,
    uint RefundedVigor);

public readonly record struct ButlerFarmingResources(
    uint ItemCount,
    uint LaborPower,
    uint LandGardenSize,
    uint WaterGardenSize,
    uint Vigor);

/// <summary>Pure admission and resource calculations for farmhand farming jobs.</summary>
public static class ButlerFarmingRules
{
    // Shipped x2ui/butler/tab_farming.lua:412-433 divides
    // butlers.overwork_production_cost_mul by 1000 and rounds up.
    public const uint OverworkProductionCostScale = 1_000;

    public static bool TryCalculateRegistrationCosts(
        ButlerHarvest harvest,
        ButlerTemplate butler,
        uint laborPowerPerUnit,
        int amount,
        bool hasActiveSpecialtyTradeJob,
        out ButlerHarvestRegistrationCosts costs,
        out ButlerFarmingRuleFailure failure)
    {
        costs = default;
        failure = ButlerFarmingRuleFailure.None;

        if (amount <= 0 || amount > short.MaxValue)
        {
            failure = ButlerFarmingRuleFailure.InvalidAmount;
            return false;
        }

        if (!HasRequiredContent(harvest, butler))
        {
            failure = ButlerFarmingRuleFailure.MissingContent;
            return false;
        }

        try
        {
            checked
            {
                var requestedAmount = (uint)amount;
                var itemCount = requestedAmount;
                var laborPower = (uint)((ulong)requestedAmount * laborPowerPerUnit);
                var gardenSize = (uint)((ulong)requestedAmount * harvest.Size!.Value);
                var baseVigor = (uint)((ulong)gardenSize * harvest.RepeatCount!.Value);
                var overworkVigor = hasActiveSpecialtyTradeJob
                    ? DivideRoundUp((ulong)baseVigor * butler.OverworkProductionCostMul,
                        OverworkProductionCostScale)
                    : 0;
                var totalVigor = checked(baseVigor + overworkVigor);

                costs = new ButlerHarvestRegistrationCosts(
                    itemCount,
                    laborPower,
                    gardenSize,
                    baseVigor,
                    overworkVigor,
                    totalVigor,
                    harvest.IsUnderWater!.Value);
                return true;
            }
        }
        catch (OverflowException)
        {
            failure = ButlerFarmingRuleFailure.ArithmeticOverflow;
            return false;
        }
    }

    /// <summary>
    /// Cancelling releases the occupied garden area, but shipped task warnings state that the
    /// input, farmhand labor, and Vigor are not returned.
    /// </summary>
    public static ButlerHarvestCancellation GetCancellation(ButlerHarvestRegistrationCosts costs) =>
        new(costs.GardenSize, costs.IsUnderWater, 0, 0, 0);

    /// <summary>
    /// Calculates the client's displayed cycle length. FUN_3918EA10 rounds adjusted growth
    /// milliseconds, divides by 1000, then compares the resulting seconds to updateTime.
    /// </summary>
    public static bool TryCalculateCycleSeconds(
        ButlerHarvest harvest,
        uint growthTimeReductionPercent,
        out uint cycleSeconds,
        out ButlerFarmingRuleFailure failure)
    {
        cycleSeconds = 0;
        failure = ButlerFarmingRuleFailure.None;
        if (harvest?.GrowthTime is not > 0)
        {
            failure = ButlerFarmingRuleFailure.MissingContent;
            return false;
        }
        if (growthTimeReductionPercent >= 100)
        {
            failure = ButlerFarmingRuleFailure.InvalidGrowthModifier;
            return false;
        }

        try
        {
            checked
            {
                var adjustedHundredthsOfMillisecond =
                    (ulong)harvest.GrowthTime.Value * (100u - growthTimeReductionPercent);
                var roundedMilliseconds = (adjustedHundredthsOfMillisecond + 50) / 100;
                cycleSeconds = checked((uint)(roundedMilliseconds / 1_000));
                if (cycleSeconds == 0)
                {
                    failure = ButlerFarmingRuleFailure.InvalidGrowthModifier;
                    return false;
                }
                return true;
            }
        }
        catch (OverflowException)
        {
            failure = ButlerFarmingRuleFailure.ArithmeticOverflow;
            return false;
        }
    }

    public static bool HasSufficientResources(
        ButlerHarvestRegistrationCosts costs,
        ButlerFarmingResources resources)
    {
        var availableGardenSize = costs.IsUnderWater
            ? resources.WaterGardenSize
            : resources.LandGardenSize;
        return resources.ItemCount >= costs.ItemCount &&
               resources.LaborPower >= costs.LaborPower &&
               availableGardenSize >= costs.GardenSize &&
               resources.Vigor >= costs.TotalVigor;
    }

    private static bool HasRequiredContent(ButlerHarvest harvest, ButlerTemplate butler) =>
        harvest is
        {
            Id: > 0,
            ItemId: > 0,
            GrowthTime: > 0,
            Size: > 0,
            RepeatCount: > 0,
            ActabilityGroupId: > 0,
            ConsumeLp: not null,
            LootPackId: > 0,
            BonusRatio: not null,
            IsUnderWater: not null
        } &&
        (harvest.BonusRatio == 0 || harvest.BonusLootPackId > 0) &&
        butler is { Id: > 0 };

    private static uint DivideRoundUp(ulong value, uint divisor)
    {
        var quotient = value / divisor;
        if (value % divisor != 0)
            quotient++;
        return checked((uint)quotient);
    }
}
