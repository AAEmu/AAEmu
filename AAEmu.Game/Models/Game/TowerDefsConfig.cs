namespace AAEmu.Game.Models.Game;

/// <summary>
/// Server-owned Event Center schedule overlays for <c>tower_defs</c>.
/// Bind from <c>AAEmu.Game/Configurations/TowerDefs.json</c> under <c>TowerDefs</c>.
/// </summary>
/// <remarks>
/// Contract:
/// <list type="bullet">
/// <item>Authoritative event rows and progression come from loaded <c>tower_defs</c> /
/// <c>tower_def_progs</c> / spawn-target tables. This JSON only overlays schedule membership and
/// World-authored doodad placements the Zone pipeline does not emit.</item>
/// <item>WallClock normally comes from weekday StartTimes on the loaded row. When those slots are
/// unused (null / compact 00:00), fill them via <see cref="WallClockStartTimesById"/> (UTC HH:mm).
/// Existing non-empty slots are never overwritten; conflicts are logged and rejected.</item>
/// <item>GameTime is explicit membership in <see cref="GameTimeAutoArmIds"/> (ids must exist in
/// <c>tower_defs</c>, have <c>tod_day_interval</c> and <c>target_npc_spawner_id</c>, and must not
/// also carry weekday slots).</item>
/// <item><see cref="FollowOnTowerDefById"/> links loaded ids only (final-step start of the target).</item>
/// <item><see cref="ProgDoodadPlacementsByTowerDefId"/> supplies world XYZ for DoodadAlmighty
/// targets when Zone ChangeStep does not place them. Empty ⇒ skip (no invented coords). Template
/// ids must match that step's spawn targets; optional <c>ZoneId</c> owns the placement.</item>
/// <item>Every other row is Manual. Display names never classify events.</item>
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
    /// UTC weekday start times by <c>tower_defs.id</c> when compact weekday slots are unused.
    /// Inner keys: day name or <c>0</c>…<c>6</c>. Values: <c>HH:mm</c> or <c>HH:mm:ss</c>.
    /// Only fills empty slots; conflicts with existing StartTimes are errors.
    /// </summary>
    public Dictionary<uint, Dictionary<string, string>> WallClockStartTimesById { get; set; } = new();

    /// <summary>
    /// When a tower opens its final progression step, also start this follow-on id (Manual).
    /// Keys/values are <c>tower_defs.id</c>.
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
    /// <summary>Yaw in degrees (same convention as permanent doodad spawn exports).</summary>
    public float Yaw { get; set; }
    /// <summary>
    /// Optional owning zone key. When 0, World resolves zone from the placement XYZ in the
    /// target <c>WorldInstance</c> template.
    /// </summary>
    public uint ZoneId { get; set; }
}
