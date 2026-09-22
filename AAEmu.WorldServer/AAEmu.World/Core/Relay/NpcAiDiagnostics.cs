using AAEmu.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.World.Core.Network;

namespace AAEmu.World.Core.Relay;

/// <summary>Small, read-only snapshots used to correlate NPC AI traffic with its actual Zone host.</summary>
public static class NpcAiDiagnostics
{
    public readonly record struct UnitSnapshot(
        bool Tracked,
        bool IsNpc,
        bool IsPlayer,
        uint TemplateId,
        uint SpawnerId,
        uint SpawnerType,
        uint AiFileId,
        string AiFileName,
        uint NpcAiParamId,
        uint TransformZoneId,
        uint TransformInstanceId);

    /// <remarks><see cref="UnitSnapshot.Tracked"/> is World relay-registry state, not a native lookup acknowledgement.</remarks>
    public static UnitSnapshot Snapshot(ZoneConnection connection, uint unitId)
    {
        var tracked = connection.Units.TryGet(unitId, out var raw);
        var worldUnit = WorldIntegration.FindUnitAcrossWorlds(unitId);
        return SnapshotFrom(worldUnit as Unit, tracked, raw);
    }

    internal static UnitSnapshot SnapshotFrom(Unit? worldUnit, bool tracked, byte[]? raw)
    {
        // Player WZUnitState and NPC ZWSpawn bodies share no discriminator. A known non-NPC
        // must never be fed to the spawn parser or its leading bytes can look like template metadata.
        var spawn = (worldUnit is null or Npc) && raw != null ? ZwSpawnNpcParser.TryParse(raw) : null;

        var npcTemplate = (worldUnit as Npc)?.Template;
        return new UnitSnapshot(
            tracked,
            worldUnit is Npc || spawn != null,
            worldUnit is Character,
            spawn?.TemplateId ?? (worldUnit as Npc)?.TemplateId ?? 0,
            spawn?.SpawnerId ?? 0,
            spawn?.SpawnerType ?? 0,
            npcTemplate?.AiFileId ?? 0,
            npcTemplate?.AiFileName ?? string.Empty,
            npcTemplate?.NpcAiParamId ?? 0,
            worldUnit?.Transform?.ZoneId ?? 0,
            worldUnit?.Transform?.InstanceId ?? 0);
    }

    public static string Source(ZoneConnection connection) =>
        $"sourceZone={connection.ZoneId} sourceInstance={connection.InstanceId} sourceSession={connection.Id}";

    public static string Unit(ZoneConnection connection, uint unitId)
    {
        var snapshot = Snapshot(connection, unitId);
        return $"unit={unitId} tracked={snapshot.Tracked} npc={snapshot.IsNpc} player={snapshot.IsPlayer} " +
               $"template={snapshot.TemplateId} spawner={snapshot.SpawnerId} spawnerType={snapshot.SpawnerType} " +
               $"aiFile={snapshot.AiFileId}:{snapshot.AiFileName} aiParam={snapshot.NpcAiParamId} " +
               $"transformZone={snapshot.TransformZoneId} transformInstance={snapshot.TransformInstanceId}";
    }
}
