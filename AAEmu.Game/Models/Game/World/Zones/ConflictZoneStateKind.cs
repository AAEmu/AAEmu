namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// <c>enum_conflict_zone_state_kinds</c>: which zone state a <c>conflict_zone_npc_spawners</c> row
/// is armed for. 0 = none.
/// </summary>
public enum ConflictZoneStateKind : byte
{
    None = 0,
    Peace = 1,
    War = 2
}
