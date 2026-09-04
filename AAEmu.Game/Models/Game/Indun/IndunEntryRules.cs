namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Pure rules for dungeon daily entries and create cooldowns (no DB / world state).
/// </summary>
public static class IndunEntryRules
{
    /// <summary>UTC calendar day start used for daily enter caps (UI: reset at 0:00).</summary>
    public static DateTime DailyWindowStartUtc(DateTime utcNow) =>
        DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);

    public static int CountEntriesInDailyWindow(IEnumerable<DateTime> entryTimesUtc, DateTime utcNow)
    {
        var dayStart = DailyWindowStartUtc(utcNow);
        var n = 0;
        foreach (var raw in entryTimesUtc)
        {
            var t = raw.Kind switch
            {
                DateTimeKind.Utc => raw,
                DateTimeKind.Local => raw.ToUniversalTime(),
                _ => DateTime.SpecifyKind(raw, DateTimeKind.Utc)
            };

            if (t >= dayStart)
                n++;
        }

        return n;
    }

    /// <summary>
    /// True when a new instance create is blocked by <see cref="IndunZone.RestoreItemTime"/>.
    /// Rejoin of an already-bound copy must not call this.
    /// </summary>
    public static bool IsCreateOnCooldown(
        DateTime? lastCreateUtc,
        DateTime utcNow,
        uint restoreItemTimeSeconds)
    {
        if (restoreItemTimeSeconds == 0 || lastCreateUtc is null)
            return false;

        var last = lastCreateUtc.Value;
        if (last.Kind == DateTimeKind.Unspecified)
            last = DateTime.SpecifyKind(last, DateTimeKind.Utc);
        else if (last.Kind == DateTimeKind.Local)
            last = last.ToUniversalTime();

        var elapsed = (utcNow - last).TotalSeconds;
        return elapsed < restoreItemTimeSeconds;
    }

    /// <summary>
    /// Prefer <c>instances.enter_count</c> for IndunZone rows; fall back for zones with no catalog row.
    /// </summary>
    public static uint ResolveEnterCount(uint? instancesEnterCount, bool selectChannel, uint zoneGroupId)
    {
        if (instancesEnterCount.HasValue)
            return instancesEnterCount.Value;

        // Legacy fallback when compact has no instances row for this zone group.
        return zoneGroupId == 49 || selectChannel ? 1000u : 3u;
    }

    /// <summary>Client <c>IVT_RESET</c> — buy a visit-count reset ticket.</summary>
    public const sbyte VisitTypeReset = 3;
    /// <summary>Client <c>IVT_PERMIT</c> — buy an extra daily enter.</summary>
    public const sbyte VisitTypePermit = 4;

    /// <summary>Ticket stack cost for the next RESET purchase.</summary>
    public static int ResetTicketCost(int currentResetCount, int increaseScale)
    {
        var scale = increaseScale > 0 ? increaseScale : 1;
        return scale * (currentResetCount + 1);
    }

    /// <summary>True when another RESET purchase is allowed.</summary>
    public static bool CanBuyReset(int currentResetCount, int resetLimit) =>
        resetLimit <= 0 || currentResetCount < resetLimit;

    /// <summary>Effective daily enter cap after PERMIT buys.</summary>
    public static int EffectivePermittedCount(uint enterCount, int permitBonus) =>
        (int)enterCount + Math.Max(0, permitBonus);
}
