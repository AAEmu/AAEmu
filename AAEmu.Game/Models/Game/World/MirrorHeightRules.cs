namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Where a spawn's Z ends up. The dedicate lifts what it spawns off the ground, so a record that is a
/// little above the terrain under it is the lift and not a ledge; a record far above it is the spawn's
/// own altitude and has to be left alone.
/// </summary>
public static class MirrorHeightRules
{
    /// <summary>
    /// Largest gap a zone mirror is snapped across: the dedicate's lift, measured at about 0.4 m, with
    /// a little margin. It stays this tight because the terrain read has no prop geometry in it - a
    /// unit legitimately standing 0.6 m up on a porch, dock or stair is that far above the heightmap,
    /// and a wider band would pull it down into the prop it is standing on.
    /// </summary>
    public const float MaxMirrorSnapMetres = 0.5f;

    /// <summary>
    /// The World spawner's own tolerance. Its positions come from the spawn files, are authored on the
    /// terrain, and can sit up to a metre off the bilinear read; that is a different question from a
    /// zone record, so it keeps the metre it always had.
    /// </summary>
    public const float MaxSpawnFileSnapMetres = 1f;

    /// <summary>
    /// True when a spawn's Z should be replaced by the terrain height under it: the unit rests on the
    /// ground rather than holding an altitude of its own, we have a terrain sample, and the gap is
    /// small enough to be the spawn lift. Floats are how a never-moving unit ends up hovering: the
    /// client paints this Z, and an idle unit gets no movement record to correct it with.
    /// </summary>
    public static bool ShouldSnapToTerrain(
        bool isOffGround, float spawnZ, float terrainZ, float maxSnapMetres = MaxMirrorSnapMetres) =>
        !isOffGround && terrainZ != 0f && MathF.Abs(spawnZ - terrainZ) <= maxSnapMetres;
}
