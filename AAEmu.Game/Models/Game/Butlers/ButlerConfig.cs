namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Server policy for Farmhand work that the retail client does not calculate.</summary>
public sealed class ButlerConfig
{
    public ButlerHarvestCompletionConfig HarvestCompletion { get; set; } = new();
}

public sealed class ButlerHarvestCompletionConfig
{
    /// <summary>How often the server checks persisted Farmhand harvest jobs.</summary>
    public int ScanIntervalSeconds { get; set; } = 5;

    /// <summary>Maximum overdue intervals completed for one job during one scan.</summary>
    public int MaxCatchUpCyclesPerPass { get; set; } = 8;

    /// <summary>
    /// Denominator for <c>butler_harvests.bonus_ratio</c>. The retail backend formula is unavailable;
    /// 10,000 treats the content value as basis points. Zero disables bonus harvests.
    /// </summary>
    public uint BonusRatioScale { get; set; } = 10_000;

    /// <summary>
    /// Multiplier applied to content formula 19 (<c>exp_by_labor_power</c>) when the final interval
    /// completes. Zero disables Farmhand experience from harvest jobs.
    /// </summary>
    public double ExperienceRate { get; set; } = 1d;

    public bool IsValid() =>
        ScanIntervalSeconds > 0 &&
        MaxCatchUpCyclesPerPass > 0 &&
        double.IsFinite(ExperienceRate) && ExperienceRate >= 0d;
}
