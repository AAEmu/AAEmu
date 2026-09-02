namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Create order for a hull and its equipment in a dedicate. Drop order is the reverse
/// (see <see cref="BoatDespawnRules.UnitIdsToRemoveFromZone"/>).
/// </summary>
public static class BoatAttachmentAnnounceRules
{
    /// <summary>
    /// Attachment doodads (ladders, helm, anchor, cargo props) stay World-side. The dedicate
    /// physicalizes a Created doodad as an immovable collider parented to the hull, and the
    /// ladder proxies sit inside the hull's own collision mesh: a permanent contact that shoves
    /// the hull sideways and down every frame. Live 2026-09-02, Ostera (slave 75): with the
    /// doodads announced the hull heeled 0° → 47° within 8 s of Create and capsized; with them
    /// withheld it sat at Z 100.44, tilt 1.3°, |v| 0 with the controller armed, and sailed with
    /// the full kit aboard. Equipment slaves (sails, cannons) are units and still go to the zone.
    /// </summary>
    public static bool AnnounceDoodadsToZone => false;

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

    /// <summary>
    /// Equipment children the dedicate must AttachTo after Create so they join the hull's
    /// child-model list (that is the list the mass refresh walks). Skip unset attach points.
    /// </summary>
    public static IReadOnlyList<(uint ChildObjId, uint HullObjId, byte AttachPoint)> ChildAttachesForZone(
        uint hullObjId,
        IEnumerable<(uint ChildObjId, sbyte AttachPoint)> children)
    {
        var attaches = new List<(uint ChildObjId, uint HullObjId, byte AttachPoint)>();
        if (hullObjId == 0 || children == null)
            return attaches;

        foreach (var (childObjId, attachPoint) in children)
        {
            if (childObjId == 0 || childObjId == hullObjId || attachPoint < 0)
                continue;
            attaches.Add((childObjId, hullObjId, (byte)attachPoint));
        }

        return attaches;
    }
}
