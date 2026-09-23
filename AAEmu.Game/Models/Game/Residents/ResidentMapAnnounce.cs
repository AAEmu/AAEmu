namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// Membership-diff for the resident-map feed: the client's resident map is only ever touched by
/// SCResidentMap, so each group must be announced exactly once per client session — re-announcing
/// is wasted traffic, and a group the character no longer resides in must be removed once.
/// </summary>
public static class ResidentMapAnnounce
{
    /// <summary>
    /// Diffs <paramref name="current"/> membership against what has already been announced,
    /// updates <paramref name="announced"/> in place, and returns the changes in ascending order:
    /// option Add for the joins, option Remove for the leaves.
    /// </summary>
    public static (List<uint> Adds, List<uint> Removes) Push(IEnumerable<uint> current, ISet<uint> announced)
    {
        var currentSet = current as ISet<uint> ?? current.ToHashSet();
        var adds = currentSet.Where(groupId => !announced.Contains(groupId)).OrderBy(groupId => groupId).ToList();
        var removes = announced.Where(groupId => !currentSet.Contains(groupId)).OrderBy(groupId => groupId).ToList();

        foreach (var groupId in adds)
            announced.Add(groupId);
        foreach (var groupId in removes)
            announced.Remove(groupId);

        return (adds, removes);
    }
}
