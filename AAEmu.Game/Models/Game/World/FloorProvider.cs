namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Which provider produced the floor Z for a single query (see also config <see cref="FloorSourceMode"/>).
/// Log/script token after <c>src=</c> — keep enum names stable.
/// </summary>
public enum FloorProvider : byte
{
    /// <summary>Nearest .bai node (legacy GeoData.GetHeight).</summary>
    LegacyNavNode = 0,

    /// <summary>Heightmap bilinear interpolation (WorldTemplate.GetHeight).</summary>
    Terrain = 1,

    /// <summary>Projected onto a navmesh edge with zHint (caves / multi-floor).</summary>
    NavSurface = 2,

    /// <summary>Caller kept zHint / no floor data.</summary>
    Unchanged = 3
}
