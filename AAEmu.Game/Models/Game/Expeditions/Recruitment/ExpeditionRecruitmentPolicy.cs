namespace AAEmu.Game.Models.Game.Expeditions.Recruitment;

using AAEmu.Game.Core.Managers;

/// <summary>
/// Recruitment policy backed by the guild content-config rows (kind 25).
/// The active client getters were used to cross-check the same values.
/// </summary>
public static class ExpeditionRecruitmentPolicy
{
    public const int MaximumTextLength = 100;
    public const short AllInterests = 0x3f;

    public static bool TryGetCost(ExpeditionManager manager, uint periodDays, out long copper)
    {
        var minimumPeriod = manager.GetContentConfig("expedition_recruit_period_min");
        var maximumPeriod = manager.GetContentConfig("expedition_recruit_period_max");
        copper = periodDays == minimumPeriod
            ? manager.GetContentConfig("expedition_recruit_period_min_cost")
            : periodDays == maximumPeriod
                ? manager.GetContentConfig("expedition_recruit_period_max_cost")
                : 0;
        return copper > 0;
    }

    public static int MaximumApplications(ExpeditionManager manager) =>
        checked((int)manager.GetContentConfig("expedition_recruit_apply_max"));

    public static bool IsValidInterestMask(short value) => (value & ~AllInterests) == 0;
}
