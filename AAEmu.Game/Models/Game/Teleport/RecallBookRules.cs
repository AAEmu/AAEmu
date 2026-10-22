namespace AAEmu.Game.Models.Game.Teleport;

/// <summary>
/// Memory Tome (recall book) destinations: return points that districts of kind
/// <c>return</c> already name, placed by <c>return_point.g</c>, unlocked by the
/// <c>subzone_area.xml</c> polygon that contains them (<c>CSNotifySubZone</c>).
/// Nested districts inherit their parent's destinations via compact
/// <c>sub_zones.parent_sub_zone_id</c> so persisted visits to a child district
/// still resolve the parent's return pad.
/// </summary>
public static class RecallBookRules
{
    /// <summary>
    /// Compact <c>enum_district_kinds.name</c> for Memory Tome / return districts.
    /// </summary>
    public const string ReturnDistrictKindName = "return";

    /// <summary>
    /// Whether this return point may appear in the book: it must be named by at least one
    /// district whose kind is <see cref="ReturnDistrictKindName"/>.
    /// </summary>
    public static bool IsBookEligible(uint returnPointId, IReadOnlySet<uint> returnDistrictReturnPointIds) =>
        returnPointId != 0 &&
        returnDistrictReturnPointIds != null &&
        returnDistrictReturnPointIds.Contains(returnPointId);

    /// <summary>
    /// Subzone area ids (client notify ids) whose polygon contains the zone-local return point.
    /// Empty when the point sits outside every loaded area for that zone.
    /// </summary>
    public static IReadOnlyList<uint> SubZonesForReturnPoint(
        float localX,
        float localY,
        uint zoneKey,
        IReadOnlyDictionary<uint, List<SubZoneAreaFileRules.LocalArea>> areasByZoneKey)
    {
        if (areasByZoneKey == null ||
            !areasByZoneKey.TryGetValue(zoneKey, out var areas) ||
            areas == null ||
            areas.Count == 0)
            return [];

        return SubZoneAreaFileRules.ContainingAreaIds(localX, localY, areas);
    }

    /// <summary>
    /// All descendants of <paramref name="rootSubZoneId"/> in the
    /// <c>sub_zones.parent_sub_zone_id</c> tree (children, grandchildren, …).
    /// </summary>
    public static IReadOnlyList<uint> DescendantSubZones(
        uint rootSubZoneId,
        IReadOnlyDictionary<uint, List<uint>> childrenByParent)
    {
        if (rootSubZoneId == 0 || childrenByParent == null)
            return [];

        var result = new List<uint>();
        var stack = new Stack<uint>();
        stack.Push(rootSubZoneId);
        var seen = new HashSet<uint> { rootSubZoneId };

        while (stack.Count > 0)
        {
            var parent = stack.Pop();
            if (!childrenByParent.TryGetValue(parent, out var children) || children == null)
                continue;
            foreach (var child in children)
            {
                if (child == 0 || !seen.Add(child))
                    continue;
                result.Add(child);
                stack.Push(child);
            }
        }

        return result;
    }

    /// <summary>
    /// Build parent→children adjacency from compact <c>sub_zones (id, parent_sub_zone_id)</c>.
    /// </summary>
    public static Dictionary<uint, List<uint>> BuildChildrenByParent(
        IEnumerable<(uint Id, uint ParentSubZoneId)> rows)
    {
        var map = new Dictionary<uint, List<uint>>();
        if (rows == null)
            return map;

        foreach (var (id, parent) in rows)
        {
            if (id == 0 || parent == 0)
                continue;
            if (!map.TryGetValue(parent, out var list))
            {
                list = [];
                map[parent] = list;
            }

            if (!list.Contains(id))
                list.Add(id);
        }

        return map;
    }
}
