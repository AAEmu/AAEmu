using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>combat_resources.default_point</c> is seeded only for resources the unit
/// owns through <c>combat_resource_groups</c> on its current abilities.
/// Pleasure (26 / 27) and Death's brand (6) share that table; they are not
/// starter pools for every class.
/// </summary>
public static class CombatResourceSeedRules
{
    public static void AddGroupResourceIds(
        ISet<int> owned,
        int resource1Id,
        int resource2Id,
        int change1Id,
        int change2Id)
    {
        Add(owned, resource1Id);
        Add(owned, resource2Id);
        Add(owned, change1Id);
        Add(owned, change2Id);
    }

    public static bool ShouldSeed(int resourceId, IReadOnlySet<int> ownedResourceIds) =>
        ownedResourceIds != null && ownedResourceIds.Contains(resourceId);

    private static void Add(ISet<int> owned, int id)
    {
        if (owned != null && id > 0)
            owned.Add(id);
    }
}
