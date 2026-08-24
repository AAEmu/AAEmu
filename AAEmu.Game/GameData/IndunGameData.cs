using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Indun.Actions;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
// ReSharper disable once ClassNeverInstantiated.Global
public class IndunGameData : Singleton<IndunGameData>, IGameDataLoader
{
    private Dictionary<uint, IndunAction> _indunActions;
    private Dictionary<uint, List<IndunEvent>> _indunEvents;
    private Dictionary<uint, IndunZone> _indunZones;
    private Dictionary<uint, IndunRoom> _indunRooms;

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
                "direct_matching, matching_invitation_type_id, min_matching_time, apply_waiting_time, matching_cleanup_term, matching_intergration_level_id " +
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
    }

    public void PostLoad()
    {
    }
}
