using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Content-config keys used by the 10.0.2.13 farmhand charging screens.</summary>
public static class ButlerContentConfig
{
    public const string ProductionCostWeeklyChargeAmountLimit = "butler_production_cost_weekly_charge_amount_limit";
    public const string ProductionCostWeeklyFreeChargeLimit = "butler_production_cost_weekly_free_charge_limit";
    public const string ProductionCostFreeChargeAmount = "butler_production_cost_free_charge_amount";
    public const string LpDailyChargeAmountLimit = "butler_lp_daily_charge_amount_limit";
    public const string LpChargeMinimum = "butler_charge_lp_min";
    public const string ProductionCostWeeklyFreeChargeResetDay =
        "butler_production_cost_weekly_free_charge_reset_day";

    private static ContentConfigGameData Data => ContentConfigGameData.Instance;

    public static uint WeeklyProductionCostChargeAmountLimit =>
        RequirePositiveUInt(ProductionCostWeeklyChargeAmountLimit);

    public static uint WeeklyProductionCostFreeChargeLimit =>
        RequirePositiveUInt(ProductionCostWeeklyFreeChargeLimit);

    public static uint FreeProductionCostChargeAmount =>
        RequirePositiveUInt(ProductionCostFreeChargeAmount);

    public static uint DailyLaborPowerChargeAmountLimit =>
        RequirePositiveUInt(LpDailyChargeAmountLimit);

    public static uint MinimumLaborPowerChargeAmount =>
        RequirePositiveUInt(LpChargeMinimum);

    public static ButlerWeekday WeeklyFreeProductionCostChargeResetDay
    {
        get
        {
            var resetDay = Data.RequireInt(ProductionCostWeeklyFreeChargeResetDay);
            if (resetDay is < (int)ButlerWeekday.Sunday or > (int)ButlerWeekday.Saturday)
                throw new InvalidOperationException($"Invalid farmhand weekly reset day '{resetDay}'.");
            return (ButlerWeekday)resetDay;
        }
    }

    /// <summary>
    /// Gets all required charge limits from <c>content_configs</c>. The client getters at
    /// <c>FUN_39B633D0</c>/<c>FUN_39B63E80</c> resolve these table values; no gameplay fallback is used.
    /// </summary>
    public static ButlerChargeContentConfig RequireChargeLimits()
    {
        return new ButlerChargeContentConfig(
            WeeklyProductionCostChargeAmountLimit,
            WeeklyProductionCostFreeChargeLimit,
            FreeProductionCostChargeAmount,
            DailyLaborPowerChargeAmountLimit,
            MinimumLaborPowerChargeAmount,
            WeeklyFreeProductionCostChargeResetDay);
    }

    private static uint RequirePositiveUInt(string key)
    {
        if (!Data.TryGet(key, out var value) || value is <= 0 or > uint.MaxValue)
            throw new InvalidOperationException($"Required farmhand content_configs row '{key}' must be a positive uint.");
        return (uint)value;
    }
}

/// <summary>Client weekday numbering used by the farmhand weekly-reset config: Sunday is 1.</summary>
public enum ButlerWeekday : byte
{
    Sunday = 1,
    Monday = 2,
    Tuesday = 3,
    Wednesday = 4,
    Thursday = 5,
    Friday = 6,
    Saturday = 7
}

/// <summary>Resolved farmhand charge limits. Callers obtain this from <see cref="ButlerContentConfig"/>.</summary>
public readonly record struct ButlerChargeContentConfig(
    uint ProductionCostWeeklyChargeAmountLimit,
    uint ProductionCostWeeklyFreeChargeLimit,
    uint ProductionCostFreeChargeAmount,
    uint LpDailyChargeAmountLimit,
    uint LpChargeMinimum,
    ButlerWeekday ProductionCostWeeklyFreeChargeResetDay);
