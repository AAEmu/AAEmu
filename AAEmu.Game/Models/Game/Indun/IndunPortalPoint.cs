namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// One instance portal as the client's instance window lists it.
/// </summary>
/// <param name="IndunZoneKey">The instance zone the portal leads to.</param>
/// <param name="PortalZoneKey">The zone the portal itself stands in.</param>
/// <param name="X">Portal position in its own world, which is what the window points at.</param>
/// <param name="Y">See <paramref name="X"/>.</param>
/// <param name="Z">See <paramref name="X"/>.</param>
public readonly record struct IndunPortalPoint(
    uint IndunZoneKey, uint PortalZoneKey, float X, float Y, float Z);

/// <summary>
/// Which instance portals a character's instance window lists, and in what order.
/// </summary>
/// <remarks>
/// The list is the world the character is standing in: the client can only point at a portal it can
/// place, so a portal from another continent is not offered. A portal that leads nowhere (zone 0) is
/// dropped, and rows that repeat the same instance and zone pair are collapsed, because the window
/// shows one entry per pair. The order is by instance zone and then portal zone so a relog cannot
/// reshuffle the list.
/// </remarks>
public static class IndunPortalListRules
{
    /// <summary>The func types that make a doodad an instance portal.</summary>
    public static bool IsInstancePortalFunc(string funcType)
        => funcType is nameof(DoodadObj.Funcs.DoodadFuncEnterInstance)
            or nameof(DoodadObj.Funcs.DoodadFuncEnterSysInstance);

    /// <summary>
    /// Whether this character meets the instance's own level and gear rows.
    /// </summary>
    /// <remarks>
    /// The portal list itself is unfiltered — the client already shows those requirements on each
    /// entry. This is the enter gate. A gear score of 0 on the row means no requirement.
    /// </remarks>
    public static bool CanEnter(uint levelMin, uint levelMax, uint gearScore, int level, int characterGearScore)
    {
        if (level < 0 || characterGearScore < 0)
            return false;

        var levelValue = (uint)level;
        var gearValue = (uint)characterGearScore;

        return levelValue >= levelMin && levelValue <= levelMax &&
               (gearScore == 0 || gearValue >= gearScore);
    }

    /// <summary>The rows worth sending, in the order the window should show them.</summary>
    public static IReadOnlyList<IndunPortalPoint> Build(IEnumerable<IndunPortalPoint> portals)
    {
        var seen = new HashSet<(uint IndunZoneKey, uint PortalZoneKey)>();
        var rows = new List<IndunPortalPoint>();

        foreach (var portal in (portals ?? []).OrderBy(p => p.IndunZoneKey).ThenBy(p => p.PortalZoneKey))
        {
            if (portal.IndunZoneKey == 0)
                continue;
            if (!seen.Add((portal.IndunZoneKey, portal.PortalZoneKey)))
                continue;

            rows.Add(portal);
        }

        return rows;
    }
}
