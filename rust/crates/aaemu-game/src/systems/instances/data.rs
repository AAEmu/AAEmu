//! Game data of the instances subsystem:
//!
//! * `GameData/IndunGameData.cs` (`indun_actions`, `indun_events`, `indun_zones`, `indun_rooms`
//!   + their detail tables) and `Models/Game/Indun/{IndunZone, IndunRoom}`;
//! * `GameData/BattlefieldGameData.cs` (`battle_fields`, `game_rule_sets`,
//!   `Data/battlefields.json`) and `Models/Game/InstantGame/{Battlefield, BattlefieldSpawns,
//!   GameRuleSet}`;
//! * `GameData/TowerDefGameData.cs` (`tower_defs`, `tower_def_progs`, `tower_def_prog_*`) and
//!   `Models/Game/TowerDefs/*`;
//! * the zone part of `ZoneManager` the dungeons need in the C# dictionary order
//!   (`GetZoneKeysInZoneGroupById` enumerates `_zones` in `zones` row order).

use std::collections::HashMap;

use rusqlite::Connection;
use serde_json::Value;

use crate::data::sqlite::for_each_row;
use crate::data::GameDataError;
use crate::systems::npcs::json;
use crate::systems::DataLoadCtx;

// ---------------------------------------------------------------------------------------------
// Indun
// ---------------------------------------------------------------------------------------------

/// `IndunZone`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IndunZone {
    pub zone_group_id: u32,
    pub name: String,
    pub comment: String,
    pub level_min: u32,
    pub level_max: u32,
    pub max_players: u32,
    /// `PvP` (false for the Mirage Island and the Library).
    pub pvp: bool,
    pub has_graveyard: bool,
    /// Item template consumed to enter (0 = none).
    pub item_id: u32,
    pub restore_item_time: u32,
    pub party_only: bool,
    pub client_driven: bool,
    pub select_channel: bool,
    /// `EnterCount` (the "hack for earlier versions": 1000 for zone group 49 and channel
    /// dungeons, 3 otherwise).
    pub enter_count: u32,
    /// `LocalizedName`.
    pub localized_name: String,
}

impl Default for IndunZone {
    fn default() -> Self {
        Self {
            zone_group_id: 0,
            name: String::new(),
            comment: String::new(),
            level_min: 1,
            level_max: 100,
            max_players: 9999,
            pvp: true,
            has_graveyard: true,
            item_id: 0,
            restore_item_time: 0,
            party_only: false,
            client_driven: false,
            select_channel: false,
            enter_count: 1000,
            localized_name: String::new(),
        }
    }
}

impl std::fmt::Display for IndunZone {
    /// `IndunZone.ToString()`.
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        if self.localized_name.trim().is_empty() {
            f.write_str(&self.name)
        } else {
            f.write_str(&self.localized_name)
        }
    }
}

/// `IndunRoom` (`indun_rooms` + `indun_room_spheres`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct IndunRoom {
    pub id: u32,
    /// `center_doodad_id` (a doodad template id).
    pub doodad_id: u32,
    pub radius: u32,
    pub zone_group_id: u32,
}

/// The `IndunAction` subclasses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IndunActionKind {
    /// `IndunActionChangeDoodadPhases`.
    ChangeDoodadPhases {
        doodad_almighty_id: u32,
        doodad_func_group_id: u32,
    },
    /// `IndunActionRemoveTaggedNpcs`.
    RemoveTaggedNpcs { tag_id: u32 },
    /// `IndunActionSetRoomCleareds`.
    SetRoomCleareds { indun_room_id: u32 },
    /// `IndunActionNpcSpawner` (`NpcSpawnerSpawnEffect`; logs only in C#).
    NpcSpawner,
}

/// `IndunAction`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct IndunAction {
    pub id: u32,
    pub detail_id: u32,
    pub zone_group_id: u32,
    pub next_action_id: u32,
    pub kind: IndunActionKind,
}

/// The `IndunEvent` subclasses.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IndunEventKind {
    /// `IndunEventDoodadSpawneds` (`WorldEvents.OnDoodadSpawn`).
    DoodadSpawneds {
        doodad_almighty_id: u32,
        doodad_func_group_id: u32,
    },
    /// `IndunEventNoAliveChInRooms` (room clear check of `Dungeon.AreaClearTick`).
    NoAliveChInRooms { room_id: u32 },
    /// `IndunEventNpcCombatEndeds` (`WorldEvents.OnUnitCombatEnd`).
    NpcCombatEndeds { npc_id: u32 },
    /// `IndunEventNpcCombatStarteds` (`WorldEvents.OnUnitCombatStart`, never raised in C#).
    NpcCombatStarteds { npc_id: u32 },
    /// `IndunEventNpcKilleds` (`WorldEvents.OnUnitKilled`).
    NpcKilleds { npc_id: u32 },
    /// `IndunEventNpcSpawneds` (`WorldEvents.OnUnitSpawn`).
    NpcSpawneds { npc_id: u32 },
}

