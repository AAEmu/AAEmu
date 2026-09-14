namespace AAEmu.Game.Models.Game.World.Transform;

/// <summary>
/// A housing area is its own zone key on top of the base zone underneath it (base 213 → housing 207).
/// A teleport or position update inside the housing area resolves to the base key, which reads as a
/// zone change and hands the character to a zone they never left. While the character still stands
/// inside the housing zone they occupy, that zone has to win.
/// </summary>
public static class HousingZoneRetentionRules
{
    /// <summary>
    /// True when the character's current housing zone (the one they are in) still covers their position,
    /// so the re-resolved base key must not replace it.
    /// </summary>
    public static bool ShouldKeepOldZone(uint lastZoneKey, IReadOnlyList<uint> housingZonesAtPosition)
    {
        return lastZoneKey != 0 && housingZonesAtPosition.Contains(lastZoneKey);
    }

    /// <summary>
    /// True when a re-resolved key must be reverted: the unit did not leave the housing zone it was in.
    /// </summary>
    public static bool ShouldSuppressChange(uint lastZoneKey, uint newZoneKey, IReadOnlyList<uint> housingZonesAtPosition)
    {
        return lastZoneKey != 0 && newZoneKey != 0 && lastZoneKey != newZoneKey &&
               ShouldKeepOldZone(lastZoneKey, housingZonesAtPosition);
    }
}
