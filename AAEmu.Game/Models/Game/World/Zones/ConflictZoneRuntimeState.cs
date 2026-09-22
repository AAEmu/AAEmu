namespace AAEmu.Game.Models.Game.World.Zones;

public readonly record struct ConflictZoneRuntimeState(
    ushort ZoneGroupId,
    ZoneConflictType State,
    uint KillCount,
    uint NpcKillCount,
    uint QuestCompletionCount,
    DateTime NextStateTimeUtc);