/// `IndunEvent`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct IndunEvent {
    pub id: u32,
    pub condition_id: u32,
    pub zone_group_id: u32,
    pub start_action_id: u32,
    pub kind: IndunEventKind,
}

/// `IndunGameData`.
#[derive(Debug, Clone, Default)]
pub struct IndunData {
    pub actions: HashMap<u32, IndunAction>,
    /// Events by zone group id, in load order (the six `SELECT`s in C# order).
    pub events: HashMap<u32, Vec<IndunEvent>>,
    pub zones: HashMap<u32, IndunZone>,
    pub rooms: HashMap<u32, IndunRoom>,
}

fn dup(what: &str, id: u32) -> GameDataError {
    GameDataError::File(format!("IndunGameData: duplicate {what} {id}"))
}

impl IndunData {
    /// `GetDungeonZone(zoneGroupId)`.
    pub fn dungeon_zone(&self, zone_group_id: u32) -> Option<&IndunZone> {
        self.zones.get(&zone_group_id)
    }

    /// `GetIndunEvents(zoneGroupId)`.
    pub fn indun_events(&self, zone_group_id: u32) -> &[IndunEvent] {
        self.events.get(&zone_group_id).map_or(&[], Vec::as_slice)
    }

    /// `GetIndunActionById(id)`.
    pub fn action(&self, id: u32) -> Option<&IndunAction> {
        self.actions.get(&id)
    }

    /// `GetRoom(roomId)`.
    pub fn room(&self, id: u32) -> Option<&IndunRoom> {
        self.rooms.get(&id)
    }

    /// `GetIndunEventById(eventId)`.
    pub fn event_by_id(&self, id: u32) -> Option<&IndunEvent> {
        self.events.values().flatten().find(|e| e.id == id)
    }

