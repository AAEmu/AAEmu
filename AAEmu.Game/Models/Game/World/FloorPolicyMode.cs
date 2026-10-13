namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Config switch for floor height policy (Path / GeoDataMode is separate).
/// </summary>
/// <remarks>
/// Policy chooses among candidates; the winner of a single query is <see cref="FloorProvider"/> (<c>src=</c> in logs).
/// </remarks>
public enum FloorPolicyMode : byte
{
    /// <summary>
    /// Heightmap + nav-surface candidates; pick nearest to zHint in a vertical window
    /// (<see cref="FloorResolver"/>). Not terrain-only.
    /// </summary>
    ByZHint = 0,

    /// <summary>
    /// Pre-split behavior: prefer nearest .bai node via GeoData.GetHeight.
    /// </summary>
    Legacy = 1,

    /// <summary>Obsolete config/GM alias for <see cref="ByZHint"/>.</summary>
    TerrainFirst = ByZHint
}
