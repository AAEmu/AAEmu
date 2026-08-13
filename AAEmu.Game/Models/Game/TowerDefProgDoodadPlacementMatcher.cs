namespace AAEmu.Game.Models.Game;

/// <summary>
/// Selects configured world placements for the doodad templates a tower prog step needs.
/// </summary>
public static class TowerDefProgDoodadPlacementMatcher
{
    public static IReadOnlyList<TowerDefProgDoodadPlacement> Match(
        IReadOnlyList<TowerDefProgDoodadPlacement> configured,
        IReadOnlyCollection<uint> wantedTemplateIds)
    {
        if (configured == null || configured.Count == 0 ||
            wantedTemplateIds == null || wantedTemplateIds.Count == 0)
            return [];

        var wanted = wantedTemplateIds as HashSet<uint> ?? wantedTemplateIds.ToHashSet();
        var matched = new List<TowerDefProgDoodadPlacement>();
        foreach (var place in configured)
        {
            if (place == null || place.TemplateId == 0 || !wanted.Contains(place.TemplateId))
                continue;
            matched.Add(place);
        }

        return matched;
    }
}