    /// `IndunGameData.Load`.
    pub fn load(conn: &Connection, language: &str) -> Result<Self, GameDataError> {
        let mut d = IndunData::default();
        let add_action = |d: &mut IndunData, a: IndunAction| -> Result<(), GameDataError> {
            if d.actions.insert(a.id, a).is_some() {
                return Err(dup("indun action", a.id));
            }
            Ok(())
        };

        // #region Actions
        let mut rows = Vec::new();
        for_each_row(
            conn,
            "SELECT indun_actions.*, doodad_almighty_id, doodad_func_group_id FROM indun_actions \
             LEFT JOIN indun_action_change_doodad_phases \
             ON indun_actions.detail_id = indun_action_change_doodad_phases.id \
             WHERE indun_actions.detail_type = 'IndunActionChangeDoodadPhase'",
            [],
            |r| {
                rows.push(IndunAction {
                    id: r.u32("id")?,
                    detail_id: r.u32("detail_id")?,
                    zone_group_id: u32::from(r.i64("zone_group_id")? as u16),
                    next_action_id: r.u32_or("next_action_id", 0)?,
                    kind: IndunActionKind::ChangeDoodadPhases {
                        doodad_almighty_id: r.u32("doodad_almighty_id")?,
                        doodad_func_group_id: r.u32("doodad_func_group_id")?,
                    },
                });
                Ok(())
            },
        )?;
        for_each_row(
            conn,
            "SELECT indun_actions.*, tag_id FROM indun_actions \
             LEFT JOIN indun_action_remove_tagged_npcs \
             ON indun_actions.detail_id = indun_action_remove_tagged_npcs.id \
             WHERE indun_actions.detail_type = 'IndunActionRemoveTaggedNpc'",
            [],
            |r| {
                rows.push(IndunAction {
                    id: r.u32("id")?,
                    detail_id: r.u32("detail_id")?,
                    zone_group_id: u32::from(r.i64("zone_group_id")? as u16),
                    next_action_id: r.u32_or("next_action_id", 0)?,
                    kind: IndunActionKind::RemoveTaggedNpcs {
                        tag_id: r.u32("tag_id")?,
                    },
                });
                Ok(())
            },
        )?;
        for_each_row(
            conn,
            "SELECT indun_actions.*, indun_room_id FROM indun_actions \
             LEFT JOIN indun_action_set_room_cleareds \
             ON indun_actions.detail_id = indun_action_set_room_cleareds.id \
             WHERE indun_actions.detail_type = 'IndunActionSetRoomCleared'",
            [],
            |r| {
                rows.push(IndunAction {
                    id: r.u32("id")?,
                    detail_id: r.u32("detail_id")?,
                    zone_group_id: u32::from(r.i64("zone_group_id")? as u16),
                    next_action_id: r.u32_or("next_action_id", 0)?,
                    kind: IndunActionKind::SetRoomCleareds {
                        indun_room_id: r.u32("indun_room_id")?,
                    },
                });
                Ok(())
            },
        )?;
        for_each_row(
            conn,
            "SELECT indun_actions.* FROM indun_actions \
             WHERE indun_actions.detail_type = 'NpcSpawnerSpawnEffect'",
            [],
            |r| {
                rows.push(IndunAction {
                    id: r.u32("id")?,
                    detail_id: r.u32("detail_id")?,
                    zone_group_id: u32::from(r.i64("zone_group_id")? as u16),
                    next_action_id: r.u32_or("next_action_id", 0)?,
                    kind: IndunActionKind::NpcSpawner,
                });
                Ok(())
            },
        )?;
        for a in rows {
            add_action(&mut d, a)?;
        }

        // #region Events
        let event_queries: [(&str, &str); 6] = [
            (
                "SELECT indun_events.*, doodad_almighty_id, doodad_func_group_id FROM indun_events \
                 LEFT JOIN indun_event_doodad_spawneds \
                 ON indun_events.condition_id = indun_event_doodad_spawneds.id \
                 WHERE indun_events.condition_type = 'IndunEventDoodadSpawned'",
                "doodad",
            ),
            (
                "SELECT indun_events.*, room_id FROM indun_events \
                 LEFT JOIN indun_event_no_alive_ch_in_rooms \
                 ON indun_events.condition_id = indun_event_no_alive_ch_in_rooms.id \
                 WHERE indun_events.condition_type = 'IndunEventNoAliveChInRoom'",
                "room",
            ),
            (
                "SELECT indun_events.*, npc_id FROM indun_events \
                 LEFT JOIN indun_event_npc_combat_endeds \
                 ON indun_events.condition_id = indun_event_npc_combat_endeds.id \
                 WHERE indun_events.condition_type = 'IndunEventNpcCombatEnded'",
                "combat_ended",
            ),
            (
                "SELECT indun_events.*, npc_id FROM indun_events \
                 LEFT JOIN indun_event_npc_combat_starteds \
                 ON indun_events.condition_id = indun_event_npc_combat_starteds.id \
                 WHERE indun_events.condition_type = 'IndunEventNpcCombatStarted'",
                "combat_started",
            ),
            (
                "SELECT indun_events.*, npc_id FROM indun_events \
                 LEFT JOIN indun_event_npc_killeds \
                 ON indun_events.condition_id = indun_event_npc_killeds.id \
                 WHERE indun_events.condition_type = 'IndunEventNpcKilled'",
                "killed",
            ),
            (
                "SELECT indun_events.*, npc_id FROM indun_events \
                 LEFT JOIN indun_event_npc_spawneds \
                 ON indun_events.condition_id = indun_event_npc_spawneds.id \
                 WHERE indun_events.condition_type = 'IndunEventNpcSpawned'",
                "spawned",
            ),
        ];
        for (sql, what) in event_queries {
            for_each_row(conn, sql, [], |r| {
                let kind = match what {
                    "doodad" => IndunEventKind::DoodadSpawneds {
                        doodad_almighty_id: r.u32("doodad_almighty_id")?,
                        doodad_func_group_id: r.u32("doodad_func_group_id")?,
                    },
                    "room" => IndunEventKind::NoAliveChInRooms {
                        room_id: r.u32("room_id")?,
                    },
                    "combat_ended" => IndunEventKind::NpcCombatEndeds {
                        npc_id: r.u32("npc_id")?,
                    },
                    "combat_started" => IndunEventKind::NpcCombatStarteds {
                        npc_id: r.u32("npc_id")?,
                    },
                    "killed" => IndunEventKind::NpcKilleds {
                        npc_id: r.u32("npc_id")?,
                    },
                    _ => IndunEventKind::NpcSpawneds {
                        npc_id: r.u32("npc_id")?,
                    },
                };
                let e = IndunEvent {
                    id: r.u32("id")?,
                    condition_id: r.u32("condition_id")?,
                    zone_group_id: u32::from(r.i64("zone_group_id")? as u16),
                    start_action_id: r.u32("start_action_id")?,
                    kind,
                };
                d.events.entry(e.zone_group_id).or_default().push(e);
                Ok(())
            })?;
        }

        // #region Zones
        let names = localized(conn, language, "indun_zones", "name")?;
        let mut zones = Vec::new();
        for_each_row(conn, "SELECT * FROM indun_zones", [], |r| {
            let mut z = IndunZone {
                zone_group_id: r.u32("zone_group_id")?,
                name: r.string("name")?,
                comment: r.string("comment")?,
                level_min: r.u32("level_min")?,
                level_max: r.u32("level_max")?,
                max_players: r.u32("max_players")?,
                pvp: r.bool_str("pvp")?,
                has_graveyard: r.bool_str("has_graveyard")?,
                item_id: r.u32_or("item_id", 0)?,
                restore_item_time: r.u32("restore_item_time")?,
                party_only: r.bool_str("party_only")?,
                client_driven: r.bool_str("client_driven")?,
                select_channel: r.bool_str("select_channel")?,
                ..IndunZone::default()
            };
            // Hack for earlier versions: exception for Mirage and Library.
            z.enter_count = if z.zone_group_id == 49 || z.select_channel {
                1000
            } else {
                3
            };
            z.localized_name = match names.get(&i64::from(z.zone_group_id)) {
                Some(t) if !t.is_empty() => t.clone(),
                _ => z.name.clone(),
            };
            zones.push(z);
            Ok(())
        })?;
        for z in zones {
            let id = z.zone_group_id;
            if d.zones.insert(id, z).is_some() {
                return Err(dup("indun zone", id));
            }
        }

        // #region Rooms
        let mut rooms = Vec::new();
        for_each_row(
            conn,
            "SELECT indun_rooms.*, center_doodad_id, radius FROM indun_rooms \
             LEFT JOIN indun_room_spheres ON indun_rooms.shape_id = indun_room_spheres.id",
            [],
            |r| {
                rooms.push(IndunRoom {
                    id: r.u32("id")?,
                    doodad_id: r.u32("center_doodad_id")?,
                    radius: r.u32("radius")?,
                    zone_group_id: r.u32("zone_group_id")?,
                });
                Ok(())
            },
        )?;
        for room in rooms {
            if d.rooms.insert(room.id, room).is_some() {
                return Err(dup("indun room", room.id));
            }
        }
        tracing::info!(
            "Loaded {} indun actions, {} indun events, {} indun zones, {} indun rooms",
            d.actions.len(),
            d.events.values().map(Vec::len).sum::<usize>(),
            d.zones.len(),
            d.rooms.len()
        );
        Ok(d)
    }
}

