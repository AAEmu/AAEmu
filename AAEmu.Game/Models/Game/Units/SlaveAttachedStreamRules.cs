namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Which attached equipment follows its hull on a character's slave stream.
/// </summary>
/// <remarks>
/// A hull's equipment (sails, figurehead, cannons) is attached by attach point, not parented as a
/// Transform child, so the region walk that re-adds a hull does not walk them. The client, however,
/// drops attached units together with the hull it lost. Without putting them back the hull returns
/// bare — the ship shows no sails until a relog — so the World has to release the children's stream
/// slots with the hull and re-send their state with it.
/// </remarks>
public static class SlaveAttachedStreamRules
{
    /// <summary>True when this child takes part in the hull's client stream (both are live units).</summary>
    public static bool ShouldFollowHull(uint hullObjId, uint childObjId, int childAttachPointId)
        => hullObjId != 0 && childObjId != 0 && childAttachPointId >= 0;

    /// <summary>The children that must be released and re-sent together with the hull.</summary>
    public static IEnumerable<uint> ChildObjIdsToFollow(
        uint hullObjId, IEnumerable<(uint ObjId, int AttachPointId)> children)
    {
        foreach (var (childObjId, childAttachPointId) in children)
            if (ShouldFollowHull(hullObjId, childObjId, childAttachPointId))
                yield return childObjId;
    }
}
