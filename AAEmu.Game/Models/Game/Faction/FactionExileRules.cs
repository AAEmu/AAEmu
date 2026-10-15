using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Faction;

public enum ExileRouteMode : byte
{
    /// <summary>Open while the destination is inferior and the origin is not, as long as slots are left.</summary>
    Automatic = 0,

    /// <summary>Forced open by a GM regardless of the standings, as long as slots are left.</summary>
    Open = 1,

    /// <summary>Forced shut by a GM.</summary>
    Closed = 2
}

/// <summary>One direction of exile between two factions, with its broker spawner and this week's use.</summary>
public sealed class ExileRoute
{
    public uint FromFactionId { get; init; }
    public uint ToFactionId { get; init; }
    public uint NpcSpawnerId { get; init; }
    public ExileRouteMode Mode { get; set; }
    public int? MaxOverride { get; set; }
    public uint Used { get; set; }
}

public static class FactionExileRules
{
    /// <summary>
    /// The weekly limit of a route: a GM override, or the faction_change_limit_nums row for the power gap
    /// between origin and destination in percent of the origin's activity.
    /// </summary>
    public static int MaxFor(ExileRoute route, IReadOnlyList<FactionChangeLimit> limits, int fromTotal, int toTotal)
    {
        if (route.MaxOverride is { } fixedMax)
            return Math.Max(0, fixedMax);
        var gap = fromTotal <= 0 ? 0 : Math.Clamp((fromTotal - toTotal) * 100.0 / fromTotal, 0, 100);
        return FactionPowerWeightRules.LimitFor(limits, gap);
    }

    public static int Left(ExileRoute route, int max) => Math.Max(0, max - (int)route.Used);

    public static bool IsActive(ExileRoute route, int max, bool enabled, IReadOnlySet<uint> suspended,
        Func<uint, bool> isInferior)
    {
        if (!enabled || route.Mode == ExileRouteMode.Closed)
            return false;
        if (suspended.Contains(route.FromFactionId) || suspended.Contains(route.ToFactionId))
            return false;
        if (Left(route, max) <= 0)
            return false;
        return route.Mode == ExileRouteMode.Open
               || (isInferior(route.ToFactionId) && !isInferior(route.FromFactionId));
    }

    public static bool CooldownOver(DateTime? lastExileUtc, int cooldownSeconds, DateTime nowUtc) =>
        lastExileUtc is not { } last || cooldownSeconds <= 0 || nowUtc >= last.AddSeconds(cooldownSeconds);
}
