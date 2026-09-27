using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Skip Floor / NavSurface resample on NPC move ticks when path-follow (or zone BAI chase)
/// can reuse XY-lerped waypoint / target Z from <see cref="FloorQuery.ApplyPathWaypointZ"/>.
/// </summary>
public static class PathLocomotionZ
{
    private const float WaypointMatchDistSq = 1f;
    private const float MaxVerticalTargetSep = 50f;

    /// <summary>
    /// When true, use XY-lerped Z from <see cref="PositionAndRotation.AddDistanceToFront"/> instead of
    /// <see cref="AAEmu.Game.Core.Managers.World.WorldManager.GetReferenceHeight"/> (full floor query).
    /// </summary>
    public static bool ShouldUseLerpedMoveHeight(
        bool geoDataMode,
        bool hasZoneBai,
        bool hasPathQueue,
        Vector3 moveTarget,
        Vector3 pathPeek,
        float currentZ)
    {
        if (!geoDataMode)
            return false;

        if (hasPathQueue && Vector3.DistanceSquared(pathPeek, moveTarget) <= WaypointMatchDistSq)
            return true;

        // Zone/.bai instances: trust lerp toward target Z (player or waypoint) on move ticks.
        if (hasZoneBai && moveTarget.Z != 0f && MathF.Abs(moveTarget.Z - currentZ) <= MaxVerticalTargetSep)
            return true;

        return false;
    }
}
