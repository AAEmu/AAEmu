namespace AAEmu.Game.Models.Game.Expeditions;

internal static class WeeklyContributionRules
{
    public static uint CurrentValue(uint storedValue, DateTime storedPeriodStart, DateTime currentPeriodStart) =>
        storedPeriodStart < currentPeriodStart ? 0u : storedValue;
}
