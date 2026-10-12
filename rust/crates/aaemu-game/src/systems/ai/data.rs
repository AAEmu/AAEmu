//! Game data of the AI subsystem:
//!
//! * `GameData/AiGameData` (`npc_ai_params` parsed per `npcs.ai_file_id`, `ai_commands`,
//!   `ai_command_sets`, `npc_chat_bubbles`, `ai_events`) and `NpcManager.LoadAiParams`
//!   (`NpcTemplate.AiParams` = `GetAiParamsForId(npc_ai_param_id)`, looked up per template);
//! * the part of `Core/Managers/ModelManager` the AI and the physics read (`actor_models` with
//!   their `game_stances`, `ship_models`, resolved through `models` like `GetActorModel` /
//!   `GetShipModel`).

use std::collections::HashMap;
use std::sync::Arc;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::models::movement::GameStanceType;
use crate::systems::DataLoadCtx;

use super::params::{
    AiCommandCategory, AiCommandSets, AiCommands, AiEvent, AiParamType, AiParams, NpcChatBubble,
};

/// `GameStance` (`game_stances` row; `StanceId` is the DB value - 1).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct GameStance {
    pub id: u32,
    pub stance_id: GameStanceType,
    pub actor_model_id: u32,
    pub ai_move_speed_run: f32,
    pub ai_move_speed_slow: f32,
    pub ai_move_speed_sprint: f32,
    pub ai_move_speed_walk: f32,
    pub height_collider: f32,
    pub height_pivot: f32,
    pub normal_speed: f32,
    pub max_speed: f32,
    pub size: [f32; 3],
    pub model_offset: [f32; 3],
    pub view_offset: [f32; 3],
    pub use_capsule: bool,
}

/// `ActorModel`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ActorModel {
    pub id: u32,
    pub radius: f32,
    pub height: f32,
    /// 0 = normal, 1 = mounts, 2 = bird / fish (ignores gravity), 3 = underground.
    pub movement_id: i32,
    /// `Stances` (by `GameStanceType`).
    pub stances: Vec<(GameStanceType, GameStance)>,
}

impl ActorModel {
    /// `Stances.TryGetValue(stance)`.
    pub fn stance(&self, s: GameStanceType) -> Option<&GameStance> {
        self.stances.iter().find(|(k, _)| *k == s).map(|(_, v)| v)
    }
}

/// `ShipModelV1` (`ship_models` row).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct ShipModel {
    pub id: u32,
    pub velocity: f32,
    pub mass: f32,
    pub mass_center_x: f32,
    pub mass_center_y: f32,
    pub mass_center_z: f32,
    pub mass_box_size_x: f32,
    pub mass_box_size_y: f32,
    pub mass_box_size_z: f32,
    pub water_density: f32,
    /// `water_damping` (Rust: the CryPhysics hull buoyancy, `physics::cry`).
    pub water_damping: f32,
    pub water_resistance: f32,
    pub steer_vel: f32,
    pub accel: f32,
    pub reverse_accel: f32,
    pub reverse_velocity: f32,
    pub turn_accel: f32,
    pub tube_length: f32,
    pub tube_radius: f32,
    pub tube_offset_z: f32,
    pub keel_length: f32,
    pub keel_height: f32,
    pub keel_offset_z: f32,
}