/// `LocalizationManager.Get(tbl, column, idx, fallback)` rows of one table column.
fn localized(
    conn: &Connection,
    language: &str,
    tbl: &str,
    column: &str,
) -> Result<HashMap<i64, String>, GameDataError> {
    let mut out = HashMap::new();
    let sql = format!(
        "SELECT idx, \"{}\" AS txt FROM localized_texts WHERE tbl_name = ?1 AND tbl_column_name = ?2",
        language.replace('"', "")
    );
    // A missing language column (or table) leaves every name unlocalized.
    let res = for_each_row(conn, &sql, [tbl, column], |r| {
        out.insert(r.i64("idx")?, r.string_or("txt", "")?);
        Ok(())
    });
    if let Err(e) = res {
        tracing::warn!("localized_texts {tbl}.{column}: {e}");
    }
    Ok(out)
}

// ---------------------------------------------------------------------------------------------
// Battlefields (instant games)
// ---------------------------------------------------------------------------------------------

/// `Point` (the members `battlefields.json` uses).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Point {
    pub world_id: u32,
    pub zone_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub rotation_x: i8,
    pub rotation_y: i8,
    pub rotation_z: i8,
}

/// `BattlefieldSpawns`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct BattlefieldSpawns {
    pub battlefield_id: u32,
    /// `None` = the JSON had no (or a `null`) spawn point (a NullReferenceException in C# when
    /// it is used).
    pub corps1_spawn: Option<Point>,
    pub corps2_spawn: Option<Point>,
}

