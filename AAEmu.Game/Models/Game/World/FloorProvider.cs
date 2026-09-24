namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Which provider won a single floor query (<c>src=</c> in FloorDebug logs).
/// Not the same as <see cref="FloorPolicyMode"/> (config policy among candidates).
/// Keep enum names stable for log parsers / scripts.
/// </summary>
public enum FloorProvider : byte
{
    /// <summary>
    /// Nearest .bai vertex via GeoData.GetHeight.
    /// Used in Legacy policy and as last-resort when ByZHint has no terrain/surface — not only when mode is Legacy.
    /// </summary>
    LegacyNavNode = 0,

    /// <summary>Heightmap bilinear interpolation (WorldTemplate.GetHeight).</summary>
    Terrain = 1,

    /// <summary>Projected onto a navmesh edge with zHint (caves / multi-floor).</summary>
    NavSurface = 2,

    /// <summary>Caller kept zHint / no floor data.</summary>
    Unchanged = 3
}