/// The tables of this subsystem (`server.game_data.systems.ai`).
#[derive(Debug, Default)]
pub struct AiData {
    /// `_aiParams` (by `npc_ai_param_id`).
    pub params: HashMap<u32, Arc<AiParams>>,
    /// Ids whose parameter string failed to parse (the C# warning), for diagnostics.
    pub params_failed: Vec<u32>,
    /// `_aiCommands` (by `cmd_set_id`, in table order, duplicate ids skipped).
    pub commands: HashMap<u32, Vec<AiCommands>>,
    /// `_aiCommandSets`.
    pub command_sets: HashMap<u32, AiCommandSets>,
    /// `_npcChatBubbles` (by `ai_event_id`, last row wins).
    pub chat_bubbles: HashMap<i32, NpcChatBubble>,
    /// `_aiEventsByNpc`.
    pub events_by_npc: HashMap<i32, Vec<AiEvent>>,
    /// `ModelManager.GetActorModel(modelId)` (by model id).
    pub actor_models: HashMap<u32, Arc<ActorModel>>,
    /// `ModelManager.GetShipModel(modelId)` (by model id).
    pub ship_models: HashMap<u32, ShipModel>,
    /// Normal prefab references, by model id, for the engine's authored buoyancy parts.
    pub ship_model_refs: HashMap<u32, String>,
}

