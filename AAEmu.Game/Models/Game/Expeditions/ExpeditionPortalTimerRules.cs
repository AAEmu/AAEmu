namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Guild worldgate countdown for <c>SCExpeditionPortalTimer</c> /
/// <c>X2Faction:GetDisplayPortalTime</c>. The client stores an absolute end tick and returns
/// <c>(endTime - now) / 1000</c> seconds; kind 0 (or endTime 0) means no timer.
/// </summary>
public static class ExpeditionPortalTimerRules
{
    /// <summary>Non-zero kind the client accepts as an active portal countdown.</summary>
    public const int ActiveKind = 1;

    /// <summary>Cleared / inactive kind (matches the client's zero sentinel).</summary>
    public const int InactiveKind = 0;

    public static bool HasRemaining(DateTime growthEndsUtc, DateTime nowUtc) =>
        growthEndsUtc > nowUtc;

    public static int RemainingMilliseconds(DateTime growthEndsUtc, DateTime nowUtc)
    {
        if (!HasRemaining(growthEndsUtc, nowUtc))
            return 0;
        var ms = (growthEndsUtc - nowUtc).TotalMilliseconds;
        if (ms <= 0)
            return 0;
        return ms >= int.MaxValue ? int.MaxValue : (int)ms;
    }

    /// <summary>
    /// Absolute end tick in the client's phys-time space: <paramref name="physTimeNow"/> plus
    /// remaining growth ms.
    /// </summary>
    public static int EndTimePhys(uint physTimeNow, int remainingMs) =>
        remainingMs <= 0 ? 0 : unchecked((int)(physTimeNow + (uint)remainingMs));
}
