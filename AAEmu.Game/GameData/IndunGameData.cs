using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Indun.Actions;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
// ReSharper disable once ClassNeverInstantiated.Global
public class IndunGameData : Singleton<IndunGameData>, IGameDataLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, IndunAction> _indunActions;
    private Dictionary<uint, List<IndunEvent>> _indunEvents;
    private Dictionary<uint, IndunZone> _indunZones;
    private Dictionary<uint, IndunRoom> _indunRooms;
    private Dictionary<uint, List<IndunRound>> _indunRounds;
    private Dictionary<uint, HashSet<byte>> _difficultiesByZoneGroup;
    private Dictionary<uint, List<InstanceReward>> _instanceRewards;
    private Dictionary<uint, List<InstanceRewardBonusCount>> _instanceRewardBonusCounts;
    private Dictionary<uint, InstanceRewardMailText> _instanceRewardMailTexts;
    private HashSet<uint> _instanceRewardIds;
    private HashSet<uint> _instanceRewardBonusCountIds;
    private HashSet<(uint InstanceRewardId, uint BuffId)> _instanceRewardBonusCountKeys;
    private HashSet<uint> _instanceRewardMailTextIds;
    private HashSet<uint> _instanceIdsWithDifficultyInfo;
    private Dictionary<uint, InstanceRewardKindDefinition> _instanceRewardKinds;
    private Dictionary<uint, InstanceRewardMailKindDefinition> _instanceRewardMailKinds;
    private Dictionary<uint, List<InstanceFaction>> _instanceFactions;
    private Dictionary<uint, List<InstanceMiniScoreboard>> _instanceMiniScoreboards;
    private Dictionary<uint, List<InstanceGainRule>> _instanceGainRules;
    private List<InstancePointDoodadPhaseChange> _instancePointDoodadPhaseChanges;
    private Dictionary<uint, SortedSet<byte>> _difficultiesByInstance;
    private HashSet<uint> _instanceRewardKindsWithDeliveryTrigger;
    private HashSet<uint> _instanceIdsWithDisplayRankingSurface;
    private HashSet<uint> _instanceFactionIds;

    public IndunZone GetDungeonZone(uint zoneGroupId)
    {
        if (_indunZones != null && _indunZones.TryGetValue(zoneGroupId, out var zone))
            return zone;
        return null;
    }

    /// <summary>Resolve by <c>instances.id</c> (CS AddInstanceVisitCount type dword).</summary>
    public IndunZone GetDungeonZoneByCatalogId(uint instanceCatalogId)
    {
        if (_indunZones == null || instanceCatalogId == 0)
            return null;
        foreach (var zone in _indunZones.Values)
        {
            if (zone.InstanceCatalogId == instanceCatalogId)
                return zone;
        }

        return null;
    }

    public IReadOnlyCollection<IndunZone> GetAllDungeonZones() =>
        _indunZones?.Values ?? (IReadOnlyCollection<IndunZone>)[];

    public List<IndunEvent> GetIndunEvents(uint zoneGroupId)
    {
        if (_indunEvents != null && _indunEvents.TryGetValue(zoneGroupId, out var value))
            return value;
        return [];
    }

    public IndunAction GetIndunActionById(uint indunActionId)
    {
        if (_indunActions != null && _indunActions.TryGetValue(indunActionId, out var value))
            return value;
        return null;
    }

    public IndunRoom GetRoom(uint roomId)
    {
        if (_indunRooms != null && _indunRooms.TryGetValue(roomId, out var value))
            return value;
        return null;
    }

    /// <summary><c>indun_rounds</c> rows of a zone group ordered by round; empty for the 47 zone groups without any.</summary>
    public IReadOnlyList<IndunRound> GetRounds(uint zoneGroupId)
    {
        if (_indunRounds != null && _indunRounds.TryGetValue(zoneGroupId, out var rounds))
            return rounds;
        return [];
    }

    /// <summary>
    /// Returns every reward row whose authored range contains <paramref name="selectionValue"/>.
    /// A reward kind can intentionally publish overlapping rows; they are all part of one mail.
    /// </summary>
    public IReadOnlyList<InstanceReward> GetInstanceRewards(uint instanceId, uint instanceRewardKindId, int selectionValue)
    {
        if (_instanceRewards == null || !_instanceRewards.TryGetValue(instanceId, out var rewards))
            throw new InvalidDataException($"instance_rewards has no rows for instance {instanceId}");

        return IndunRewardSelectionRules.Select(rewards, instanceRewardKindId, selectionValue);
    }

    /// <summary>
    /// Bonus rows attached to one authored reward. Only rows that joined both
    /// <c>instance_rewards</c> and <c>buffs</c> are returned; orphan rows are reported by
    /// <see cref="InstanceRewardBonusDiagnostics"/> and never guessed onto another reward.
    /// </summary>
    public IReadOnlyList<InstanceRewardBonusCount> GetInstanceRewardBonusCounts(uint instanceRewardId)
    {
        if (instanceRewardId == 0)
            throw new InvalidDataException("instance_reward_bonus_counts has no zero reward id");
        if (_instanceRewardIds != null && !_instanceRewardIds.Contains(instanceRewardId))
            throw new InvalidDataException($"instance_rewards has no reward {instanceRewardId}");
        if (_instanceRewardBonusCounts != null &&
            _instanceRewardBonusCounts.TryGetValue(instanceRewardId, out var bonusCounts))
            return bonusCounts;
        return [];
    }

    /// <summary>Rows rejected during content load because their reward id is not shipped.</summary>
    public InstanceRewardBonusDiagnostics InstanceRewardBonusDiagnostics { get; private set; } =
        new([], []);

    /// <summary>The mail copy is keyed by the instance catalog id, not the transient world id.</summary>
    public InstanceRewardMailText GetInstanceRewardMailText(uint instanceId)
    {
        if (_instanceRewardMailTexts != null && _instanceRewardMailTexts.TryGetValue(instanceId, out var text))
            return text;

        throw new InvalidDataException($"instance_reward_mail_texts has no row for instance {instanceId}");
    }

    public bool HasInstanceRewardKind(uint instanceId, uint instanceRewardKindId) =>
        _instanceRewards != null && _instanceRewards.TryGetValue(instanceId, out var rewards) &&
        rewards.Any(reward => reward.InstanceRewardKindId == instanceRewardKindId);

    public string GetInstanceRewardKindName(uint instanceRewardKindId)
    {
        if (_instanceRewardKinds != null && _instanceRewardKinds.TryGetValue(instanceRewardKindId, out var kind))
            return kind.Name;
        throw new InvalidDataException($"enum_instance_reward_kinds has no kind {instanceRewardKindId}");
    }

    public bool HasInstanceRewardMailText(uint instanceId) =>
        _instanceRewardMailTexts != null && _instanceRewardMailTexts.ContainsKey(instanceId);

    #region W03C selection taxonomy

    /// <summary>
    /// Classifies how this instance/kind pair's selection value can be established, and names the
    /// exact artifact whose absence blocks it. The classification is structural, so it never depends on
    /// a reward-kind name or a literal kind id.
    /// </summary>
    public InstanceRewardSelectionVerdict ClassifyInstanceRewardSelection(
        uint instanceId,
        uint instanceRewardKindId,
        byte? runtimeDifficulty)
    {
        var kindName = GetInstanceRewardKindName(instanceRewardKindId);
        IReadOnlyList<InstanceReward> rewards = [];
        if (_instanceRewards != null && _instanceRewards.TryGetValue(instanceId, out var instanceRewards))
            rewards = instanceRewards.Where(reward => reward.InstanceRewardKindId == instanceRewardKindId).ToArray();

        IReadOnlyCollection<byte> difficulties =
            _difficultiesByInstance != null && _difficultiesByInstance.TryGetValue(instanceId, out var loaded)
                ? loaded
                : [];

        var roundCount = 0;
        var zone = GetDungeonZoneByCatalogId(instanceId);
        if (zone != null)
            roundCount = GetRounds(zone.ZoneGroupId).Count;

        IReadOnlyCollection<int> teamSizes = _instanceFactions != null &&
                                             _instanceFactions.TryGetValue(instanceId, out var factions)
            ? factions.Where(faction => faction.MinPlayer == faction.MaxPlayer)
                      .Select(faction => faction.MaxPlayer)
                      .Distinct()
                      .ToArray()
            : [];

        var hasDisplaySurface = _instanceIdsWithDisplayRankingSurface != null &&
                                _instanceIdsWithDisplayRankingSurface.Contains(instanceId);
        var hasTrigger = _instanceRewardKindsWithDeliveryTrigger != null &&
                         _instanceRewardKindsWithDeliveryTrigger.Contains(instanceRewardKindId);

        return InstanceRewardTaxonomyRules.Classify(
            instanceId, instanceRewardKindId, kindName, rewards, difficulties, roundCount,
            teamSizes, hasDisplaySurface, hasTrigger, runtimeDifficulty);
    }

    /// <summary>Fixed-size <c>instance_factions</c> teams of an instance; empty when none are shipped.</summary>
    public IReadOnlyList<InstanceFaction> GetInstanceFactions(uint instanceId)
    {
        if (_instanceFactions != null && _instanceFactions.TryGetValue(instanceId, out var factions))
            return factions;
        return [];
    }

    /// <summary>
    /// The instance's mini scoreboard rows. Display and grouping only: these never carry a score value
    /// and must not be read as one.
    /// </summary>
    public IReadOnlyList<InstanceMiniScoreboard> GetInstanceMiniScoreboards(uint instanceId)
    {
        if (_instanceMiniScoreboards != null && _instanceMiniScoreboards.TryGetValue(instanceId, out var boards))
            return boards;
        return [];
    }

    /// <summary>The instance's gain rules. Display and grouping only, for the same reason.</summary>
    public IReadOnlyList<InstanceGainRule> GetInstanceGainRules(uint instanceId)
    {
        if (_instanceGainRules != null && _instanceGainRules.TryGetValue(instanceId, out var rules))
            return rules;
        return [];
    }

    /// <summary>Every shipped <c>instance_point_doodad_phase_changes</c> row.</summary>
    public IReadOnlyList<InstancePointDoodadPhaseChange> GetInstancePointDoodadPhaseChanges() =>
        _instancePointDoodadPhaseChanges ?? (IReadOnlyList<InstancePointDoodadPhaseChange>)[];

    /// <summary>The <c>instance_difficult_infos</c> difficulties of one instance, in ascending order.</summary>
    public IReadOnlyList<byte> GetInstanceDifficulties(uint instanceId)
    {
        if (_difficultiesByInstance != null && _difficultiesByInstance.TryGetValue(instanceId, out var values))
            return values.ToArray();
        return [];
    }

    /// <summary>Reward kinds that at least one <c>indun_action_send_mail_rewards</c> row references.</summary>
    public IReadOnlyCollection<uint> GetInstanceRewardKindsWithDeliveryTrigger() =>
        _instanceRewardKindsWithDeliveryTrigger ?? (IReadOnlyCollection<uint>)[];

    #endregion

    public bool IsDifficultyAvailable(uint zoneGroupId, byte difficult) =>
        _difficultiesByZoneGroup != null &&
        _difficultiesByZoneGroup.TryGetValue(zoneGroupId, out var difficulties) &&
        difficulties.Contains(difficult);

    public bool HasDifficultyOptions(uint zoneGroupId) =>
        _difficultiesByZoneGroup != null && _difficultiesByZoneGroup.ContainsKey(zoneGroupId);

    /// <summary>
    /// True when the shipped <c>instance_difficult_infos</c> table supplies the selection source
    /// for a dungeon copy. It is deliberately separate from zone-group difficulty options.
    /// </summary>
    public bool HasInstanceDifficultyInfo(uint instanceId) =>
        _instanceIdsWithDifficultyInfo != null && _instanceIdsWithDifficultyInfo.Contains(instanceId);

    /// <summary>
    /// Resolves only a difficulty value that is both evidence-backed by the instance table and
    /// present in the authored reward range for this kind. A non-difficulty reward kind cannot use
    /// this path.
    /// </summary>
    public bool TryGetAuthoredDifficultySelection(
        uint instanceId,
        uint instanceRewardKindId,
        byte? difficulty,
        out int selectionValue)
    {
        selectionValue = 0;
        if (_instanceRewards == null || !_instanceRewards.TryGetValue(instanceId, out var rewards))
            return false;

        return IndunRewardSelectionRules.TryResolveAuthoredDifficultySelection(
            rewards, HasInstanceDifficultyInfo(instanceId), difficulty, instanceRewardKindId, out selectionValue);
    }

    /// <summary>Appends to a per-key catalog list, creating the bucket on first use.</summary>
    private static void AddTo<TKey, TValue>(Dictionary<TKey, List<TValue>> source, TKey key, TValue value)
    {
        if (!source.TryGetValue(key, out var bucket))
        {
            bucket = [];
            source.Add(key, bucket);
        }

        bucket.Add(value);
    }

    private void AddIndunEvent(IndunEvent indunEvent)
    {
        if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
            _indunEvents.Add(indunEvent.ZoneGroupId, []);

        _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
    }

    public IndunEvent GetIndunEventById(uint eventId)
    {
        if (_indunEvents == null) { return null; }

        foreach (var evList in _indunEvents.Values)
        {
            foreach (var ev in evList)
            {
                if (ev.Id == eventId)
                    return ev;
            }
        }

        return null;
    }

    public void Load(SqliteConnection connection)
    {
        _indunActions = [];
        _indunEvents = [];
        _indunZones = [];
        _indunRooms = [];
        _indunRounds = [];
        _difficultiesByZoneGroup = [];
        _instanceRewards = [];
        _instanceRewardBonusCounts = [];
        _instanceRewardMailTexts = [];
        _instanceRewardIds = [];
        _instanceRewardBonusCountIds = [];
        _instanceRewardBonusCountKeys = [];
        _instanceRewardMailTextIds = [];
        _instanceIdsWithDifficultyInfo = [];
        _instanceRewardKinds = [];
        _instanceRewardMailKinds = [];
        _instanceFactions = [];
        _instanceMiniScoreboards = [];
        _instanceGainRules = [];
        _instancePointDoodadPhaseChanges = [];
        _difficultiesByInstance = [];
        _instanceRewardKindsWithDeliveryTrigger = [];
        _instanceIdsWithDisplayRankingSurface = [];
        _instanceFactionIds = [];

        #region Reward catalogs
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM enum_instance_reward_kinds";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                var name = reader.GetString("name");
                if (id == 0 || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("enum_instance_reward_kinds has an invalid row");
                if (!_instanceRewardKinds.TryAdd(id, new InstanceRewardKindDefinition(id, name)))
                    throw new InvalidDataException($"enum_instance_reward_kinds has duplicate id {id}");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM enum_instance_reward_mail_kinds";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                var name = reader.GetString("name");
                if (id == 0 || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("enum_instance_reward_mail_kinds has an invalid row");
                if (!_instanceRewardMailKinds.TryAdd(id, new InstanceRewardMailKindDefinition(id, name)))
                    throw new InvalidDataException($"enum_instance_reward_mail_kinds has duplicate id {id}");
            }
        }
        #endregion

        #region Actions
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, doodad_almighty_id, doodad_func_group_id FROM indun_actions
                                        LEFT JOIN indun_action_change_doodad_phases 
                                        ON indun_actions.detail_id = indun_action_change_doodad_phases.id
                                        WHERE indun_actions.detail_type = 'IndunActionChangeDoodadPhase'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionChangeDoodadPhases
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id"),
                        DoodadFuncGroupId = reader.GetUInt32("doodad_func_group_id")
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, tag_id FROM indun_actions
                                        LEFT JOIN indun_action_remove_tagged_npcs 
                                        ON indun_actions.detail_id = indun_action_remove_tagged_npcs.id
                                        WHERE indun_actions.detail_type = 'IndunActionRemoveTaggedNpc'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionRemoveTaggedNpcs
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        TagId = reader.GetUInt32("tag_id")
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, indun_room_id FROM indun_actions
                                        LEFT JOIN indun_action_set_room_cleareds 
                                        ON indun_actions.detail_id = indun_action_set_room_cleareds.id
                                        WHERE indun_actions.detail_type = 'IndunActionSetRoomCleared'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionSetRoomCleareds
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        IndunRoomId = reader.GetUInt32("indun_room_id")
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.* FROM indun_actions
                                        WHERE indun_actions.detail_type = 'NpcSpawnerSpawnEffect'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionNpcSpawner
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0)
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        // The three kinds below (18 rows) complete the 357-row table; kinds and columns from
        // enum_indun_round_alarm_kinds, indun_action_next_rounds, indun_action_send_mail_rewards.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, round_alarm_kind_id, show_ui FROM indun_actions
                                        LEFT JOIN indun_action_round_alarms
                                        ON indun_actions.detail_id = indun_action_round_alarms.id
                                        WHERE indun_actions.detail_type = 'IndunActionRoundAlarm'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionRoundAlarm
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        RoundAlarmKindId = (byte)reader.GetUInt32("round_alarm_kind_id", 0),
                        ShowUi = reader.GetBoolean("show_ui", true)
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, round_add FROM indun_actions
                                        LEFT JOIN indun_action_next_rounds
                                        ON indun_actions.detail_id = indun_action_next_rounds.id
                                        WHERE indun_actions.detail_type = 'IndunActionNextRound'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var action = new IndunActionNextRound
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        RoundAdd = reader.GetInt32("round_add", 0)
                    };

                    _indunActions.Add(action.Id, action);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_actions.*, instance_reward_kind_id,
                                           enum_instance_reward_kinds.name AS instance_reward_kind_name
                                    FROM indun_actions
                                        LEFT JOIN indun_action_send_mail_rewards
                                        ON indun_actions.detail_id = indun_action_send_mail_rewards.id
                                        LEFT JOIN enum_instance_reward_kinds
                                        ON instance_reward_kind_id = enum_instance_reward_kinds.id
                                        WHERE indun_actions.detail_type = 'IndunActionSendMailReward'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var kindId = reader.GetUInt32("instance_reward_kind_id", 0);
                    if (kindId == 0 || reader.IsDBNull("instance_reward_kind_name") ||
                        !_instanceRewardKinds.TryGetValue(kindId, out var kindDefinition) ||
                        !string.Equals(reader.GetString("instance_reward_kind_name"), kindDefinition.Name, StringComparison.Ordinal))
                        throw new InvalidDataException("IndunActionSendMailReward has no enum_instance_reward_kinds join");
                    var action = new IndunActionSendMailReward
                    {
                        Id = reader.GetUInt32("id"),
                        DetailId = reader.GetUInt32("detail_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        NextActionId = reader.GetUInt32("next_action_id", 0),
                        InstanceRewardKindId = kindId
                    };

                    _instanceRewardKindsWithDeliveryTrigger.Add(kindId);
                    _indunActions.Add(action.Id, action);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT instances.target_id AS zone_group_id, instance_difficult_infos.difficult
                                    FROM instance_difficult_infos
                                    JOIN instances ON instances.id = instance_difficult_infos.instance_id
                                    WHERE instances.target_type = 'IndunZone'";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var zoneGroupId = reader.GetUInt32("zone_group_id");
                var difficult = (byte)reader.GetUInt32("difficult");
                if (!_difficultiesByZoneGroup.TryGetValue(zoneGroupId, out var difficulties))
                {
                    difficulties = [];
                    _difficultiesByZoneGroup.Add(zoneGroupId, difficulties);
                }
                difficulties.Add(difficult);
            }
        }
        #endregion
        #region Events
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, doodad_almighty_id, doodad_func_group_id FROM indun_events
                                        LEFT JOIN indun_event_doodad_spawneds
                                        ON indun_events.condition_id = indun_event_doodad_spawneds.id
                                        WHERE indun_events.condition_type = 'IndunEventDoodadSpawned'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventDoodadSpawneds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id"),
                        DoodadFuncGroupId = reader.GetUInt32("doodad_func_group_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, room_id FROM indun_events
                                        LEFT JOIN indun_event_no_alive_ch_in_rooms
                                        ON indun_events.condition_id = indun_event_no_alive_ch_in_rooms.id
                                        WHERE indun_events.condition_type = 'IndunEventNoAliveChInRoom'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventNoAliveChInRooms
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        RoomId = reader.GetUInt32("room_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, npc_id FROM indun_events
                                        LEFT JOIN indun_event_npc_combat_endeds
                                        ON indun_events.condition_id = indun_event_npc_combat_endeds.id
                                        WHERE indun_events.condition_type = 'IndunEventNpcCombatEnded'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventNpcCombatEndeds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        NpcId = reader.GetUInt32("npc_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, npc_id FROM indun_events
                                        LEFT JOIN indun_event_npc_combat_starteds
                                        ON indun_events.condition_id = indun_event_npc_combat_starteds.id
                                        WHERE indun_events.condition_type = 'IndunEventNpcCombatStarted'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventNpcCombatStarteds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        NpcId = reader.GetUInt32("npc_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, npc_id FROM indun_events
                                        LEFT JOIN indun_event_npc_killeds
                                        ON indun_events.condition_id = indun_event_npc_killeds.id
                                        WHERE indun_events.condition_type = 'IndunEventNpcKilled'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventNpcKilleds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        NpcId = reader.GetUInt32("npc_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, npc_id FROM indun_events
                                        LEFT JOIN indun_event_npc_spawneds
                                        ON indun_events.condition_id = indun_event_npc_spawneds.id
                                        WHERE indun_events.condition_type = 'IndunEventNpcSpawned'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var indunEvent = new IndunEventNpcSpawneds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        NpcId = reader.GetUInt32("npc_id")
                    };

                    if (!_indunEvents.ContainsKey(indunEvent.ZoneGroupId))
                        _indunEvents.Add(indunEvent.ZoneGroupId, []);

                    _indunEvents[indunEvent.ZoneGroupId].Add(indunEvent);
                }
            }
        }
        // The five kinds below (41 rows) complete the 263-row table.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, doodad_almighty_id, doodad_func_group_id, check_status_id FROM indun_events
                                        LEFT JOIN indun_event_doodad_phase_changeds
                                        ON indun_events.condition_id = indun_event_doodad_phase_changeds.id
                                        WHERE indun_events.condition_type = 'IndunEventDoodadPhaseChanged'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    AddIndunEvent(new IndunEventDoodadPhaseChangeds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id", 0),
                        DoodadFuncGroupId = reader.GetUInt32("doodad_func_group_id", 0),
                        CheckStatusId = reader.GetUInt32("check_status_id", 0)
                    });
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, tag_id FROM indun_events
                                        LEFT JOIN indun_event_no_in_aggro_lists
                                        ON indun_events.condition_id = indun_event_no_in_aggro_lists.id
                                        WHERE indun_events.condition_type = 'IndunEventNoInAggroList'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    AddIndunEvent(new IndunEventNoInAggroLists
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        TagId = reader.GetUInt32("tag_id", 0)
                    });
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, npc_id, buff_id, npc_info_broadcasting_id FROM indun_events
                                        LEFT JOIN indun_event_npc_info_broadcastings
                                        ON indun_events.condition_id = indun_event_npc_info_broadcastings.id
                                        WHERE indun_events.condition_type = 'IndunEventNpcInfoBroadcasting'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    AddIndunEvent(new IndunEventNpcInfoBroadcastings
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        NpcId = reader.GetUInt32("npc_id", 0),
                        BuffId = reader.GetUInt32("buff_id", 0),
                        NpcInfoBroadcastingId = (byte)reader.GetUInt32("npc_info_broadcasting_id", 0)
                    });
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, source_faction_id, zone_score_kind_id, level, change_way FROM indun_events
                                        LEFT JOIN indun_event_zone_score_level_changeds
                                        ON indun_events.condition_id = indun_event_zone_score_level_changeds.id
                                        WHERE indun_events.condition_type = 'IndunEventZoneScoreLevelChanged'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    AddIndunEvent(new IndunEventZoneScoreLevelChangeds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        SourceFactionId = reader.GetUInt32("source_faction_id", 0),
                        ZoneScoreKindId = reader.GetUInt32("zone_score_kind_id", 0),
                        Level = reader.GetInt32("level", 0),
                        ChangeWay = reader.GetInt32("change_way", 0)
                    });
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_events.*, min_difficult, max_difficult FROM indun_events
                                        LEFT JOIN indun_event_difficult_changeds
                                        ON indun_events.condition_id = indun_event_difficult_changeds.id
                                        WHERE indun_events.condition_type = 'IndunEventDifficultChanged'";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    AddIndunEvent(new IndunEventDifficultChangeds
                    {
                        Id = reader.GetUInt32("id"),
                        ConditionId = reader.GetUInt32("condition_id"),
                        ZoneGroupId = reader.GetUInt16("zone_group_id"),
                        StartActionId = reader.GetUInt32("start_action_id", 0),
                        MinDifficult = reader.GetInt32("min_difficult", 0),
                        MaxDifficult = reader.GetInt32("max_difficult", 0)
                    });
                }
            }
        }
        #endregion
        #region Zones
        using (var command = connection.CreateCommand())
        {

            command.CommandText = "SELECT * FROM indun_zones";

            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {

                    var indunZone = new IndunZone
                    {
                        ZoneGroupId = reader.GetUInt32("zone_group_id"),
                        // EnterCount = reader.GetUInt32("enter_count"),
                        // 10.0.2.13: name, comment, item_id removed from indun_zones
                        LevelMin = reader.GetUInt32("level_min"),
                        LevelMax = reader.GetUInt32("level_max"),
                        GearScore = reader.GetUInt32("gear_score"),
                        MaxPlayers = reader.GetUInt32("max_players"),
                        PvP = reader.GetBoolean("pvp", true),
                        HasGraveyard = reader.GetBoolean("has_graveyard", true),
                        RestoreItemTime = reader.GetUInt32("restore_item_time"),
                        PartyOnly = reader.GetBoolean("party_only", true),
                        ClientDriven = reader.GetBoolean("client_driven", true),
                        SelectChannel = reader.GetBoolean("select_channel", true)
                    };

                    indunZone.LocalizedName = LocalizationManager.Instance.Get("indun_zones", "name", indunZone.ZoneGroupId, string.Empty);
                    // EnterCount filled from instances below (or legacy fallback if no row).
                    indunZone.EnterCount = IndunEntryRules.ResolveEnterCount(
                        instancesEnterCount: null,
                        indunZone.SelectChannel,
                        indunZone.ZoneGroupId);

                    _indunZones.Add(indunZone.ZoneGroupId, indunZone);

                }
            }
        }

        // 10.0.2.13: daily enter caps live on instances (target_type=IndunZone, target_id=zone_group_id).
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, target_id, enter_count, reset_item_id, reset_limit, reset_item_increase_scale, permit_enter_count_item_id, " +
                "direct_matching, matching_invitation_type_id, min_matching_time, apply_waiting_time, matching_cleanup_term, matching_intergration_level_id, " +
                "permission_white_list_bit, use_utc " +
                "FROM instances WHERE target_type = 'IndunZone'";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var zoneGroupId = reader.GetUInt32("target_id");
                if (_indunZones == null || !_indunZones.TryGetValue(zoneGroupId, out var zone))
                    continue;

                zone.InstanceCatalogId = reader.GetUInt32("id");
                zone.EnterCount = reader.GetUInt32("enter_count");
                zone.ResetItemId = reader.GetUInt32("reset_item_id", 0);
                zone.ResetLimit = (int)reader.GetUInt32("reset_limit", 0);
                zone.ResetItemIncreaseScale = (int)reader.GetUInt32("reset_item_increase_scale", 1);
                if (zone.ResetItemIncreaseScale <= 0)
                    zone.ResetItemIncreaseScale = 1;
                zone.PermitEnterCountItemId = reader.GetUInt32("permit_enter_count_item_id", 0);
                zone.DirectMatching = reader.GetBoolean("direct_matching");
                zone.MatchingInvitationTypeId = (byte)reader.GetUInt32("matching_invitation_type_id", 0);
                zone.MinMatchingTimeMs = reader.GetUInt32("min_matching_time", 0);
                zone.ApplyWaitingTimeMs = reader.GetUInt32("apply_waiting_time", 0);
                zone.MatchingCleanupTermMs = reader.GetUInt32("matching_cleanup_term", 0);
                zone.MatchingIntegrationLevelId = (byte)reader.GetUInt32("matching_intergration_level_id", 0);
                zone.PermissionWhiteListBit = reader.GetUInt32("permission_white_list_bit", 0);
                zone.UseUtcEntranceTimes = reader.GetBoolean("use_utc");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT iet.instance_id, iet.day_of_week, iet.start_hour, iet.start_minute,
                                           iet.end_hour, iet.end_minute
                                    FROM instance_entrance_times iet
                                    INNER JOIN instances i ON i.id = iet.instance_id
                                    WHERE i.target_type = 'IndunZone'
                                    ORDER BY iet.instance_id, iet.day_of_week, iet.start_hour, iet.start_minute";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var zone = GetDungeonZoneByCatalogId(reader.GetUInt32("instance_id"));
                zone?.EntranceTimes.Add(new InstanceEntranceTime(
                    (int)reader.GetUInt32("day_of_week"),
                    (int)reader.GetUInt32("start_hour"),
                    (int)reader.GetUInt32("start_minute"),
                    (int)reader.GetUInt32("end_hour"),
                    (int)reader.GetUInt32("end_minute")));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT ipt.instance_id, ipt.kind_id, ipt.tag_id
                                    FROM instance_permission_tags ipt
                                    INNER JOIN instances i ON i.id = ipt.instance_id
                                    WHERE i.target_type = 'IndunZone'
                                    ORDER BY ipt.instance_id, ipt.kind_id, ipt.id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var kindId = reader.GetUInt32("kind_id");
                if (kindId != (uint)InstancePermissionTagKind.Buff)
                {
                    var instanceId = reader.GetUInt32("instance_id");
                    throw new InvalidDataException(
                        $"IndunZone instance {instanceId} uses unsupported permission tag kind {kindId}; refusing to load an unenforced admission rule.");
                }

                var zone = GetDungeonZoneByCatalogId(reader.GetUInt32("instance_id"));
                zone?.PermissionTags.Add(new InstancePermissionTag(
                    (InstancePermissionTagKind)kindId,
                    reader.GetUInt32("tag_id")));
            }
        }
        #endregion
        #region Rooms
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT indun_rooms.*, center_doodad_id, radius FROM indun_rooms
                                        LEFT JOIN indun_room_spheres ON indun_rooms.shape_id = indun_room_spheres.id";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var room = new IndunRoom
                    {
                        Id = reader.GetUInt32("id"),
                        DoodadId = reader.GetUInt32("center_doodad_id"),
                        Radius = reader.GetUInt32("radius"),
                        ZoneGroupId = reader.GetUInt32("zone_group_id")
                    };
                    _indunRooms.Add(room.Id, room);
                }
            }
        }
        #endregion
        #region Rounds
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM indun_rounds ORDER BY zone_group_id, round";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var round = new IndunRound
                    {
                        Id = reader.GetUInt32("id"),
                        ZoneGroupId = reader.GetUInt32("zone_group_id"),
                        Round = reader.GetInt32("round", 0),
                        SpawnerId = reader.GetUInt32("spawner_id", 0),
                        TimerSeconds = reader.GetInt32("timer", 0),
                        BossRound = reader.GetBoolean("boss_round", true)
                    };

                    if (!_indunRounds.TryGetValue(round.ZoneGroupId, out var rounds))
                    {
                        rounds = [];
                        _indunRounds.Add(round.ZoneGroupId, rounds);
                    }

                    rounds.Add(round);
                }
            }
        }
        #endregion

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT DISTINCT idi.instance_id
                                    FROM instance_difficult_infos idi
                                    JOIN instances i ON i.id = idi.instance_id
                                    WHERE i.target_type = 'IndunZone'";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                _instanceIdsWithDifficultyInfo.Add(reader.GetUInt32("instance_id"));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT idi.instance_id, idi.difficult
                                    FROM instance_difficult_infos idi
                                    JOIN instances i ON i.id = idi.instance_id
                                    WHERE i.target_type = 'IndunZone'
                                    ORDER BY idi.instance_id, idi.difficult";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var instanceId = reader.GetUInt32("instance_id");
                if (instanceId == 0)
                    throw new InvalidDataException("instance_difficult_infos has a zero instance id");
                var difficult = (byte)reader.GetUInt32("difficult");
                if (!_difficultiesByInstance.TryGetValue(instanceId, out var difficulties))
                {
                    difficulties = [];
                    _difficultiesByInstance.Add(instanceId, difficulties);
                }

                if (!difficulties.Add(difficult))
                    throw new InvalidDataException(
                        $"instance_difficult_infos has a duplicate difficulty {difficult} for instance {instanceId}");
            }
        }

        #region Instance rewards
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT r.id, r.instance_id, r.instance_reward_kind_id, r.start_range, r.end_range,
                                           r.reward_amount, r.use_game_score, r.reward_target_id, r.reward_target_type,
                                           r.give_ignore_visited_count, r.apply_config,
                                           rk.name AS instance_reward_kind_name
                                    FROM instance_rewards r
                                    LEFT JOIN enum_instance_reward_kinds rk ON rk.id = r.instance_reward_kind_id
                                    ORDER BY r.instance_id, r.instance_reward_kind_id, r.id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var rewardId = reader.GetUInt32("id");
                var instanceId = reader.GetUInt32("instance_id");
                var rewardKindId = reader.GetUInt32("instance_reward_kind_id");
                var targetId = reader.GetUInt32("reward_target_id");
                if (rewardId == 0 || instanceId == 0 || rewardKindId == 0 || targetId == 0)
                    throw new InvalidDataException($"instance_rewards {rewardId} has a zero catalog id");
                if (reader.IsDBNull("instance_reward_kind_name") ||
                    !_instanceRewardKinds.TryGetValue(rewardKindId, out var rewardKindDefinition) ||
                    !string.Equals(reader.GetString("instance_reward_kind_name"), rewardKindDefinition.Name, StringComparison.Ordinal))
                    throw new InvalidDataException($"instance_rewards {rewardId} has no enum_instance_reward_kinds join");
                if (!_instanceRewardIds.Add(rewardId))
                    throw new InvalidDataException($"instance_rewards has duplicate id {rewardId}");

                var targetTypeText = reader.GetString("reward_target_type");
                if (!Enum.TryParse<InstanceRewardTargetType>(targetTypeText, true, out var targetType))
                {
                    throw new InvalidDataException(
                        $"instance_rewards {rewardId} has unsupported reward_target_type '{targetTypeText}'");
                }

                var startRange = reader.GetInt32("start_range");
                var endRange = reader.GetInt32("end_range");
                if (startRange > endRange)
                    throw new InvalidDataException($"instance_rewards {rewardId} has an inverted range");

                var rewardAmount = reader.GetInt32("reward_amount");
                if (rewardAmount < 0)
                    throw new InvalidDataException($"instance_rewards {rewardId} has a negative reward_amount");

                var reward = new InstanceReward(
                    rewardId,
                    instanceId,
                    rewardKindId,
                    startRange,
                    endRange,
                    rewardAmount,
                    reader.GetBoolean("use_game_score"),
                    targetId,
                    targetType,
                    reader.GetBoolean("give_ignore_visited_count"),
                    reader.GetBoolean("apply_config"));

                if (!_instanceRewards.TryGetValue(reward.InstanceId, out var instanceRewards))
                {
                    instanceRewards = [];
                    _instanceRewards.Add(reward.InstanceId, instanceRewards);
                }
                instanceRewards.Add(reward);
            }
        }

        var orphanBonusRowIds = new List<uint>();
        var orphanBonusRewardIds = new SortedSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT bc.id, bc.instance_reward_id, bc.buff_id, bc.count,
                                           r.id AS reward_catalog_id, b.name AS buff_name
                                    FROM instance_reward_bonus_counts bc
                                    LEFT JOIN instance_rewards r ON r.id = bc.instance_reward_id
                                    LEFT JOIN buffs b ON b.id = bc.buff_id
                                    ORDER BY bc.instance_reward_id, bc.buff_id, bc.id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var bonusId = reader.GetUInt32("id");
                var rewardId = reader.GetUInt32("instance_reward_id");
                var buffId = reader.GetUInt32("buff_id");
                var count = reader.GetInt32("count");
                if (bonusId == 0 || rewardId == 0 || buffId == 0)
                    throw new InvalidDataException($"instance_reward_bonus_counts {bonusId} has a zero catalog id");
                if (count <= 0)
                    throw new InvalidDataException($"instance_reward_bonus_counts {bonusId} has a non-positive count");
                if (!_instanceRewardBonusCountIds.Add(bonusId))
                    throw new InvalidDataException($"instance_reward_bonus_counts has duplicate id {bonusId}");
                if (reader.IsDBNull("buff_name") || string.IsNullOrWhiteSpace(reader.GetString("buff_name")))
                    throw new InvalidDataException($"instance_reward_bonus_counts {bonusId} has no buffs row for buff {buffId}");

                if (reader.IsDBNull("reward_catalog_id"))
                {
                    orphanBonusRowIds.Add(bonusId);
                    orphanBonusRewardIds.Add(rewardId);
                    continue;
                }

                if (!_instanceRewardBonusCountKeys.Add((rewardId, buffId)))
                    throw new InvalidDataException(
                        $"instance_reward_bonus_counts has duplicate reward {rewardId} / buff {buffId}");

                if (!_instanceRewardBonusCounts.TryGetValue(rewardId, out var bonusCounts))
                {
                    bonusCounts = [];
                    _instanceRewardBonusCounts.Add(rewardId, bonusCounts);
                }
                bonusCounts.Add(new InstanceRewardBonusCount(bonusId, rewardId, buffId, count));
            }
        }

        orphanBonusRowIds.Sort();
        InstanceRewardBonusDiagnostics = new(orphanBonusRewardIds.ToArray(), orphanBonusRowIds.ToArray());
        if (orphanBonusRowIds.Count > 0)
        {
            Logger.Error(
                "instance_reward_bonus_counts rejected {0} orphan row(s) for absent reward id(s) {1}; row id(s): {2}",
                orphanBonusRowIds.Count,
                string.Join(", ", orphanBonusRewardIds),
                string.Join(", ", orphanBonusRowIds));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT m.id, m.instance_id, m.mail_sender, m.mail_title, m.mail_body, m.mail_kind_id,
                                           k.name AS mail_kind_name
                                    FROM instance_reward_mail_texts m
                                    LEFT JOIN enum_instance_reward_mail_kinds k ON k.id = m.mail_kind_id
                                    ORDER BY m.instance_id, m.id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var textId = reader.GetUInt32("id");
                var instanceId = reader.GetUInt32("instance_id");
                if (textId == 0 || instanceId == 0)
                    throw new InvalidDataException($"instance_reward_mail_texts {textId} has a zero catalog id");
                if (!_instanceRewardMailTextIds.Add(textId))
                    throw new InvalidDataException($"instance_reward_mail_texts has duplicate id {textId}");
                if (_instanceRewardMailTexts.ContainsKey(instanceId))
                    throw new InvalidDataException($"instance_reward_mail_texts has duplicate rows for instance {instanceId}");

                var sender = reader.GetString("mail_sender");
                var title = reader.GetString("mail_title");
                var body = reader.GetString("mail_body");
                var mailKindId = reader.GetUInt32("mail_kind_id");
                if (mailKindId == 0 || string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
                    throw new InvalidDataException($"instance_reward_mail_texts {textId} has incomplete mail text");
                if (reader.IsDBNull("mail_kind_name") ||
                    !_instanceRewardMailKinds.TryGetValue(mailKindId, out var mailKindDefinition) ||
                    !string.Equals(reader.GetString("mail_kind_name"), mailKindDefinition.Name, StringComparison.Ordinal) ||
                    !Enum.TryParse<InstanceRewardMailKind>(mailKindDefinition.Name, true, out var mailKind))
                {
                    throw new InvalidDataException(
                        $"instance_reward_mail_texts {textId} has no supported enum_instance_reward_mail_kinds join");
                }

                _instanceRewardMailTexts.Add(instanceId, new InstanceRewardMailText(
                    textId,
                    instanceId,
                    sender,
                    title,
                    body,
                    mailKindId,
                    mailKind));
            }
        }

        // Some legacy indun_actions rows use this action class but have no instance_rewards
        // catalogue entry. Their joins are checked at execution by the delivery service, so
        // loading the complete action catalog does not turn those legacy rows into a boot failure.
        #endregion

        #region W03C selection-taxonomy catalogs (display and grouping only)
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT id, instance_id, instance_faction_preset_id, min_player, max_player,
                                           exclude_when_dev_minimal_recruitment, spawn_point_index
                                    FROM instance_factions ORDER BY instance_id, id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var factionId = reader.GetUInt32("id");
                var instanceId = reader.GetUInt32("instance_id");
                var presetId = reader.GetUInt32("instance_faction_preset_id");
                var minPlayer = reader.GetInt32("min_player");
                var maxPlayer = reader.GetInt32("max_player");
                if (factionId == 0 || instanceId == 0 || presetId == 0)
                    throw new InvalidDataException($"instance_factions {factionId} has a zero catalog id");
                if (minPlayer < 0 || maxPlayer < minPlayer)
                    throw new InvalidDataException(
                        $"instance_factions {factionId} has an inverted team size {minPlayer}..{maxPlayer}");
                if (!_instanceFactionIds.Add(factionId))
                    throw new InvalidDataException($"instance_factions has duplicate id {factionId}");

                AddTo(_instanceFactions, instanceId,
                    new InstanceFaction(factionId, instanceId, presetId, minPlayer, maxPlayer,
                        reader.GetBoolean("exclude_when_dev_minimal_recruitment"),
                        reader.GetUInt32("spawn_point_index", 0)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT id, instance_id, target_id, target_type, name, visible_order,
                                           icon_id, merge_target_id
                                    FROM instance_mini_scoreboards ORDER BY instance_id, id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var boardId = reader.GetUInt32("id");
                var instanceId = reader.GetUInt32("instance_id");
                var targetId = reader.GetUInt32("target_id");
                if (boardId == 0 || instanceId == 0 || targetId == 0)
                    throw new InvalidDataException($"instance_mini_scoreboards {boardId} has a zero catalog id");

                var targetTypeText = reader.GetString("target_type");
                if (!Enum.TryParse<InstanceMiniScoreboardTargetType>(targetTypeText, true, out var targetType))
                {
                    throw new InvalidDataException(
                        $"instance_mini_scoreboards {boardId} has unsupported target_type '{targetTypeText}'");
                }

                // icon_id is a catalog key string, never parsed as a number.
                var name = reader.GetString("name");
                var iconId = reader.GetString("icon_id");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(iconId))
                    throw new InvalidDataException($"instance_mini_scoreboards {boardId} has an empty name or icon key");

                _instanceIdsWithDisplayRankingSurface.Add(instanceId);
                AddTo(_instanceMiniScoreboards, instanceId,
                    new InstanceMiniScoreboard(boardId, instanceId, targetId, targetType, name,
                        reader.GetInt32("visible_order"), iconId, reader.GetUInt32("merge_target_id", 0)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT id, instance_id, range_id, instance_faction_id, target_id,
                                           target_type, display_range_id
                                    FROM instance_gain_rules ORDER BY instance_id, id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var ruleId = reader.GetUInt32("id");
                var instanceId = reader.GetUInt32("instance_id");
                var factionId = reader.GetUInt32("instance_faction_id");
                var targetId = reader.GetUInt32("target_id");
                if (ruleId == 0 || instanceId == 0 || factionId == 0 || targetId == 0)
                    throw new InvalidDataException($"instance_gain_rules {ruleId} has a zero catalog id");
                if (!_instanceFactionIds.Contains(factionId))                {
                    throw new InvalidDataException(
                        $"instance_gain_rules {ruleId} points at instance_factions {factionId}, which is not shipped");
                }

                var targetTypeText = reader.GetString("target_type");
                if (!Enum.TryParse<InstanceGainRuleTargetType>(targetTypeText, true, out var targetType))
                {
                    throw new InvalidDataException(
                        $"instance_gain_rules {ruleId} has unsupported target_type '{targetTypeText}'");
                }

                _instanceIdsWithDisplayRankingSurface.Add(instanceId);
                AddTo(_instanceGainRules, instanceId,
                    new InstanceGainRule(ruleId, instanceId, reader.GetUInt32("range_id", 0), factionId,
                        targetId, targetType, reader.GetUInt32("display_range_id", 0)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT id, doodad_almighty_id, doodad_func_group_id
                                    FROM instance_point_doodad_phase_changes ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var changeId = reader.GetUInt32("id");
                var almightyId = reader.GetUInt32("doodad_almighty_id");
                var funcGroupId = reader.GetUInt32("doodad_func_group_id");
                if (changeId == 0 || almightyId == 0 || funcGroupId == 0)
                    throw new InvalidDataException(
                        $"instance_point_doodad_phase_changes {changeId} has a zero catalog id");

                _instancePointDoodadPhaseChanges.Add(
                    new InstancePointDoodadPhaseChange(changeId, almightyId, funcGroupId));
            }
        }
        #endregion

        var eventCount = 0;
        foreach (var events in _indunEvents.Values)
            eventCount += events.Count;
        var roundCount = 0;
        foreach (var rounds in _indunRounds.Values)
            roundCount += rounds.Count;
        var instanceRewardCount = _instanceRewards.Values.Sum(rewards => rewards.Count);
        var instanceRewardBonusCount = _instanceRewardBonusCounts.Values.Sum(bonusCounts => bonusCounts.Count);
        var instanceFactionCount = _instanceFactions.Values.Sum(factions => factions.Count);
        var miniScoreboardCount = _instanceMiniScoreboards.Values.Sum(boards => boards.Count);
        var gainRuleCount = _instanceGainRules.Values.Sum(rules => rules.Count);
        Logger.Info($"Loaded {_indunActions.Count} indun actions, {eventCount} indun events, {roundCount} indun rounds, {instanceRewardCount} instance rewards, {instanceRewardBonusCount} instance reward bonus counts, {_instanceRewardMailTexts.Count} instance reward mail texts, {instanceFactionCount} instance factions, {miniScoreboardCount} mini scoreboards, {gainRuleCount} instance gain rules, {_instancePointDoodadPhaseChanges.Count} instance point doodad phase changes, {_instanceRewardKindsWithDeliveryTrigger.Count} reward kind(s) with an authored delivery trigger");
    }

    public void PostLoad()
    {
    }
}
