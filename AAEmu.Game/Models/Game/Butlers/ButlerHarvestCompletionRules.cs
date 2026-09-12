namespace AAEmu.Game.Models.Game.Butlers;

public readonly record struct ButlerHarvestCyclePlan(
    ushort CycleNumber,
    ButlerHarvestJob AdvancedJob,
    bool IsFinal);

/// <summary>Pure persisted-clock and repeat-count rules for one Farmhand harvest interval.</summary>
public static class ButlerHarvestCompletionRules
{
    public static bool TryPlanNextCycle(
        ButlerHarvestJob job,
        uint configuredRepeatCount,
        uint cycleSeconds,
        long now,
        out ButlerHarvestCyclePlan plan)
    {
        plan = default;
        if (job == null || job.JobId <= 0 || job.StaticHarvestId == 0 || job.RequestedAmount == 0 ||
            job.UpdateTime <= 0 || job.RemainingRepeatCount == 0 ||
            configuredRepeatCount == 0 || configuredRepeatCount > (uint)short.MaxValue ||
            job.RemainingRepeatCount > configuredRepeatCount || cycleSeconds == 0)
            return false;

        long nextUpdateTime;
        try
        {
            nextUpdateTime = checked(job.UpdateTime + cycleSeconds);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (now < nextUpdateTime)
            return false;

        var remaining = checked((ushort)(job.RemainingRepeatCount - 1));
        var completedBeforeThisCycle = configuredRepeatCount - job.RemainingRepeatCount;
        var cycleNumber = checked((ushort)(completedBeforeThisCycle + 1));
        plan = new ButlerHarvestCyclePlan(
            cycleNumber,
            job with { RemainingRepeatCount = remaining, UpdateTime = nextUpdateTime },
            remaining == 0);
        return true;
    }
}
