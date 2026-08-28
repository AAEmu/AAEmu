namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Config switch for floor height policy (Path / GeoDataMode is separate).
/// </summary>
public enum FloorSourceMode : byte
{
    /// <summary>
    /// Outdoor/open world uses terrain Blerp; zone/.bai worlds may use nav surface.
    /// </summary>
    TerrainFirst = 0,

    /// <summary>
    /// Pre-split behavior: prefer nearest .bai node via GeoData.GetHeight.
    /// </summary>
    Legacy = 1
}
