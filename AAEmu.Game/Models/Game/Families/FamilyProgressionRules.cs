using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.Families;

/// <summary>
/// Server policy for guide-level family rules whose exact server implementation is unavailable.
/// Timestamps use the server's shared Unix-seconds convention; percentage loss truncates the lost
/// amount and does not lower the explicitly purchased family level.
/// </summary>
public static class FamilyProgressionRules
{
    public static bool IsNewUtcDay(long previousUnixSeconds, long currentUnixSeconds)
    {
        if (previousUnixSeconds <= 0)
            return true;

        var previous = DateTimeOffset.FromUnixTimeSeconds(previousUnixSeconds).UtcDateTime;
        var current = DateTimeOffset.FromUnixTimeSeconds(currentUnixSeconds).UtcDateTime;
        return ServerCalendar.IsNewDailyPeriod(previous, current);
    }

    public static bool CanChangeRole(long lastUpdateTime, long now, long cooldownSeconds) =>
        lastUpdateTime <= 0 || cooldownSeconds <= 0 || now - lastUpdateTime >= cooldownSeconds;

    public static uint ApplyDepartureExperienceLoss(uint experience, int percent)
    {
        if (percent <= 0) return experience;
        if (percent >= 100) return 0;
        return experience - (uint)((ulong)experience * (uint)percent / 100);
    }
}