/// `GameRuleSet`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct GameRuleSet {
    pub id: u32,
    pub corps_size: u32,
    pub corps1_faction_id: u32,
    pub corps2_faction_id: u32,
    /// Minutes.
    pub time_opening: i32,
    /// Minutes.
    pub time_ending: i32,
    /// Minutes.
    pub time_playing: i32,
    pub victory_score: i32,
    pub battlefield_id: u32,
    /// `victory_kill_count` (0 = no kill-count victory). Not loaded by the C# (`TODO fill out
    /// the rest of the fields` in `GameRuleSet.cs`); the 1.2 data sets it (battlefield 6: 50)
    /// and `SCInstantGameJoinedPacket` carries it to the client.
    pub victory_kill_count: u32,
    /// `killstreak0_id` ..= `killstreak23_id` (the 24 killstreak skill ids of
    /// `SCInstantGameJoinedPacket`; the C# wrote 24 zeros, so the battlefield action bar
    /// never had skills to show).
    pub killstreak_skills: [u32; 24],
    /// `level_min` (0 = no gate). The entrance window groups a battlefield's rule sets into
    /// level sections by this value; applying below it is refused.
    pub level_min: u32,
    /// `victory_kill_corps1_head`: the NPC template corps 1 must kill to win (the enemy
    /// Polestar; 0 = no such target). The 1.2 rows pair 13353/13352, the blue/red Polestars
    /// (factions 160/159).
    pub victory_kill_corps1_head: u32,
    /// `victory_kill_corps2_head`: the NPC template corps 2 must kill to win.
    pub victory_kill_corps2_head: u32,
    /// End-of-match honor bonuses (`bonus_*` columns), credited per member at the result.
    pub bonus_winner: i32,
    pub bonus_loser: i32,
    pub bonus_no_death: i32,
    pub bonus_top_enemy_pc_kill: i32,
    pub bonus_top_enemy_non_pc_kill: i32,
    /// Arena ranking points per result (`rank_*_point` columns; only the Gladiator rule set
    /// authors non-zero values).
    pub rank_invalid_point: i32,
    pub rank_win_point: i32,
    pub rank_lose_point: i32,
    pub rank_draw_point: i32,
    /// `time_unearned_win` (seconds; 0 on every shipped row but the Gladiator's 60).
    pub time_unearned_win: i32,
}

impl GameRuleSet {
    /// The corps-head NPC template a member of `corps` must kill to win (0 = none).
    pub fn corps_head_target(&self, corps: u8) -> u32 {
        match corps {
            0 => self.victory_kill_corps1_head,
            1 => self.victory_kill_corps2_head,
            _ => 0,
        }
    }
}

/// `Battlefield`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Battlefield {
    pub id: u32,
    pub zone_key: u32,
    /// `Spawns` (`None` = not in `battlefields.json`).
    pub spawns: Option<BattlefieldSpawns>,
    /// `RuleSet` (`None` = no `game_rule_sets` row; the last row of a battlefield wins).
    pub rule_set: Option<GameRuleSet>,
}

/// `BattlefieldGameData`.
#[derive(Debug, Clone, Default)]
pub struct BattlefieldData {
    pub battlefields: HashMap<u32, Battlefield>,
}

/// A `game_rule_sets.rank_*_point` column: `0` when the table has no such column or the value is
/// NULL.
fn rank_point(r: &crate::data::sqlite::SqlRow<'_, '_>, col: &str) -> rusqlite::Result<i32> {
    if r.has(col) {
        r.i32_or(col, 0)
    } else {
        Ok(0)
    }
}

impl BattlefieldData {
    /// `GetBattlefield(id)`.
    pub fn battlefield(&self, id: u32) -> Option<&Battlefield> {
        self.battlefields.get(&id)
    }

