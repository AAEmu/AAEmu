namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Events accepted by the native Zone NPC spawner handler
/// Event 9 is intentionally omitted because its native meaning is not yet established.
/// </summary>
public enum NpcSpawnerEvent : sbyte
{
    Activate = 1,
    Deactivate = 2,
    SpawnPersistInstanceNpc = 3,
    DeactivateAndRetire = 4,
    RespawnAllOnce = 5,
    DespawnAll = 6,
    SpawnAllOnce = 7,
    SpawnAllOnceAndDeactivate = 8
}

/// <summary>
/// gates. Context 0 is the ordinary skill/script path.
/// </summary>
public enum NpcSpawnerEventType
{
    Default = 0,
    TowerDefense = 1,
    GameSchedule = 2
}

/// <summary>
/// </summary>
public enum NpcSpawnReasonType : sbyte
{
    Default = 0,
    /// <summary>Carries a complete <c>CastAction</c> union.</summary>
    Fishing = 6
}
