using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Indun.Actions;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class IndunGameData : Singleton<IndunGameData>, IGameDataLoader
{
    private Dictionary<uint, IndunAction> _indunActions;
    private Dictionary<uint, List<IndunEvent>> _indunEvents;
    private Dictionary<uint, IndunZone> _indunZones;
    private Dictionary<uint, IndunRoom> _indunRooms;

    public IndunZone GetDungeonZone(uint id)
    {
        if (_indunZones != null && _indunZones.TryGetValue(id, out var zone))
            return zone;
        return null;
    }

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
                    var action = new IndunActionChangeDoodadPhases();
                    action.Id = reader.GetUInt32("id");
                    action.DetailId = reader.GetUInt32("detail_id");
                    action.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    action.NextActionId = reader.GetUInt32("next_action_id", 0);
                    action.DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id");
                    action.DoodadFuncGroupId = reader.GetUInt32("doodad_func_group_id");

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
                    var action = new IndunActionRemoveTaggedNpcs();
                    action.Id = reader.GetUInt32("id");
                    action.DetailId = reader.GetUInt32("detail_id");
                    action.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    action.NextActionId = reader.GetUInt32("next_action_id", 0);
                    action.TagId = reader.GetUInt32("tag_id");

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
                    var action = new IndunActionSetRoomCleareds();
                    action.Id = reader.GetUInt32("id");
                    action.DetailId = reader.GetUInt32("detail_id");
                    action.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    action.NextActionId = reader.GetUInt32("next_action_id", 0);
                    action.IndunRoomId = reader.GetUInt32("indun_room_id");

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
                    var action = new IndunActionNpcSpawner();
                    action.Id = reader.GetUInt32("id");
                    action.DetailId = reader.GetUInt32("detail_id");
                    action.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    action.NextActionId = reader.GetUInt32("next_action_id", 0);

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
                    var indunEvent = new IndunEventDoodadSpawneds();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id");
                    indunEvent.DoodadFuncGroupId = reader.GetUInt32("doodad_func_group_id");

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
                    var indunEvent = new IndunEventNoAliveChInRooms();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.RoomId = reader.GetUInt32("room_id");

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
                    var indunEvent = new IndunEventNpcCombatEndeds();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.NpcId = reader.GetUInt32("npc_id");

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
                    var indunEvent = new IndunEventNpcCombatStarteds();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.NpcId = reader.GetUInt32("npc_id");

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
                    var indunEvent = new IndunEventNpcKilleds();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.NpcId = reader.GetUInt32("npc_id");

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
                    var indunEvent = new IndunEventNpcSpawneds();
                    indunEvent.Id = reader.GetUInt32("id");
                    indunEvent.ConditionId = reader.GetUInt32("condition_id");
                    indunEvent.ZoneGroupId = reader.GetUInt16("zone_group_id");
                    indunEvent.StartActionId = reader.GetUInt32("start_action_id");
                    indunEvent.NpcId = reader.GetUInt32("npc_id");

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

                    var indunZone = new IndunZone();
                    indunZone.ZoneGroupId = reader.GetUInt32("zone_group_id");
                    indunZone.LevelMin = reader.GetUInt32("level_min");
                    indunZone.LevelMax = reader.GetUInt32("level_max");
                    indunZone.MaxPlayers = reader.GetUInt32("max_players");
                    indunZone.PlayerCombat = reader.GetBoolean("pvp", true);
                    indunZone.HasGraveyard = reader.GetBoolean("has_graveyard", true);
                    indunZone.ItemRequired = reader.GetUInt32("item_id", 0);
                    indunZone.ItemCooldown = reader.GetUInt32("restore_item_time");
                    indunZone.PartyRequired = reader.GetBoolean("party_only", true);
                    indunZone.ClientDriven = reader.GetBoolean("client_driven", true);
                    indunZone.SelectChannel = reader.GetBoolean("select_channel", true);

                    _indunZones.Add(indunZone.ZoneGroupId, indunZone);

                }
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
                    var room = new IndunRoom();
                    room.Id = reader.GetUInt32("id");
                    room.DoodadId = reader.GetUInt32("center_doodad_id");
                    room.Radius = reader.GetUInt32("radius");
                    room.ZoneGroupId = reader.GetUInt32("zone_group_id");
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
