using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// One row of <c>conflict_zone_realtime_schedules</c>: a war-state change for a conflict zone at a
/// wall-clock time. <see cref="DayOfWeekId"/> follows <c>enum_day_of_weeks</c> (1 = sunday …
/// 7 = saturday, 8 = invalid). <see cref="TimeOfDayHhmm"/> is military HHMM as stored in the table
/// (2320 = 23:20, 20 = 00:20) — it is not minutes-of-day.
/// </summary>
public readonly record struct ConflictZoneScheduleEntry(byte DayOfWeekId, int TimeOfDayHhmm, ZoneConflictType WarState);

/// <summary>
/// One row of <c>conflict_zone_npc_spawners</c>: a zone-local NPC spawner placement that should be
/// armed while the conflict zone is in <see cref="ZoneStateKindId"/> (see
/// <see cref="ConflictZoneStateKind"/>). <see cref="NpcSpawnerId"/> is the placement id from the
/// zone's own spawner catalog, not <c>npc_spawners.id</c> from compact.
/// </summary>
public readonly record struct ConflictZoneSpawnerEntry(uint NpcSpawnerId, ConflictZoneStateKind ZoneStateKindId, bool SpawnActivate, bool UseDespawn);

/// <summary>
/// The conflict-zone participation tables — <c>conflict_zone_realtime_schedules</c>,
/// <c>conflict_zone_npc_kills</c>, <c>conflict_zone_quest_completions</c> and
/// <c>conflict_zone_npc_spawners</c> — keyed by <c>conflict_zones.zone_group_id</c>.
/// </summary>
/// <remarks>
/// <c>conflict_zones</c> itself (thresholds, durations, return points) is loaded by
/// <see cref="Core.Managers.World.ZoneManager"/>; this loader owns the child tables that drive
/// scheduled war-state transitions and kill/quest participation.
/// </remarks>
[GameData]
public class ConflictZoneGameData : Singleton<ConflictZoneGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<ushort, List<ConflictZoneScheduleEntry>> _schedules = [];
    private Dictionary<ushort, HashSet<uint>> _participationNpcs = [];
    private Dictionary<ushort, HashSet<uint>> _participationQuests = [];
    private Dictionary<ushort, List<ConflictZoneSpawnerEntry>> _spawners = [];

    public void Load(SqliteConnection connection)
    {
        _schedules = [];
        _participationNpcs = [];
        _participationQuests = [];
        _spawners = [];

        LoadRealtimeSchedules(connection);
        LoadParticipationNpcs(connection);
        LoadParticipationQuests(connection);
        LoadStateSpawners(connection);

        var scheduledRows = _schedules.Values.Sum(x => x.Count);
        Logger.Info("Loaded {0} conflict-zone war-state schedule entries across {1} zone groups",
            scheduledRows, _schedules.Count);
        Logger.Info("Loaded conflict-zone participation lists: {0} npc-kill zones, {1} quest-completion zones",
            _participationNpcs.Count, _participationQuests.Count);
        Logger.Info("Loaded {0} conflict-zone state spawner rows across {1} zone groups",
            _spawners.Values.Sum(x => x.Count), _spawners.Count);
    }

    public void PostLoad()
    {
    }

    /// <summary>Wall-clock war-state changes for a zone group, or an empty list when it is not scheduled.</summary>
    public IReadOnlyList<ConflictZoneScheduleEntry> GetSchedule(ushort zoneGroupId) =>
        _schedules.GetValueOrDefault(zoneGroupId) ?? [];

    /// <summary>True when this NPC template counts toward the zone's conflict escalation
    /// (<c>conflict_zone_npc_kills</c>).</summary>
    public bool IsParticipatingNpc(ushort zoneGroupId, uint npcTemplateId) =>
        _participationNpcs.TryGetValue(zoneGroupId, out var ids) && ids.Contains(npcTemplateId);

    /// <summary>True when finishing this quest counts toward the zone's conflict escalation
    /// (<c>conflict_zone_quest_completions.context_id</c>, which is a <c>quest_contexts.id</c>).</summary>
    public bool IsParticipatingQuest(ushort zoneGroupId, uint questId) =>
        _participationQuests.TryGetValue(zoneGroupId, out var ids) && ids.Contains(questId);

    /// <summary>
    /// Zone-local spawner placements armed for a zone state (see <see cref="ConflictZoneStateKind"/>).
    /// The ids are placement ids from the zone's own <c>npc_spawners.g</c> catalog, so arming them
    /// belongs to the World/zone-host relay that owns those placements, not the Game server.
    /// </summary>
    public IReadOnlyList<ConflictZoneSpawnerEntry> GetSpawners(ushort zoneGroupId) =>
        _spawners.GetValueOrDefault(zoneGroupId) ?? [];

    private void LoadRealtimeSchedules(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conflict_zone_realtime_schedules";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var zoneGroupId = (ushort)reader.GetUInt32("conflict_zone_id");
            var entry = new ConflictZoneScheduleEntry(
                (byte)reader.GetInt32("day_of_week_id", 0),
                reader.GetInt32("time_of_day", 0),
                (ZoneConflictType)reader.GetInt32("war_state_id", 0));

            if (!_schedules.TryGetValue(zoneGroupId, out var list))
                _schedules[zoneGroupId] = list = [];
            list.Add(entry);
        }
    }

    private void LoadParticipationNpcs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT conflict_zone_id, npc_id FROM conflict_zone_npc_kills";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var npcId = reader.GetUInt32("npc_id", 0);
            if (npcId == 0)
                continue;

            var zoneGroupId = (ushort)reader.GetUInt32("conflict_zone_id");
            if (!_participationNpcs.TryGetValue(zoneGroupId, out var ids))
                _participationNpcs[zoneGroupId] = ids = [];
            ids.Add(npcId);
        }
    }

    private void LoadParticipationQuests(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT conflict_zone_id, context_id FROM conflict_zone_quest_completions";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var contextId = reader.GetUInt32("context_id", 0);
            if (contextId == 0)
                continue;

            var zoneGroupId = (ushort)reader.GetUInt32("conflict_zone_id");
            if (!_participationQuests.TryGetValue(zoneGroupId, out var ids))
                _participationQuests[zoneGroupId] = ids = [];
            ids.Add(contextId);
        }
    }

    private void LoadStateSpawners(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conflict_zone_npc_spawners";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var spawnerId = reader.GetUInt32("npc_spawner_id", 0);
            if (spawnerId == 0)
                continue;

            var zoneGroupId = (ushort)reader.GetUInt32("conflict_zone_id");
            var entry = new ConflictZoneSpawnerEntry(
                spawnerId,
                (ConflictZoneStateKind)reader.GetInt32("zone_state_kind_id", 0),
                reader.GetBoolean("spawn_activate", false),
                reader.GetBoolean("use_despawn", false));

            if (!_spawners.TryGetValue(zoneGroupId, out var list))
                _spawners[zoneGroupId] = list = [];
            list.Add(entry);
        }
    }
}