    /// `BattlefieldGameData.Load` (`battle_fields`, `game_rule_sets` and
    /// `<app>/Data/battlefields.json`).
    pub fn load(conn: &Connection, battlefields_json: Option<&str>) -> Result<Self, GameDataError> {
        let mut d = BattlefieldData::default();
        let mut rows = Vec::new();
        for_each_row(conn, "SELECT * FROM battle_fields", [], |r| {
            rows.push(Battlefield {
                id: r.u32("id")?,
                zone_key: r.u32("zone_key")?,
                ..Battlefield::default()
            });
            Ok(())
        })?;
        for bf in rows {
            let id = bf.id;
            if d.battlefields.insert(id, bf).is_some() {
                return Err(GameDataError::File(format!(
                    "BattlefieldGameData: duplicate battlefield {id}"
                )));
            }
        }
        for_each_row(conn, "SELECT * FROM game_rule_sets", [], |r| {
            let mut killstreak_skills = [0u32; 24];
            for (i, slot) in killstreak_skills.iter_mut().enumerate() {
                let col = format!("killstreak{i}_id");
                if r.has(&col) {
                    *slot = r.u32_or(&col, 0)?;
                }
            }
            let gsr = GameRuleSet {
                id: r.u32("id")?,
                battlefield_id: r.u32("battle_field_id")?,
                corps_size: r.u32("corps_size")?,
                corps1_faction_id: r.u32("corps1_id")?,
                corps2_faction_id: r.u32("corps2_id")?,
                time_opening: r.i32("time_opening")?,
                time_ending: r.i32("time_ending")?,
                time_playing: r.i32("time_playing")?,
                victory_score: r.i32("victory_score")?,
                victory_kill_count: if r.has("victory_kill_count") {
                    r.u32_or("victory_kill_count", 0)?
                } else {
                    0
                },
                killstreak_skills,
                level_min: if r.has("level_min") {
                    r.u32_or("level_min", 0)?
                } else {
                    0
                },
                victory_kill_corps1_head: if r.has("victory_kill_corps1_head") {
                    r.u32_or("victory_kill_corps1_head", 0)?
                } else {
                    0
                },
                victory_kill_corps2_head: if r.has("victory_kill_corps2_head") {
                    r.u32_or("victory_kill_corps2_head", 0)?
                } else {
                    0
                },
                bonus_winner: if r.has("bonus_winner") {
                    r.i32_or("bonus_winner", 0)?
                } else {
                    0
                },
                bonus_loser: if r.has("bonus_loser") {
                    r.i32_or("bonus_loser", 0)?
                } else {
                    0
                },
                bonus_no_death: if r.has("bonus_no_death") {
                    r.i32_or("bonus_no_death", 0)?
                } else {
                    0
                },
                bonus_top_enemy_pc_kill: if r.has("bonus_top_enemy_pc_kill") {
                    r.i32_or("bonus_top_enemy_pc_kill", 0)?
                } else {
                    0
                },
                bonus_top_enemy_non_pc_kill: if r.has("bonus_top_enemy_non_pc_kill") {
                    r.i32_or("bonus_top_enemy_non_pc_kill", 0)?
                } else {
                    0
                },
                // The ranking columns are optional (hand-built test schemas omit them).
                rank_invalid_point: rank_point(r, "rank_invalid_point")?,
                rank_win_point: rank_point(r, "rank_win_point")?,
                rank_lose_point: rank_point(r, "rank_lose_point")?,
                rank_draw_point: rank_point(r, "rank_draw_point")?,
                time_unearned_win: if r.has("time_unearned_win") {
                    r.i32_or("time_unearned_win", 0)?
                } else {
                    0
                },
            };
            if let Some(bf) = d.battlefields.get_mut(&gsr.battlefield_id) {
                bf.rule_set = Some(gsr);
            }
            Ok(())
        })?;

        let contents = battlefields_json.unwrap_or_default();
        if contents.trim().is_empty() {
            // `throw new IOException($"File {pathFile} doesn't exists or is empty.")`
            return Err(GameDataError::File(
                "File Data/battlefields.json doesn't exists or is empty.".into(),
            ));
        }
        if let Some(spawns) = parse_battlefield_spawns(contents) {
            for s in spawns {
                let Some(bf) = d.battlefields.get_mut(&s.battlefield_id) else {
                    // Deviation from the C# (`return` abandoned every remaining entry, so one
                    // stale id silently left later battlefields without spawns and their
                    // invited players were never teleported): skip only the unknown entry.
                    tracing::warn!(
                        "battlefields.json: no battlefield {} for its spawns entry",
                        s.battlefield_id
                    );
                    continue;
                };
                bf.spawns = Some(s);
            }
        }
        tracing::info!("Loaded {} battlefields", d.battlefields.len());
        Ok(d)
    }
}

fn parse_point(v: &Value) -> Option<Option<Point>> {
    match v {
        Value::Null => Some(None),
        Value::Object(o) => {
            let sbyte = |name| json::i64_or(o, name, 0).ok().map(|v| v as i8);
            Some(Some(Point {
                world_id: json::u32_or(o, "WorldId", 0).ok()?,
                zone_id: json::u32_or(o, "ZoneId", 0).ok()?,
                x: json::f32_or(o, "X", 0.0).ok()?,
                y: json::f32_or(o, "Y", 0.0).ok()?,
                z: json::f32_or(o, "Z", 0.0).ok()?,
                rotation_x: sbyte("RotationX")?,
                rotation_y: sbyte("RotationY")?,
                rotation_z: sbyte("RotationZ")?,
            }))
        }
        _ => None,
    }
}

