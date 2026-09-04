namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Which zones still hold a hull's attachments, and which object ids must stay reserved until
/// those attachments have been withdrawn and hidden.
/// </summary>
/// <remarks>
/// A hull can visit several dedicades, then despawn. The portal path used to detach sails,
/// figureheads and doodads without telling those dedicades, then recycle the ids as soon as
/// the delayed despawn tick ran. The next ship reused the same ids; crossing back into a
/// zone that still had the old children parented them onto the new hull.
/// </remarks>
public static class BoatDespawnRules
{
    /// <summary>
    /// Zones that may still have this hull or an attachment: the live simulator, a pending
    /// create, and any attachment whose own zone key differs (a child can sit across a seam).
    /// </summary>
    public static IReadOnlyList<uint> ZonesThatMayHoldAttachments(
        uint announcedTo,
        uint pendingFor,
        IEnumerable<uint> attachmentZoneIds)
    {
        var zones = new HashSet<uint>();
        if (announcedTo != 0)
            zones.Add(announcedTo);
        if (pendingFor != 0)
            zones.Add(pendingFor);
        if (attachmentZoneIds == null)
            return [.. zones.OrderBy(z => z)];

        foreach (var zoneId in attachmentZoneIds)
        {
            if (zoneId != 0)
                zones.Add(zoneId);
        }

        return [.. zones.OrderBy(z => z)];
    }

    /// <summary>
    /// Unit ids a dedicate must drop for this hull: every child slave, then the hull itself.
    /// Children go first so a parent remove cannot leave them attached to a recycled id.
    /// </summary>
    public static IReadOnlyList<uint> UnitIdsToRemoveFromZone(uint hullObjId, IEnumerable<uint> childSlaveObjIds)
    {
        var ids = new List<uint>();
        if (childSlaveObjIds != null)
        {
            foreach (var childId in childSlaveObjIds)
            {
                if (childId != 0 && childId != hullObjId && !ids.Contains(childId))
                    ids.Add(childId);
            }
        }

        if (hullObjId != 0)
            ids.Add(hullObjId);
        return ids;
    }

    /// <summary>Attached doodad ids that must be removed from the same dedicades as the hull.</summary>
    public static IReadOnlyList<uint> DoodadIdsToRemoveFromZone(IEnumerable<uint> attachedDoodadObjIds)
    {
        if (attachedDoodadObjIds == null)
            return [];

        var ids = new List<uint>();
        foreach (var doodadId in attachedDoodadObjIds)
        {
            if (doodadId != 0 && !ids.Contains(doodadId))
                ids.Add(doodadId);
        }

        return ids;
    }

    /// <summary>
    /// Broadcast object ids that must stay reserved until withdraw + hide finish. Recycling any
    /// of these while a leftover child still names the old hull as parent is what attached
    /// another ship's masts to a newly summoned hull.
    /// </summary>
    public static IReadOnlyList<uint> ObjectIdsHeldUntilFinalize(
        uint hullObjId,
        IEnumerable<uint> childSlaveObjIds,
        IEnumerable<uint> attachedDoodadObjIds)
    {
        var ids = new HashSet<uint>();
        if (hullObjId != 0)
            ids.Add(hullObjId);
        if (childSlaveObjIds != null)
        {
            foreach (var childId in childSlaveObjIds)
            {
                if (childId != 0)
                    ids.Add(childId);
            }
        }

        if (attachedDoodadObjIds != null)
        {
            foreach (var doodadId in attachedDoodadObjIds)
            {
                if (doodadId != 0)
                    ids.Add(doodadId);
            }
        }

        return [.. ids.OrderBy(id => id)];
    }
}
