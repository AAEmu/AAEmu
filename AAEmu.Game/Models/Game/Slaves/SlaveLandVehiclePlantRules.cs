namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Floor Z for client-driven land vehicles (carts / tanks / siege).
/// </summary>
/// <remarks>
/// Wheeled contact is intolerant of a hover: shipped suspension strokes are often ~0.1 m, so a
/// plant 0.5–2 m above dirt leaves the wheels spinning with no bite. <c>GeoData.GetHeight</c>
/// picks the nearest .bai node in 3D and routinely sits that far above the walkable floor the
/// driver is standing on — prefer the XY heightmap, then the owner's feet, and never GeoData
/// when it would lift the chassis. Do not add NPC <c>TerrainFloor.ClearanceMetres</c>; that
/// alone exceeds a typical wheel stroke.
/// </remarks>
public static class SlaveLandVehiclePlantRules
{
    /// <summary>
    /// Heightmap may differ from the driver's feet by this much and still be treated as the same
    /// patch of dirt (ramps / kerbs). Beyond that, trust the owner.
    /// </summary>
    public const float OwnerFloorBandMetres = 2f;

    /// <summary>
    /// Client seed Z may sit this far from the owner's feet. Sanity bound for a crafted
    /// <c>CSSpawnSlave</c> height, not a content radius — XY still uses
    /// <c>slaves.spawn_valid_area_range</c>.
    /// </summary>
    public const float ClientSeedZBandMetres = 15f;

    /// <summary>
    /// Pick the plant Z for a land-vehicle summon.
    /// </summary>
    /// <param name="ownerZ">Driver's world Z (feet on dirt).</param>
    /// <param name="heightmapZ">XY heightmap sample, or 0 when heightmaps are off/missing.</param>
    /// <param name="geoDataZ">Optional GeoData sample — used only when both other sources are unusable.</param>
    public static float ChoosePlantZ(float ownerZ, float heightmapZ, float geoDataZ = 0f)
    {
        if (heightmapZ > 0f &&
            (ownerZ == 0f || MathF.Abs(heightmapZ - ownerZ) <= OwnerFloorBandMetres))
            return heightmapZ;

        if (ownerZ != 0f)
            return ownerZ;

        if (heightmapZ > 0f)
            return heightmapZ;

        return geoDataZ > 0f ? geoDataZ : ownerZ;
    }
}
