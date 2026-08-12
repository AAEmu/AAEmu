namespace AAEmu.Game.Models.Game;

/// <summary>
/// Server-owned Event Center Game-Time membership overlay for <c>tower_defs</c>.
/// Bind from <c>AAEmu.Game/Configurations/TowerDefs.json</c> under <c>TowerDefs</c>.
/// </summary>
/// <remarks>
/// Contract:
/// <list type="bullet">
/// <item>WallClock is derived from weekday StartTimes on the loaded row.</item>
/// <item>GameTime is explicit membership in <see cref="GameTimeAutoArmIds"/> (ids must exist in
/// <c>tower_defs</c>, have <c>tod_day_interval</c> and <c>target_npc_spawner_id</c>, and must not
/// also carry weekday slots).</item>
/// <item>Every other row is Manual. ToD-capable rows omitted from the list stay Manual and are
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
}
