namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Create order for a hull and its equipment in a dedicate. Drop order is the reverse
/// (see <see cref="BoatDespawnRules.UnitIdsToRemoveFromZone"/>).
/// </summary>
public static class BoatAttachmentAnnounceRules
{
    /// <summary>
    /// Hull first, then children parent-before-child. <paramref name="childSlaveObjIdsDeepestFirst"/>
    /// is the walk <c>SlaveManager.CollectBoatAttachments</c> produces.
    /// </summary>
    public static IReadOnlyList<uint> UnitIdsToCreateInZone(
        uint hullObjId,
        IEnumerable<uint> childSlaveObjIdsDeepestFirst)
    {
        var children = new List<uint>();
        if (childSlaveObjIdsDeepestFirst != null)
        {
            foreach (var childId in childSlaveObjIdsDeepestFirst)
            {
                if (childId != 0 && childId != hullObjId && !children.Contains(childId))
                    children.Add(childId);
            }
        }

        children.Reverse();

        var ids = new List<uint>();
        if (hullObjId != 0)
            ids.Add(hullObjId);
        ids.AddRange(children);
        return ids;
    }
}
