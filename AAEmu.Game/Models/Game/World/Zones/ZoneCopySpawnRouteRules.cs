namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Picks the zone-copy id a World-authored Create must name.
/// </summary>
/// <remarks>
/// Several loaded copies of the same dungeon zone key have no unique host. Addressing
/// only the zone key then drops the Create. The World instance id is that copy; an
/// unplaced transform must not be treated as one.
/// </remarks>
public static class ZoneCopySpawnRouteRules
{
    /// <summary>
    /// Instance id to put on a World-authored Create. Zero means "the unique host for this zone key".
    /// </summary>
    public static uint InstanceIdForSpawn(uint parentWorldId, uint transformInstanceId)
    {
        if (parentWorldId != 0)
            return parentWorldId;
        if (transformInstanceId != 0 && transformInstanceId != AAEmu.Game.Models.Game.World.Transform.Transform.NoInstanceId)
            return transformInstanceId;
        return 0;
    }
}
