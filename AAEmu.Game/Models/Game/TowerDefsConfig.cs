namespace AAEmu.Game.Models.Game;

/// <summary>
/// Authoritative schedule classification for <c>tower_defs</c> rows.
/// Configure in <c>AAEmu.Game/Configurations/TowerDefs.json</c> under <c>TowerDefs</c>.
/// Display names are localization only and must not drive server behavior.
/// </summary>
public class TowerDefsConfig
{
    /// <summary>
    /// Rows that auto-arm on simulated game-hour crossings. Missing ids stay Manual unless
    /// they carry weekday StartTimes (those become WallClock from data alone).
    /// </summary>
    public List<TowerDefScheduleEntryConfig> GameTimeAutoArm { get; set; } = [];
}

public class TowerDefScheduleEntryConfig
{
    public uint Id { get; set; }
    public string Family { get; set; } = "Unspecified";
    public string Variant { get; set; } = "Unspecified";
}