impl AiData {
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self::load_from(ctx.conn)?)
    }

    /// `AiGameData.Load` + the `ModelManager` tables.
    pub fn load_from(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = Self::default();
        d.load_ai_params(conn)?;
        d.load_commands(conn)?;
        d.load_chat_bubbles(conn)?;
        d.load_events(conn)?;
        d.load_models(conn)?;
        Ok(d)
    }

    fn load_ai_params(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        // fileTypeToId.TryAdd(npc_ai_param_id, (AiParamType)ai_file_id) in table order
        let mut file_type_to_id: HashMap<u32, u32> = HashMap::new();
        for_each_row(
            conn,
            "SELECT id, ai_file_id, npc_ai_param_id FROM npcs",
            [],
            |r| {
                let t = r.u32("ai_file_id")?;
                let id = r.u32("npc_ai_param_id")?;
                file_type_to_id.entry(id).or_insert(t);
                Ok(())
            },
        )?;
        for_each_row(conn, "SELECT * FROM npc_ai_params", [], |r| {
            let id = r.u32("id")?;
            let Some(file_type) = file_type_to_id.get(&id).copied() else {
                return Ok(());
            };
            let data = if r.is_null("ai_param")? {
                String::new()
            } else {
                r.string("ai_param")?
            };
            match AiParams::create_by_type(AiParamType::from_u32(file_type), &data) {
                Ok(Some(p)) => {
                    self.params.entry(id).or_insert_with(|| Arc::new(p));
                }
                Ok(None) => {}
                Err(e) => {
                    tracing::warn!("Impossible to parse npc_ai_params {id}\n{e}");
                    self.params_failed.push(id);
                }
            }
            Ok(())
        })?;
        Ok(())
    }

    fn load_commands(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        let mut seen = std::collections::HashSet::new();
        for_each_row(conn, "SELECT * FROM ai_commands", [], |r| {
            let id = r.u32("id")?;
            if !seen.insert(id) {
                return Ok(()); // The table contains duplicates.
            }
            let c = AiCommands {
                id,
                cmd_set_id: r.u32("cmd_set_id")?,
                cmd_id: AiCommandCategory::from_u32(r.u32("cmd_id")?),
                param1: r.u32("param1")?,
                param2: r.string("param2")?,
            };
            self.commands.entry(c.cmd_set_id).or_default().push(c);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM ai_command_sets", [], |r| {
            let s = AiCommandSets {
                id: r.u32("id")?,
                name: r.string("name")?,
                can_interact: r.bool("can_interact")?,
            };
            self.command_sets.entry(s.id).or_insert(s);
            Ok(())
        })
    }

    fn load_chat_bubbles(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        for_each_row(conn, "SELECT * FROM npc_chat_bubbles", [], |r| {
            let b = NpcChatBubble {
                id: r.i32("id")?,
                ai_event_id: r.i32("ai_event_id")?,
                bubble: r.string("bubble")?,
            };
            self.chat_bubbles.insert(b.ai_event_id, b);
            Ok(())
        })?;
        tracing::info!(
            "Loaded {} entries from npc_chat_bubbles.",
            self.chat_bubbles.len()
        );
        Ok(())
    }

    fn load_events(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        for_each_row(conn, "SELECT * FROM ai_events", [], |r| {
            let e = AiEvent {
                id: r.i32("id")?,
                ignore_category_id: r.i32("ignore_category_id")?,
                weight: r.f32_or("ignore_time", 0.0)?,
                event_name: r.string("name")?,
                npc_id: r.i32("npc_id")?,
                or_unit_reqs: if r.is_null("or_unit_reqs")? {
                    false
                } else {
                    r.bool("or_unit_reqs")?
                },
                skill_id: if r.is_null("skill_id")? {
                    0
                } else {
                    r.i32("skill_id")?
                },
            };
            self.events_by_npc.entry(e.npc_id).or_default().push(e);
            Ok(())
        })?;
        tracing::info!(
            "Loaded {} records from ai_events.",
            self.events_by_npc.len()
        );
        Ok(())
    }

    fn load_models(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        let mut actors: HashMap<u32, ActorModel> = HashMap::new();
        for_each_row(conn, "SELECT * FROM actor_models", [], |r| {
            let m = ActorModel {
                id: r.u32("id")?,
                radius: r.f32("radius")?,
                height: r.f32("height")?,
                movement_id: r.i32("movement_id")?,
                stances: Vec::new(),
            };
            actors.entry(m.id).or_insert(m);
            Ok(())
        })?;
        let mut ships: HashMap<u32, ShipModel> = HashMap::new();
        let mut ship_refs = HashMap::new();
        for_each_row(conn, "SELECT * FROM ship_models", [], |r| {
            let m = ShipModel {
                id: r.u32("id")?,
                velocity: r.f32("velocity")?,
                mass: r.f32("mass")?,
                mass_center_x: r.f32("mass_center_x")?,
                mass_center_y: r.f32("mass_center_y")?,
                mass_center_z: r.f32("mass_center_z")?,
                mass_box_size_x: r.f32("mass_box_size_x")?,
                mass_box_size_y: r.f32("mass_box_size_y")?,
                mass_box_size_z: r.f32("mass_box_size_z")?,
                water_density: r.f32_or("water_density", 1.0)?,
                water_damping: r.f32_or("water_damping", 0.0).unwrap_or(0.0),
                water_resistance: r.f32_or("water_resistance", 1.0)?,
                steer_vel: r.f32("steer_vel")?,
                accel: r.f32("accel")?,
                reverse_accel: r.f32("reverse_accel")?,
                reverse_velocity: r.f32("reverse_velocity")?,
                turn_accel: r.f32("turn_accel")?,
                tube_length: r.f32("tube_length")?,
                tube_radius: r.f32("tube_radius")?,
                tube_offset_z: r.f32("tube_offset_z")?,
                keel_length: r.f32("keel_length")?,
                keel_height: r.f32("keel_height")?,
                keel_offset_z: r.f32("keel_offset_z")?,
            };
            ship_refs.entry(m.id).or_insert(r.string_or("normal", "")?);
            ships.entry(m.id).or_insert(m);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM game_stances", [], |r| {
            let st = GameStance {
                id: r.u32("id")?,
                actor_model_id: r.u32("actor_model_id")?,
                stance_id: GameStanceType(r.u8("stance_id")?.wrapping_sub(1)),
                ai_move_speed_run: r.f32("ai_move_speed_run")?,
                ai_move_speed_slow: r.f32("ai_move_speed_slow")?,
                ai_move_speed_sprint: r.f32("ai_move_speed_sprint")?,
                ai_move_speed_walk: r.f32("ai_move_speed_walk")?,
                height_collider: r.f32("height_collider")?,
                height_pivot: r.f32("height_pivot")?,
                max_speed: r.f32("max_speed")?,
                model_offset: [
                    r.f32("model_offset_x")?,
                    r.f32("model_offset_y")?,
                    r.f32("model_offset_z")?,
                ],
                normal_speed: r.f32("normal_speed")?,
                size: [r.f32("size_x")?, r.f32("size_y")?, r.f32("size_z")?],
                use_capsule: if r.is_null("use_capsule")? {
                    true
                } else {
                    r.bool("use_capsule")?
                },
                view_offset: [
                    r.f32("view_offset_x")?,
                    r.f32("view_offset_y")?,
                    r.f32("view_offset_z")?,
                ],
            };
            if let Some(m) = actors.get_mut(&st.actor_model_id) {
                if m.stance(st.stance_id).is_some() {
                    // Dictionary.Add throws on a duplicate stance (aborts ModelManager.Load).
                    return Err(rusqlite::Error::InvalidParameterName(format!(
                        "duplicate stance {} for actor model {}",
                        st.stance_id.0, st.actor_model_id
                    )));
                }
                m.stances.push((st.stance_id, st));
            }
            Ok(())
        })?;
        let actors: HashMap<u32, Arc<ActorModel>> =
            actors.into_iter().map(|(k, v)| (k, Arc::new(v))).collect();
        for_each_row(conn, "SELECT * FROM models", [], |r| {
            let id = r.u32("id")?;
            let sub_id = r.u32("sub_id")?;
            match r.string("sub_type")?.as_str() {
                "ActorModel" => {
                    if let Some(a) = actors.get(&sub_id) {
                        self.actor_models.entry(id).or_insert_with(|| a.clone());
                    }
                }
                "ShipModel" => {
                    if let Some(s) = ships.get(&sub_id) {
                        self.ship_models.entry(id).or_insert(*s);
                        if let Some(reference) = ship_refs.get(&sub_id) {
                            self.ship_model_refs
                                .entry(id)
                                .or_insert_with(|| reference.clone());
                        }
                    }
                }
                _ => {}
            }
            Ok(())
        })?;
        Ok(())
    }

    /// `AiGameData.GetAiParamsForId(id)`.
    pub fn ai_params_for_id(&self, id: u32) -> Option<Arc<AiParams>> {
        self.params.get(&id).cloned()
    }

    /// `AiGameData.GetAiCommands(id)`.
    pub fn ai_commands(&self, id: u32) -> Option<&[AiCommands]> {
        self.commands.get(&id).map(Vec::as_slice)
    }

    /// `AiGameData.TryGet(aiEventId)`.
    pub fn chat_bubble(&self, ai_event_id: i32) -> Option<&NpcChatBubble> {
        self.chat_bubbles.get(&ai_event_id)
    }

    /// `AiGameData.GetEvents(npcId, eventName)` (case-insensitive name).
    pub fn events(&self, npc_id: i32, name: &str) -> Vec<&AiEvent> {
        self.events_by_npc.get(&npc_id).map_or_else(Vec::new, |l| {
            l.iter()
                .filter(|e| e.event_name.eq_ignore_ascii_case(name))
                .collect()
        })
    }

    /// `AiGameData.GetEvent(npcId, eventName, weight)`: the first event of that name whose
    /// weight is at least `weight`.
    pub fn event(&self, npc_id: i32, name: &str, weight: f32) -> Option<&AiEvent> {
        self.events_by_npc
            .get(&npc_id)?
            .iter()
            .find(|e| e.event_name.eq_ignore_ascii_case(name) && e.weight >= weight)
    }

    /// `ModelManager.GetActorModel(modelId)`.
    pub fn actor_model(&self, model_id: u32) -> Option<&Arc<ActorModel>> {
        self.actor_models.get(&model_id)
    }

    /// `ModelManager.GetShipModel(modelId)`.
    pub fn ship_model(&self, model_id: u32) -> Option<&ShipModel> {
        self.ship_models.get(&model_id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::ai::params::{AiParamsKind, AiSkillList, SkillList};

    fn conn() -> Option<Connection> {
        let path = std::env::var("AAEMU_COMPACT_DB").ok()?;
        Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).ok()
    }

    /// Gated: the AI tables of the real compact.sqlite3 load (row counts, model resolution).
    #[test]
    #[ignore]
    fn real_compact_db_ai_tables() {
        let Some(c) = conn() else {
            return;
        };
        let d = AiData::load_from(&c).expect("ai data");
        assert!(d.params.len() > 1000, "{}", d.params.len());
        // npc_ai_params 443, 835 and 1975 are Lua syntax errors (the C# warns and skips them).
        let mut failed = d.params_failed.clone();
        failed.sort_unstable();
        assert_eq!(failed, vec![443, 835, 1975]);
        assert!(!d.commands.is_empty());
        assert_eq!(d.command_sets.len(), 325);
        assert!(d.chat_bubbles.len() > 1000);
        assert!(!d.events_by_npc.is_empty());
        assert!(d.actor_models.len() > 500);
        assert!(!d.ship_models.is_empty());
        let with_stances = d
            .actor_models
            .values()
            .filter(|m| !m.stances.is_empty())
            .count();
        assert!(with_stances > 500, "{with_stances}");
    }

    fn fmt_f(f: f32) -> String {
        f.to_bits().cast_signed().to_string()
    }

    fn fmt_list(l: &[u32]) -> String {
        format!(
            "[{}]",
            l.iter().map(u32::to_string).collect::<Vec<_>>().join(",")
        )
    }

    fn fmt_skill(s: &crate::systems::ai::params::AiSkill) -> String {
        format!(
            "{{{},{},{}}}",
            s.skill_id,
            u8::from(s.strafe),
            fmt_f(s.delay)
        )
    }

    fn fmt_sl(s: &SkillList) -> String {
        format!(
            "{{{},{},{},{},{},{},[{}]}}",
            s.use_type,
            s.dice,
            s.health_range_min,
            s.health_range_max,
            fmt_f(s.time_range_start),
            fmt_f(s.time_range_end),
            s.skills.iter().map(fmt_skill).collect::<Vec<_>>().join(",")
        )
    }

    fn fmt_asl(s: &AiSkillList) -> String {
        format!(
            "{{{}|{},{},{},{},{},{},{},{},{},[{}],[{}]}}",
            s.pipe_name,
            s.phase_type,
            s.use_type,
            s.dice,
            s.health_range_min,
            s.health_range_max,
            fmt_f(s.time_range_start),
            fmt_f(s.time_range_end),
            u8::from(s.restoration),
            u8::from(s.go_return),
            s.start_ai_skills
                .iter()
                .map(fmt_skill)
                .collect::<Vec<_>>()
                .join(","),
            s.skill_lists
                .iter()
                .map(fmt_sl)
                .collect::<Vec<_>>()
                .join(",")
        )
    }

    /// The format the C# NLua harness prints for one parsed `AiParams`.
    fn format_params(p: &AiParams) -> String {
        let g = format!(
            "{},{},{},{},{},{},{},{},{}",
            fmt_f(p.alert_duration),
            u8::from(p.alert_to_attack),
            fmt_f(p.alert_safe_target_remember_time),
            u8::from(p.always_teleport_on_return),
            p.max_make_a_gap_count,
            fmt_f(p.melee_attack_range),
            fmt_f(p.prefered_combat_dist),
            u8::from(p.restoration_on_return()),
            u8::from(p.go_return_state())
        );
        let j = |l: &[AiSkillList]| l.iter().map(fmt_asl).collect::<Vec<_>>().join(",");
        let k = match &p.kind {
            AiParamsKind::Almighty {
                idle_ai,
                can_change_ai_unit_attr,
                ai_phase_change_type,
                ai_skill_lists,
                ai_path_skill_lists,
                ai_path_damage_skill_lists,
                ..
            } => format!(
                "A:{idle_ai}|{can_change_ai_unit_attr},{ai_phase_change_type},[{}],[{}],[{}]",
                j(ai_skill_lists),
                j(ai_path_skill_lists),
                j(ai_path_damage_skill_lists)
            ),
            AiParamsKind::Archer { combat_skills } => format!(
                "R:{}",
                combat_skills
                    .iter()
                    .map(|c| format!(
                        "{{{},{},{},{}}}",
                        fmt_list(&c.melee),
                        fmt_list(&c.make_a_gap),
                        fmt_list(&c.ranged_def),
                        fmt_list(&c.ranged_strong)
                    ))
                    .collect::<Vec<_>>()
                    .join(",")
            ),
            AiParamsKind::BigMonster { combat_skills } => format!(
                "B:{}",
                combat_skills
                    .iter()
                    .map(|c| format!(
                        "{{{},{},{},{},{}}}",
                        c.skill_type,
                        fmt_f(c.skill_delay),
                        u8::from(c.strafe_during_delay),
                        c.health_range_min,
                        c.health_range_max
                    ))
                    .collect::<Vec<_>>()
                    .join(",")
            ),
            AiParamsKind::Flytrap {
                attack_end_distance,
                combat_skills,
            } => format!(
                "F:{},{},{}",
                fmt_f(*attack_end_distance),
                fmt_list(&combat_skills.melee),
                fmt_list(&combat_skills.ranged)
            ),
            AiParamsKind::WildBoar {
                on_combat_start_skills,
                on_spurt_skills,
            } => format!(
                "W:{},[{}]",
                fmt_list(on_combat_start_skills),
                on_spurt_skills
                    .iter()
                    .map(|s| format!("{{{},{}}}", s.skill_type, s.health_condition))
                    .collect::<Vec<_>>()
                    .join(",")
            ),
        };
        format!("{g};{k}")
    }

    /// Gated differential test against the C# parser: `AAEMU_AI_PARAMS_EXPECTED` points to the
    /// TSV the NLua harness (the C# `*AiParams` classes run with NLua 1.7.9) printed for every
    /// `npc_ai_params` row of the same compact.sqlite3. Every row must match exactly (floats
    /// compared by bits, errors by occurrence).
    #[test]
    #[ignore]
    fn real_compact_db_params_match_nlua() {
        let (Some(c), Ok(expected)) = (conn(), std::env::var("AAEMU_AI_PARAMS_EXPECTED")) else {
            return;
        };
        let expected = std::fs::read_to_string(expected).expect("expected tsv");
        let mut types: HashMap<u32, u32> = HashMap::new();
        for_each_row(
            &c,
            "SELECT id, ai_file_id, npc_ai_param_id FROM npcs",
            [],
            |r| {
                types
                    .entry(r.u32("npc_ai_param_id")?)
                    .or_insert(r.u32("ai_file_id")?);
                Ok(())
            },
        )
        .unwrap();
        let mut rows: HashMap<u32, String> = HashMap::new();
        for_each_row(&c, "SELECT * FROM npc_ai_params", [], |r| {
            rows.insert(r.u32("id")?, r.string_or("ai_param", "")?);
            Ok(())
        })
        .unwrap();
        let mut checked = 0;
        let mut mismatches = Vec::new();
        for line in expected.lines() {
            let (id, want) = line.split_once('\t').unwrap();
            let id: u32 = id.parse().unwrap();
            let got = match AiParams::create_by_type(AiParamType::from_u32(types[&id]), &rows[&id])
            {
                Ok(None) => "null".to_string(),
                Ok(Some(p)) => format_params(&p),
                Err(_) => "ERR".to_string(),
            };
            let want = if want.starts_with("ERR") { "ERR" } else { want };
            if got != want {
                mismatches.push(format!("{id}\n  want {want}\n  got  {got}"));
            }
            checked += 1;
        }
        assert!(checked > 2000);
        assert!(
            mismatches.is_empty(),
            "{} mismatches:\n{}",
            mismatches.len(),
            mismatches[..mismatches.len().min(10)].join("\n")
        );
    }
}
