using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class TowerDefGameData : Singleton<TowerDefGameData>, IGameDataLoader
{
    private Dictionary<uint, TowerDef> _towerDefs;
    private Dictionary<uint, TowerDefProg> _towerDefProgs;

    /// <summary>
    /// Portal + wave <c>NpcSpawner</c> ids from every tower_def / prog spawn target.
    /// They carry optional ToD windows for ambient population, but TowerDef OnEvent forces them
    /// live; the schedule gate must not NpcSpawnFailed those announcements while a wave is on.
    /// </summary>
    private HashSet<uint> _eventSpawnerTemplateIds = [];

    /// <summary>
    /// Portal / stage-spawner <c>Npc</c> members (and group members of those spawners). Soft-stream
    /// priority so rifts stay painted across region hops. Kill-quota infantry are NOT in this set —
    /// marking them priority made wave packs thrash <c>AAEMU_MIRROR_NPC_MAX</c> and flood SCUnitState.
    /// </summary>
    private HashSet<uint> _priorityNpcTemplateIds = [];

    /// <summary>Spawner template id → direct <c>Npc</c> members (portal seed unit ids).</summary>
    private Dictionary<uint, HashSet<uint>> _spawnerMemberNpcs = [];

    /// <summary>NpcGroup id → member npc template ids (kill quotas + wave groups).</summary>
    private Dictionary<uint, HashSet<uint>> _npcGroupMembers = [];

    /// <summary>True after <see cref="Load"/> finishes. Lookups fail closed until then.</summary>
    public bool IsLoaded { get; private set; }

    public void Load(SqliteConnection connection)
    {
        IsLoaded = false;
        _towerDefs = [];
        _towerDefProgs = [];
        _eventSpawnerTemplateIds = [];
        _priorityNpcTemplateIds = [];
        _spawnerMemberNpcs = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_defs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TowerDef
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name", ""),
                        StartMsg = reader.GetString("start_msg", ""),
                        EndMsg = reader.GetString("end_msg", ""),
                        TitleMsg = reader.GetString("title_msg", ""),
                        TimeOfDay = reader.GetFloat("tod"),
                        FirstWaveAfter = reader.GetFloat("first_wave_after"),
                        TargetNpcSpawnId = reader.GetUInt32("target_npc_spawner_id", 0),
                        KillNpcId = reader.GetUInt32("kill_npc_id", 0),
                        KillNpcCount = reader.GetUInt32("kill_npc_count", 0),
                        ForceEndTime = reader.GetFloat("force_end_time"),
                        TimeOfDayDayInterval = reader.GetUInt32("tod_day_interval"),
                        MilestoneId = reader.GetUInt32("milestone_id", 0),
                        BroadcastToWholeWorld = reader.GetBoolean("broadcast_event_to_whole_seamless_world", true),
                        StartDayOfWeekBit = reader.GetUInt32("start_day_of_week_bit", 0),
                        Progs = []
                    };

                    // start_hour/start_minute is the Sunday slot; start_hourN is day N. A 00:00
                    // pair means the event does not run that day — every row that genuinely wants
                    // a midnight start uses 00:01 (망자 시스템 runs 00:01 on all seven days).
                    for (var day = 0; day < 7; day++)
                    {
                        var suffix = day == 0 ? "" : day.ToString();
                        var hour = reader.GetInt32($"start_hour{suffix}", 0);
                        var minute = reader.GetInt32($"start_minute{suffix}", 0);
                        if (hour == 0 && minute == 0)
                            continue;
                        if (hour is < 0 or > 23 || minute is < 0 or > 59)
                            continue;

                        template.StartTimes[day] = new TimeSpan(hour, minute, 0);
                    }

                    _towerDefs.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_progs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefId = reader.GetUInt32("tower_def_id");
                    if (!_towerDefs.TryGetValue(towerDefId, out var towerDef))
                        continue;

                    var template = new TowerDefProg
                    {
                        Id = reader.GetUInt32("id"),
                        TowerDef = towerDef,
                        CondToNextTime = reader.GetFloat("cond_to_next_time"),
                        CondCompByAnd = reader.GetBoolean("cond_comp_by_and", true),
                        KillTargets = [],
                        SpawnTargets = []
                    };

                    towerDef.Progs.Add(template);
                    _towerDefProgs.Add(template.Id, template);
                }
            }
        }

        // Wave indices match dedicate step order: stable by prog id ascending.
        foreach (var towerDef in _towerDefs.Values)
            towerDef.Progs.Sort((a, b) => a.Id.CompareTo(b.Id));

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_prog_spawn_targets";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefProgId = reader.GetUInt32("tower_def_prog_id");
                    if (!_towerDefProgs.TryGetValue(towerDefProgId, out var towerDefProg))
                        continue;

                    var template = new TowerDefProgSpawnTarget
                    {
                        Id = reader.GetUInt32("id"),
                        SpawnTargetId = reader.GetUInt32("spawn_target_id"),
                        SpawnTargetType = reader.GetString("spawn_target_type"),
                        DespawnOnNextStep = reader.GetBoolean("despawn_on_next_step", true),
                        TowerDefProg = towerDefProg
                    };

                    towerDefProg.SpawnTargets.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_prog_kill_targets";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefProgId = reader.GetUInt32("tower_def_prog_id");
                    if (!_towerDefProgs.TryGetValue(towerDefProgId, out var towerDefProg))
                        continue;

                    var template = new TowerDefProgKillTarget
                    {
                        Id = reader.GetUInt32("id"),
                        KillTargetId = reader.GetUInt32("kill_target_id"),
                        KillTargetType = reader.GetString("kill_target_type"),
                        KillCount = reader.GetUInt32("kill_count"),
                        TowerDefProg = towerDefProg
                    };

                    towerDefProg.KillTargets.Add(template);
                }
            }
        }

        RebuildEventIndexes(connection);
    }

    private void RebuildEventIndexes(SqliteConnection connection)
    {
        var spawners = new HashSet<uint>();
        // Direct Npc spawn targets only (rare) — not kill quotas.
        var priorityNpcs = new HashSet<uint>();

        foreach (var towerDef in _towerDefs.Values)
        {
            if (towerDef.TargetNpcSpawnId != 0)
                spawners.Add(towerDef.TargetNpcSpawnId);

            if (towerDef.Progs == null)
                continue;

            foreach (var prog in towerDef.Progs)
            {
                if (prog.SpawnTargets == null)
                    continue;
                foreach (var spawn in prog.SpawnTargets)
                {
                    if (spawn.SpawnTargetId == 0)
                        continue;
                    if (string.Equals(spawn.SpawnTargetType, "NpcSpawner", StringComparison.Ordinal))
                        spawners.Add(spawn.SpawnTargetId);
                    else if (string.Equals(spawn.SpawnTargetType, "Npc", StringComparison.Ordinal))
                        priorityNpcs.Add(spawn.SpawnTargetId);
                }
            }
        }

        _eventSpawnerTemplateIds = spawners;

        // Spawner membership → portal/stage seeds (and group packs used as wave spawners, e.g. Grimghast).
        // Kill-quota templates alone are never added here.
        var groupIds = new HashSet<uint>();
        var spawnerMembers = new Dictionary<uint, HashSet<uint>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT npc_spawner_id, member_id, member_type FROM npc_spawner_npcs";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var spawnerId = reader.GetUInt32("npc_spawner_id");
                if (!spawners.Contains(spawnerId))
                    continue;

                var memberId = reader.GetUInt32("member_id");
                var memberType = reader.GetString("member_type", "");
                if (string.Equals(memberType, "Npc", StringComparison.OrdinalIgnoreCase))
                {
                    priorityNpcs.Add(memberId);
                    if (!spawnerMembers.TryGetValue(spawnerId, out var set))
                    {
                        set = [];
                        spawnerMembers[spawnerId] = set;
                    }

                    set.Add(memberId);
                }
                else if (string.Equals(memberType, "NpcGroup", StringComparison.OrdinalIgnoreCase))
                    groupIds.Add(memberId);
            }
        }

        _spawnerMemberNpcs = spawnerMembers;

        var groupMembers = new Dictionary<uint, HashSet<uint>>();
        if (groupIds.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT npc_group_id, npc_id FROM npc_group_members";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var groupId = reader.GetUInt32("npc_group_id");
                if (!groupIds.Contains(groupId))
                    continue;
                var npcId = reader.GetUInt32("npc_id");
                if (npcId == 0)
                    continue;
                priorityNpcs.Add(npcId);
                if (!groupMembers.TryGetValue(groupId, out var set))
                {
                    set = [];
                    groupMembers[groupId] = set;
                }

                set.Add(npcId);
            }
        }

        // Also load full npc_group_members for kill-quota cleanup (not limited to wave spawner groups).
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT npc_group_id, npc_id FROM npc_group_members";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var groupId = reader.GetUInt32("npc_group_id");
                var npcId = reader.GetUInt32("npc_id");
                if (npcId == 0)
                    continue;
                if (!groupMembers.TryGetValue(groupId, out var set))
                {
                    set = [];
                    groupMembers[groupId] = set;
                }

                set.Add(npcId);
            }
        }

        _npcGroupMembers = groupMembers;
        _priorityNpcTemplateIds = priorityNpcs;
        ApplyScheduleMetadata();
        IsLoaded = true;
    }

    /// <summary>
    /// Fill optional UTC StartTimes from config, then assign ScheduleMode from weekday slots plus
    /// <c>TowerDefs.GameTimeAutoArmIds</c>. Unknown or stale overlay ids are logged; classification
    /// never uses display names.
    /// </summary>
    private void ApplyScheduleMetadata()
    {
        var cfg = Models.AppConfiguration.Instance.TowerDefs;
        var log = NLog.LogManager.GetCurrentClassLogger();

        var wallOverlay = cfg?.WallClockStartTimesById ??
                          new Dictionary<uint, Dictionary<string, string>>();
        var wallResult = TowerDefScheduleMetadata.ApplyWallClockStartTimes(_towerDefs.Values, wallOverlay);
        if (wallResult.AppliedSlots > 0)
        {
            log.Info(
                "TowerDefs.WallClockStartTimesById applied {0} weekday slot(s)",
                wallResult.AppliedSlots);
        }

        foreach (var id in wallResult.UnknownIds)
            log.Error("TowerDefs.WallClockStartTimesById references unknown tower_defs.id={0}", id);
        foreach (var entry in wallResult.InvalidEntries)
            log.Error("TowerDefs.WallClockStartTimesById invalid entry {0}", entry);
        foreach (var entry in wallResult.Conflicts)
            log.Error("TowerDefs.WallClockStartTimesById conflict (existing slot kept): {0}", entry);

        var ids = cfg?.GameTimeAutoArmIds ?? [];
        var result = TowerDefScheduleMetadata.Apply(_towerDefs.Values, ids);

        if (ids.Count == 0)
        {
            log.Error(
                "TowerDefs.GameTimeAutoArmIds is empty — Event Center Game Time auto-arm will not run. " +
                "Add tower_defs.id values under Configurations/TowerDefs.json");
        }

        foreach (var id in result.UnknownIds)
            log.Error("TowerDefs.GameTimeAutoArmIds references unknown tower_defs.id={0}", id);

        foreach (var id in result.WallClockConflicts)
            log.Warn("TowerDefs.GameTimeAutoArmIds id={0} also has weekday StartTimes — keeping WallClock", id);

        foreach (var id in result.UnlistedToDCandidates)
        {
            log.Warn(
                "tower_defs.id={0} has tod_day_interval and a seed spawner but is not in GameTimeAutoArmIds — staying Manual",
                id);
        }

        foreach (var id in result.IneligibleIds)
        {
            log.Error(
                "TowerDefs.GameTimeAutoArmIds id={0} is missing tod_day_interval or target_npc_spawner_id — staying Manual",
                id);
        }

        var followOverlay = cfg?.FollowOnTowerDefById ?? new Dictionary<uint, uint>();
        var follow = TowerDefScheduleMetadata.ApplyFollowOn(_towerDefs.Values, followOverlay);
        if (follow.Applied > 0)
        {
            log.Info(
                "TowerDefs.FollowOnTowerDefById applied {0} link(s)",
                follow.Applied);
        }

        foreach (var id in follow.UnknownSourceIds)
            log.Error("TowerDefs.FollowOnTowerDefById unknown source tower_defs.id={0}", id);
        foreach (var id in follow.UnknownTargetIds)
            log.Error("TowerDefs.FollowOnTowerDefById unknown target tower_defs.id={0}", id);
        foreach (var id in follow.SelfRefs)
            log.Error("TowerDefs.FollowOnTowerDefById self-reference tower_defs.id={0}", id);
    }

    public void PostLoad()
    {
    }

    public TowerDef GetTowerDef(uint id) => _towerDefs.GetValueOrDefault(id);

    public IReadOnlyCollection<TowerDef> GetAllTowerDefs() => _towerDefs.Values;

    /// <summary>
    /// True when this spawner template is a TowerDef portal or progressive wave arm — schedule
    /// day-night gating must not suppress ZW that dedic announced because of TowerDef Start/Wave.
    /// </summary>
    public bool IsTowerDefEventSpawner(uint spawnerTemplateId) =>
        IsLoaded && spawnerTemplateId != 0 && _eventSpawnerTemplateIds.Contains(spawnerTemplateId);

    /// <summary>
    /// True for tower portal/stage (and wave-spawner group) members that must keep priority stream
    /// paint. Kill-quota infantry are excluded on purpose. Returns false until <see cref="Load"/> completes.
    /// </summary>
    public bool IsTowerDefEventNpc(uint npcTemplateId) =>
        IsLoaded && npcTemplateId != 0 && _priorityNpcTemplateIds.Contains(npcTemplateId);

    /// <summary>
    /// Direct <c>Npc</c> members of a tower portal/wave spawner template (e.g. 9846 → 8828).
    /// </summary>
    public IReadOnlyCollection<uint> GetSpawnerMemberNpcIds(uint spawnerTemplateId)
    {
        if (spawnerTemplateId == 0 ||
            !_spawnerMemberNpcs.TryGetValue(spawnerTemplateId, out var set))
            return Array.Empty<uint>();
        return set;
    }

    /// <summary>True when this NPC template is a seed unit for the given tower portal spawner.</summary>
    public bool IsPortalSeedNpc(uint towerTargetSpawnerId, uint npcTemplateId)
    {
        if (towerTargetSpawnerId == 0 || npcTemplateId == 0)
            return false;
        return _spawnerMemberNpcs.TryGetValue(towerTargetSpawnerId, out var set) && set.Contains(npcTemplateId);
    }

    /// <summary>
    /// All Npc templates that belong to an event for cleanup on End: portal/stage seeds, wave
    /// spawner members, and kill-quota targets (incl. Crimson army 8826/8834 from plot).
    /// </summary>
    public IReadOnlyCollection<uint> GetCleanupNpcTemplates(uint towerDefId)
    {
        var towerDef = GetTowerDef(towerDefId);
        if (towerDef == null)
            return Array.Empty<uint>();

        var set = new HashSet<uint>();
        void AddSpawnerMembers(uint spawnerId)
        {
            if (spawnerId == 0)
                return;
            if (_spawnerMemberNpcs.TryGetValue(spawnerId, out var members))
            {
                foreach (var id in members)
                    set.Add(id);
            }
        }

        void AddGroupMembers(uint groupId)
        {
            if (groupId == 0)
                return;
            if (_npcGroupMembers.TryGetValue(groupId, out var members))
            {
                foreach (var id in members)
                    set.Add(id);
            }
        }

        AddSpawnerMembers(towerDef.TargetNpcSpawnId);
        if (towerDef.KillNpcId != 0)
            set.Add(towerDef.KillNpcId);

        if (towerDef.Progs != null)
        {
            foreach (var prog in towerDef.Progs)
            {
                if (prog.SpawnTargets != null)
                {
                    foreach (var spawn in prog.SpawnTargets)
                    {
                        if (spawn.SpawnTargetId == 0)
                            continue;
                        if (string.Equals(spawn.SpawnTargetType, "NpcSpawner", StringComparison.Ordinal))
                            AddSpawnerMembers(spawn.SpawnTargetId);
                        else if (string.Equals(spawn.SpawnTargetType, "Npc", StringComparison.Ordinal))
                            set.Add(spawn.SpawnTargetId);
                        else if (string.Equals(spawn.SpawnTargetType, "NpcGroup", StringComparison.Ordinal))
                            AddGroupMembers(spawn.SpawnTargetId);
                    }
                }

                if (prog.KillTargets == null)
                    continue;
                foreach (var kill in prog.KillTargets)
                {
                    if (kill.KillTargetId == 0)
                        continue;
                    if (string.Equals(kill.KillTargetType, "Npc", StringComparison.Ordinal))
                        set.Add(kill.KillTargetId);
                    else if (string.Equals(kill.KillTargetType, "NpcGroup", StringComparison.Ordinal))
                        AddGroupMembers(kill.KillTargetId);
                }
            }
        }

        return set;
    }

    public int EventSpawnerCount => _eventSpawnerTemplateIds.Count;
    public int EventNpcCount => _priorityNpcTemplateIds.Count;

    /// <summary>
    /// The events that carry a wall-clock start slot on at least one weekday — the timed world
    /// events (world bosses, ghost ship, dragon invasions) as opposed to the rows driven purely by
    /// kill counts or quest triggers. Evaluated on World UTC.
    /// </summary>
    public IEnumerable<TowerDef> GetScheduledTowerDefs()
    {
        if (!IsLoaded || _towerDefs == null)
            yield break;

        foreach (var towerDef in _towerDefs.Values)
        {
            if (towerDef?.IsScheduled == true)
                yield return towerDef;
        }
    }

    /// <summary>
    /// Event Center "Game Time" rifts (Crimson / Grimghast / Oblivion / Clockwork): <c>tod</c>-driven,
    /// no wall-clock hours. Fired when the zone-simulated hour crosses each row's <c>tod</c>.
    /// </summary>
    public IEnumerable<TowerDef> GetGameTimeScheduledTowerDefs()
    {
        if (!IsLoaded || _towerDefs == null)
            yield break;

        foreach (var towerDef in _towerDefs.Values)
        {
            if (towerDef?.IsGameTimeScheduled == true)
                yield return towerDef;
        }
    }
}
