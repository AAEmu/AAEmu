namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>Stable event family for tower_defs rows (not derived from display names).</summary>
public enum TowerDefEventFamily : byte
{
    Unspecified = 0,
    Crimson = 1,
    Grimghast = 2,
    Oblivion = 3,
    Clockwork = 4,
    Other = 255
}

/// <summary>Base / expand / guide relationship for a tower_def row.</summary>
public enum TowerDefEventVariant : byte
{
    Unspecified = 0,
    Base = 1,
    Expand = 2,
    Guide = 3
}

/// <summary>
/// How World auto-arms this event. Wall-clock slots use <see cref="TowerDef.StartTimes"/>;
/// GameTime uses simulated hour / <see cref="TowerDef.TimeOfDay"/>.
/// </summary>
public enum TowerDefScheduleMode : byte
{
    /// <summary>GM / API start only (or incomplete config).</summary>
    Manual = 0,
    /// <summary>UTC weekday StartTimes windows.</summary>
    WallClock = 1,
    /// <summary>Simulated game-hour ToD crossing (Event Center "Game Time" strip).</summary>
    GameTime = 2
}
