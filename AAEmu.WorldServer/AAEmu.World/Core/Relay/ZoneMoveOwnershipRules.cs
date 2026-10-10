using GameTransform = AAEmu.Game.Models.Game.World.Transform.Transform;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Which zone may report a mirrored unit's movement. Unit ids return to a shared pool, so a zone
/// still simulating a unit the World already dropped can name an id that now belongs to a unit in
/// another zone; only the zone (and copy) the unit lives in may move it.
/// </summary>
public static class ZoneMoveOwnershipRules
{
    public static bool IsOwnedBy(uint sourceZoneId, uint sourceInstanceId, uint unitZoneId, uint unitInstanceId)
    {
        if (unitZoneId == 0)
            return true;
        if (sourceZoneId != unitZoneId)
            return false;
        if (unitInstanceId == GameTransform.NoInstanceId)
            return true;
        return sourceInstanceId == unitInstanceId;
    }
}