/// `JsonHelper.TryDeserializeObject<List<BattlefieldSpawns>>` (`None` = not deserializable).
pub fn parse_battlefield_spawns(text: &str) -> Option<Vec<BattlefieldSpawns>> {
    let value = json::parse(text).ok()?;
    let arr = json::array(&value).ok()?;
    let mut out = Vec::with_capacity(arr.len());
    for item in arr {
        let Value::Object(o) = item else {
            return None;
        };
        let point = |name| match json::prop(o, name) {
            None => Some(None),
            Some(v) => parse_point(v),
        };
        out.push(BattlefieldSpawns {
            battlefield_id: json::u32_or(o, "BattlefieldId", 0).ok()?,
            corps1_spawn: point("Corps1Spawn")?,
            corps2_spawn: point("Corps2Spawn")?,
        });
    }
    Some(out)
}

// ---------------------------------------------------------------------------------------------
// Tower defense
// ---------------------------------------------------------------------------------------------

/// `TowerDefProgKillTarget`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TowerDefProgKillTarget {
    pub id: u32,
    pub kill_target_id: u32,
    pub kill_target_type: String,
    pub kill_count: u32,
}

/// `TowerDefProgSpawnTarget`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TowerDefProgSpawnTarget {
    pub id: u32,
    pub spawn_target_id: u32,
    pub spawn_target_type: String,
    pub despawn_on_next_step: bool,
}

/// `TowerDefProg` (`TowerDef` is the owning [`TowerDef`]).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct TowerDefProg {
    pub id: u32,
    pub tower_def_id: u32,
    pub cond_to_next_time: f32,
    pub cond_comp_by_and: bool,
    pub kill_targets: Vec<TowerDefProgKillTarget>,
    pub spawn_targets: Vec<TowerDefProgSpawnTarget>,
}

/// `TowerDef`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct TowerDef {
    pub id: u32,
    pub time_of_day: f32,
    pub first_wave_after: f32,
    pub target_npc_spawn_id: u32,
    pub kill_npc_id: u32,
    pub kill_npc_count: u32,
    pub force_end_time: f32,
    pub time_of_day_day_interval: u32,
    /// `Progs` (ids into [`TowerDefData::progs`], in load order).
    pub progs: Vec<u32>,
}

/// `TowerDefGameData`.
#[derive(Debug, Clone, Default)]
pub struct TowerDefData {
    pub tower_defs: HashMap<u32, TowerDef>,
    pub progs: HashMap<u32, TowerDefProg>,
}

