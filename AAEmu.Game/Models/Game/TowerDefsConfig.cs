namespace AAEmu.Game.Models.Game;

/// <summary>
/// Server-owned Event Center schedule overlays for <c>tower_defs</c>.
/// Bind from <c>AAEmu.Game/Configurations/TowerDefs.json</c> under <c>TowerDefs</c>.
/// </summary>
/// <remarks>
/// Contract:
/// <list type="bullet">
/// <item>WallClock normally comes from weekday StartTimes on the loaded row. When a shipped
/// compact zeros those slots (00:00 = unused) but Event Center still lists Server Time, fill them
/// via <see cref="WallClockStartTimesById"/> (UTC HH:mm by DayOfWeek name or 0–6).</item>
/// <item>GameTime is explicit membership in <see cref="GameTimeAutoArmIds"/> (ids must exist in
/// <c>tower_defs</c>, have <c>tod_day_interval</c> and <c>target_npc_spawner_id</c>, and must not
/// also carry weekday slots).</item>
/// <item>Every other row is Manual. ToD-capable rows omitted from GameTime stay Manual and are
/// logged at load so a stale overlay is visible.</item>
/// <item>Display names never classify events. Portal exclusion uses
/// <c>target_npc_spawner_id</c>, not family labels.</item>
/// </list>
/// </remarks>
public class TowerDefsConfig
{
    /// <summary>
    /// <c>tower_defs.id</c> values that auto-arm when simulated game hour crosses the row's
    /// <c>tod</c>. Empty means no Game-Time auto-arm.
    /// </summary>
    public List<uint> GameTimeAutoArmIds { get; set; } = [];

    /// <summary>
    /// UTC weekday start times by <c>tower_defs.id</c> when compact <c>start_hour*</c> is empty.
    /// Inner keys: <c>Sunday</c>…<c>Saturday</c> or <c>0</c>…<c>6</c> (<see cref="DayOfWeek"/>).
    /// Values: <c>HH:mm</c> or <c>HH:mm:ss</c>. Listed days overwrite that day's StartTimes slot.
    /// </summary>
    public Dictionary<uint, Dictionary<string, string>> WallClockStartTimesById { get; set; } = new();

    /// <summary>
    /// When a tower opens its final progression step, also start this follow-on id (Manual).
    /// Keys/values are <c>tower_defs.id</c> (e.g. Abyssal Assault 36 → victory reward 37).
    /// </summary>
    public Dictionary<uint, uint> FollowOnTowerDefById { get; set; } = new();

    /// <summary>
    /// World positions for <c>DoodadAlmighty</c> prog spawn targets, keyed by <c>tower_defs.id</c>.
    /// Required when Zone does not place those templates. Empty list ⇒ skip spawn (no invented coords).
    /// Each entry is one world-space placement for <see cref="TowerDefProgDoodadPlacement.TemplateId"/>.
    /// </summary>
    public Dictionary<uint, List<TowerDefProgDoodadPlacement>> ProgDoodadPlacementsByTowerDefId { get; set; } = new();
}

/// <summary>One World-authored tower prog doodad placement (world XYZ + yaw degrees).</summary>
public class TowerDefProgDoodadPlacement
{
    public uint TemplateId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>Yaw in degrees (same convention as <c>doodad_spawns.json</c>).</summary>
    public float Yaw { get; set; }
}
