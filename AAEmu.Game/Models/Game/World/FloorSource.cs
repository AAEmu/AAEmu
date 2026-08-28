namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Which provider produced the floor Z.
/// </summary>
public enum FloorSource : byte
{
    /// <summary>Nearest .bai node (legacy GeoData.GetHeight).</summary>
    LegacyNavNode = 0, // keep name stable for logs/scripts

    /// <summary>Heightmap bilinear interpolation (WorldTemplate.GetHeight).</summary>
    Terrain = 1,

    /// <summary>Projected onto a navmesh edge/triangle with zHint.</summary>
    NavSurface = 2,

    /// <summary>Caller kept zHint / no floor data.</summary>
    Unchanged = 3
}
