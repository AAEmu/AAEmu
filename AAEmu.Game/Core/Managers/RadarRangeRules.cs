namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Finder / telescope visibility uses the registered buff range only.
/// Access level must not widen that circle.
/// </summary>
public static class RadarRangeRules
{
    public static bool IsInRange(float distance, float range) =>
        range > 0f && distance <= range;
}