impl TowerDefData {
    /// `TowerDefGameData.Load`. Unused parents are skipped without dropping later valid waves.
    pub fn load(conn: &Connection) -> Result<Self, GameDataError> {
        let mut d = TowerDefData::default();
        let mut defs = Vec::new();
        for_each_row(conn, "SELECT * FROM tower_defs", [], |r| {
            defs.push(TowerDef {
                id: r.u32("id")?,
                time_of_day: r.f32("tod")?,
                first_wave_after: r.f32("first_wave_after")?,
                target_npc_spawn_id: r.u32_or("target_npc_spawner_id", 0)?,
                kill_npc_id: r.u32_or("kill_npc_id", 0)?,
                kill_npc_count: r.u32_or("kill_npc_count", 0)?,
                force_end_time: r.f32("force_end_time")?,
                time_of_day_day_interval: r.u32("tod_day_interval")?,
                progs: Vec::new(),
            });
            Ok(())
        })?;
        for t in defs {
            let id = t.id;
            if d.tower_defs.insert(id, t).is_some() {
                return Err(GameDataError::File(format!(
                    "TowerDefGameData: duplicate tower def {id}"
                )));
            }
        }

        let mut progs = Vec::new();
        for_each_row(conn, "SELECT * FROM tower_def_progs ORDER BY id", [], |r| {
            let tower_def_id = r.u32("tower_def_id")?;
            if !d.tower_defs.contains_key(&tower_def_id) {
                tracing::warn!(tower_def_id, "Skipping orphan tower defense wave");
                return Ok(());
            }
            progs.push(TowerDefProg {
                id: r.u32("id")?,
                tower_def_id,
                cond_to_next_time: r.f32("cond_to_next_time")?,
                cond_comp_by_and: r.bool_str("cond_comp_by_and")?,
                kill_targets: Vec::new(),
                spawn_targets: Vec::new(),
            });
            Ok(())
        })?;
        for p in progs {
            let (id, def) = (p.id, p.tower_def_id);
            if d.progs.insert(id, p).is_some() {
                return Err(GameDataError::File(format!(
                    "TowerDefGameData: duplicate tower def prog {id}"
                )));
            }
            if let Some(t) = d.tower_defs.get_mut(&def) {
                t.progs.push(id);
            }
        }

        for_each_row(
            conn,
            "SELECT * FROM tower_def_prog_spawn_targets ORDER BY id",
            [],
            |r| {
                let prog_id = r.u32("tower_def_prog_id")?;
                let Some(prog) = d.progs.get_mut(&prog_id) else {
                    return Ok(());
                };
                prog.spawn_targets.push(TowerDefProgSpawnTarget {
                    id: r.u32("id")?,
                    spawn_target_id: r.u32("spawn_target_id")?,
                    spawn_target_type: r.string("spawn_target_type")?,
                    despawn_on_next_step: r.bool_str("despawn_on_next_step")?,
                });
                Ok(())
            },
        )?;

        for_each_row(
            conn,
            "SELECT * FROM tower_def_prog_kill_targets ORDER BY id",
            [],
            |r| {
                let prog_id = r.u32("tower_def_prog_id")?;
                let Some(prog) = d.progs.get_mut(&prog_id) else {
                    return Ok(());
                };
                prog.kill_targets.push(TowerDefProgKillTarget {
                    id: r.u32("id")?,
                    kill_target_id: r.u32("kill_target_id")?,
                    kill_target_type: r.string("kill_target_type")?,
                    kill_count: r.u32("kill_count")?,
                });
                Ok(())
            },
        )?;
        tracing::info!(
            "Loaded {} tower defs, {} tower def progs",
            d.tower_defs.len(),
            d.progs.len()
        );
        Ok(d)
    }

    /// Controller spawner templates used at event start and by its waves. These must not
    /// run as ordinary permanent world spawns.
    pub fn npc_spawner_ids(&self) -> std::collections::HashSet<u32> {
        self.tower_defs
            .values()
            .map(|d| d.target_npc_spawn_id)
            .filter(|id| *id != 0)
            .chain(self.progs.values().flat_map(|p| {
                p.spawn_targets
                    .iter()
                    .filter(|t| t.spawn_target_type == "NpcSpawner")
                    .map(|t| t.spawn_target_id)
            }))
            .collect()
    }
}

// ---------------------------------------------------------------------------------------------
// Zones (ZoneManager order)
// ---------------------------------------------------------------------------------------------

/// The `zones` rows in table order (`ZoneManager._zones` enumeration order).
#[derive(Debug, Clone, Default)]
pub struct ZoneOrder {
    /// (zone key, zone group id) in row order.
    pub zones: Vec<(u32, u32)>,
}

impl ZoneOrder {
    /// `SELECT * FROM zones` (key and group only).
    pub fn load(conn: &Connection) -> Result<Self, GameDataError> {
        let mut zones = Vec::new();
        for_each_row(conn, "SELECT * FROM zones", [], |r| {
            zones.push((r.u32("zone_key")?, r.u32_or("group_id", 0)?));
            Ok(())
        })?;
        Ok(Self { zones })
    }

    /// `ZoneManager.GetZoneKeysInZoneGroupById(zoneGroupId)`.
    pub fn zone_keys_in_group(&self, zone_group_id: u32) -> Vec<u32> {
        self.zones
            .iter()
            .filter(|(_, g)| *g == zone_group_id)
            .map(|(k, _)| *k)
            .collect()
    }
}

/// Everything loaded by [`super::Data::load`].
pub fn load_all(ctx: &DataLoadCtx<'_>) -> Result<super::Data, GameDataError> {
    let json_path = ctx.app_path.join("Data").join("battlefields.json");
    let json = std::fs::read(&json_path).ok().map(|b| {
        String::from_utf8_lossy(&b)
            .trim_start_matches('\u{feff}')
            .to_string()
    });
    Ok(super::Data {
        indun: IndunData::load(ctx.conn, ctx.language)?,
        battlefields: BattlefieldData::load(ctx.conn, json.as_deref())?,
        tower_defs: TowerDefData::load(ctx.conn)?,
        zone_order: ZoneOrder::load(ctx.conn)?,
    })
}
