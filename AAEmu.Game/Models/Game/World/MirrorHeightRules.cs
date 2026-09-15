namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Where a spawn's Z ends up. The dedicate lifts what it spawns off the ground, so a record that is a
/// little above the terrain under it is the lift and not a ledge; a record far above it is the spawn's
/// own altitude and has to be left alone.
/// </summary>
public static class MirrorHeightRules
{
    /// <summary>
    /// Largest gap treated as the dedicate's spawn lift. Anything wider is a real difference - a spawn
    /// over a cliff edge, a rift fly-in - and snapping it would drop the unit through the ground.
    /// </summary>
    public const float MaxSnapMetres = 1f;

    /// <summary>
    /// True when a spawn's Z should be replaced by the terrain height under it: the unit rests on the
    /// ground rather than holding an altitude of its own, we have a terrain sample, and the gap is
    /// small enough to be the spawn lift. Floats are how a never-moving unit ends up hovering: the
    /// client paints this Z, and an idle unit gets no movement record to correct it with.
    /// </summary>
    public static bool ShouldSnapToTerrain(bool isOffGround, float spawnZ, float terrainZ) =>
        !isOffGround && terrainZ != 0f && MathF.Abs(spawnZ - terrainZ) <= MaxSnapMetres;
}
