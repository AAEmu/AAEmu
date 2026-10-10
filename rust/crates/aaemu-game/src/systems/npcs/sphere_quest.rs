//! Quest spheres, world side: `SphereGameData` (`GameData/SphereGameData.cs`, the `spheres` /
//! `sphere_*` tables), `SphereQuestManager` (`Core/Managers/World/SphereQuestManager.cs`: the
//! client's `quest_sign_sphere.g` areas, per-player quest sphere triggers and the global quest
//! starter spheres) and `SphereQuest` / `SphereQuestTrigger` / `SphereQuestStarter`
//! (`Models/Game/World/SphereQuest.cs`).
//!
//! The quest logic is the quests subsystem's: quest components are looked up and the enter /
//! exit events are delivered through `server.systems.npcs.hooks.quests`
//! ([`super::hooks::QuestsBridge`]); `UnitRequirementsGameData.CanTriggerSphere` is
//! `QuestsBridge::can_trigger_sphere`.
//!
//! C# quirks mirrored: the sphere table is static and loaded from the *first* instance's world
//! template only; the add-queue "already registered" loop has no effect (its `break` only leaves
//! the inner loop); with `NpcTemplate` set the trigger centres on *any* NPC near the player (the
//! template is never compared); the quest starters share one last-position table per player,
//! so a later starter in the same tick sees the position already updated.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, OnceLock, Weak};
use std::time::{Duration, Instant};

use tracing::{debug, error, info, warn};

use super::area_trigger::{get_around_units, region_has_player_activity};
use crate::data::client_files::ClientFileManager;
use crate::data::sqlite::{for_each_row, SqlRow};
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;
use crate::world::{PlayerRef, WorldInstance, WorldObject};

/// `SphereQuestManager.Initialize`: tick period.
pub const TICK_DELAY: Duration = Duration::from_millis(500);

/// `AreaSphereTriggerCondition`.
pub mod trigger_condition {
    pub const NONE: u32 = 0;
    pub const TRIGGER_ONCE_AT_ALL: u32 = 1;
    pub const TRIGGER_ONCE_IN_RUNTIME: u32 = 2;
    pub const TRIGGER_EVERY_N_TIME_AFTER: u32 = 3;
}

// ---------------------------------------------------------------------------------------------
// SphereGameData
// ---------------------------------------------------------------------------------------------

/// `Spheres` (`spheres` row).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Sphere {
    pub id: u32,
    pub name: String,
    pub enter_or_leave: bool,
    pub sphere_detail_id: u32,
    pub sphere_detail_type: String,
    /// `AreaSphereTriggerCondition`.
    pub trigger_condition_id: u32,
    pub trigger_condition_time: u32,
    pub team_msg: String,
    pub category_id: u32,
    pub or_unit_reqs: bool,
    pub is_personal_msg: bool,
    pub milestone_id: u32,
    pub name_tr: bool,
    pub team_msg_tr: bool,
}

/// `SphereQuests`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereQuests {
    pub id: u32,
    pub quest_id: u32,
    /// `QuestTrigger`.
    pub quest_trigger_id: u32,
}

/// `SphereSkills`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereSkills {
    pub id: u32,
    pub skill_id: u32,
    pub max_rate: u32,
    pub min_rate: u32,
}

/// `SphereSounds`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereSounds {
    pub id: u32,
    pub sound_id: u32,
    pub broadcast: bool,
}

/// `SphereDoodadInteracts`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereDoodadInteracts {
    pub id: u32,
    pub skill_id: u32,
    pub doodad_family_id: u32,
}

/// `SphereChatBubbles`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SphereChatBubbles {
    pub id: u32,
    pub sphere_bubble_id: u32,
    pub is_start: bool,
    pub speech: String,
    pub npc_id: u32,
    pub npc_spawner_id: u32,
    pub next_bubble: u32,
    pub sound_id: u32,
    pub angle: u32,
    /// `ChatBubbleKind`.
    pub chat_bubble_kind_id: u32,
    pub facial: String,
    pub camera_id: u32,
    pub change_speaker_name: String,
}

/// `SphereBuffs`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereBuffs {
    pub id: u32,
    pub buff_id: u32,
}

/// `SphereAcceptQuestQuests`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SphereAcceptQuestQuests {
    pub id: u32,
    pub sphere_accept_quest_id: u32,
    pub quest_id: u32,
}

/// A `Dictionary<uint, T>` keeping insertion order (`.Values` order).
#[derive(Debug, Clone)]
pub struct Table<T> {
    rows: Vec<T>,
    index: HashMap<u32, usize>,
}

impl<T> Default for Table<T> {
    fn default() -> Self {
        Self {
            rows: Vec::new(),
            index: HashMap::new(),
        }
    }
}

impl<T> Table<T> {
    /// `Dictionary.Add`: `false` for a duplicate key (C# throws).
    fn add(&mut self, id: u32, row: T) -> bool {
        if self.index.contains_key(&id) {
            return false;
        }
        self.index.insert(id, self.rows.len());
        self.rows.push(row);
        true
    }

    pub fn get(&self, id: u32) -> Option<&T> {
        self.index.get(&id).map(|&i| &self.rows[i])
    }

    /// `.Values` (insertion order).
    pub fn values(&self) -> &[T] {
        &self.rows
    }

    pub fn len(&self) -> usize {
        self.rows.len()
    }

    pub fn is_empty(&self) -> bool {
        self.rows.is_empty()
    }
}

/// `SphereGameData` (`server.game_data.systems.npcs.sphere`).
#[derive(Debug, Default)]
pub struct SphereData {
    pub spheres: Table<Sphere>,
    pub sphere_quests: Table<SphereQuests>,
    pub sphere_skills: Table<SphereSkills>,
    pub sphere_sounds: Table<SphereSounds>,
    pub sphere_doodad_interacts: Table<SphereDoodadInteracts>,
    pub sphere_chat_bubbles: Table<SphereChatBubbles>,
    pub sphere_buffs: Table<SphereBuffs>,
    /// `sphere_bubbles` (only ids).
    pub sphere_bubbles: Table<u32>,
    /// `sphere_accept_quests` (only ids).
    pub sphere_accept_quests: Table<u32>,
    pub sphere_accept_quest_quests: Table<SphereAcceptQuestQuests>,
}

fn load_table<T>(
    ctx: &DataLoadCtx<'_>,
    name: &str,
    table: &mut Table<T>,
    mut f: impl FnMut(&SqlRow<'_, '_>) -> rusqlite::Result<(u32, T)>,
) -> Result<(), crate::data::GameDataError> {
    let mut duplicate = None;
    for_each_row(ctx.conn, &format!("SELECT * FROM {name}"), [], |r| {
        let (id, row) = f(r)?;
        if !table.add(id, row) {
            duplicate.get_or_insert(id);
        }
        Ok(())
    })?;
    match duplicate {
        Some(id) => Err(crate::data::GameDataError::File(format!(
            "{name}: duplicate id {id}"
        ))),
        None => Ok(()),
    }
}

impl SphereData {
    /// `SphereGameData.Load`. Booleans use `GetBoolean` (`GetInt64 != 0`): the `'t'`/`'f'`
    /// texts of compact.sqlite3 read as false, as in C#.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let mut d = Self::default();
        load_table(ctx, "spheres", &mut d.spheres, |r| {
            let s = Sphere {
                id: r.u32("id")?,
                name: r.string("name")?,
                enter_or_leave: r.bool("enter_or_leave")?,
                sphere_detail_id: r.u32("sphere_detail_id")?,
                sphere_detail_type: r.string("sphere_detail_type")?,
                trigger_condition_id: r.u32("trigger_condition_id")?,
                trigger_condition_time: r.u32_or("trigger_condition_time", 0)?,
                team_msg: r.string("team_msg")?,
                category_id: r.u32("category_id")?,
                or_unit_reqs: r.bool("or_unit_reqs")?,
                is_personal_msg: r.bool("is_personal_msg")?,
                milestone_id: r.u32("milestone_id")?,
                name_tr: r.bool("name_tr")?,
                team_msg_tr: r.bool("team_msg_tr")?,
            };
            Ok((s.id, s))
        })?;
        load_table(ctx, "sphere_quests", &mut d.sphere_quests, |r| {
            let s = SphereQuests {
                id: r.u32("id")?,
                quest_id: r.u32("quest_id")?,
                quest_trigger_id: r.u32("quest_trigger_id")?,
            };
            Ok((s.id, s))
        })?;
        load_table(ctx, "sphere_skills", &mut d.sphere_skills, |r| {
            let s = SphereSkills {
                id: r.u32("id")?,
                skill_id: r.u32("skill_id")?,
                max_rate: r.u32("max_rate")?,
                min_rate: r.u32("min_rate")?,
            };
            Ok((s.id, s))
        })?;
        load_table(ctx, "sphere_sounds", &mut d.sphere_sounds, |r| {
            let s = SphereSounds {
                id: r.u32("id")?,
                sound_id: r.u32("sound_id")?,
                broadcast: r.bool("broadcast")?,
            };
            Ok((s.id, s))
        })?;
        load_table(
            ctx,
            "sphere_doodad_interacts",
            &mut d.sphere_doodad_interacts,
            |r| {
                let s = SphereDoodadInteracts {
                    id: r.u32("id")?,
                    skill_id: r.u32("skill_id")?,
                    doodad_family_id: r.u32("doodad_family_id")?,
                };
                Ok((s.id, s))
            },
        )?;
        load_table(
            ctx,
            "sphere_chat_bubbles",
            &mut d.sphere_chat_bubbles,
            |r| {
                let s = SphereChatBubbles {
                    id: r.u32("id")?,
                    sphere_bubble_id: r.u32("sphere_bubble_id")?,
                    is_start: r.bool("is_start")?,
                    speech: r.string("speech")?,
                    npc_id: r.u32_or("npc_id", 0)?,
                    npc_spawner_id: r.u32_or("npc_spawner_id", 0)?,
                    next_bubble: r.u32_or("next_bubble", 0)?,
                    sound_id: r.u32_or("sound_id", 0)?,
                    angle: r.u32_or("angle", 0)?,
                    chat_bubble_kind_id: r.u32("chat_bubble_kind_id")?,
                    facial: r.string_or("facial", "")?,
                    camera_id: r.u32_or("camera_id", 0)?,
                    change_speaker_name: r.string_or("change_speaker_name", "")?,
                };
                Ok((s.id, s))
            },
        )?;
        load_table(ctx, "sphere_buffs", &mut d.sphere_buffs, |r| {
            let s = SphereBuffs {
                id: r.u32("id")?,
                buff_id: r.u32("buff_id")?,
            };
            Ok((s.id, s))
        })?;
        load_table(ctx, "sphere_bubbles", &mut d.sphere_bubbles, |r| {
            let id = r.u32("id")?;
            Ok((id, id))
        })?;
        load_table(
            ctx,
            "sphere_accept_quests",
            &mut d.sphere_accept_quests,
            |r| {
                let id = r.u32("id")?;
                Ok((id, id))
            },
        )?;
        load_table(
            ctx,
            "sphere_accept_quest_quests",
            &mut d.sphere_accept_quest_quests,
            |r| {
                let s = SphereAcceptQuestQuests {
                    id: r.u32("id")?,
                    sphere_accept_quest_id: r.u32("sphere_accept_quest_id")?,
                    quest_id: r.u32("quest_id")?,
                };
                Ok((s.id, s))
            },
        )?;
        Ok(d)
    }

    /// `GetSphereIdFromQuest(questId)`: the first `SphereQuest`-typed sphere of the quest's
    /// `sphere_quests` rows (0 = none).
    pub fn get_sphere_id_from_quest(&self, quest_id: u32) -> u32 {
        for sq in self
            .sphere_quests
            .values()
            .iter()
            .filter(|x| x.quest_id == quest_id)
        {
            if let Some(s) = self
                .spheres
                .values()
                .iter()
                .find(|x| x.sphere_detail_id == sq.id && x.sphere_detail_type == "SphereQuest")
            {
                return s.id;
            }
        }
        0
    }

    /// `GetSphere(sphereId)`.
    pub fn get_sphere(&self, sphere_id: u32) -> Option<&Sphere> {
        self.spheres.get(sphere_id)
    }
}

// ---------------------------------------------------------------------------------------------
// SphereQuest (quest_sign_sphere.g)
// ---------------------------------------------------------------------------------------------

/// `MathUtil.CalculateDistance(a, b, includeZAxis)` (float differences, double sums).
pub fn calculate_distance(a: [f32; 3], b: [f32; 3], include_z: bool) -> f32 {
    let dx = f64::from(a[0] - b[0]);
    let dy = f64::from(a[1] - b[1]);
    if include_z {
        let dz = f64::from(a[2] - b[2]);
        (dx * dx + dy * dy + dz * dz).sqrt() as f32
    } else {
        (dx * dx + dy * dy).sqrt() as f32
    }
}

/// `SphereQuest`: one `area` of a `quest_sign_sphere.g` file (world coordinates).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct SphereQuest {
    /// The zone key the file belongs to.
    pub zone_id: u32,
    /// `WorldId` (the world template name).
    pub world_id: String,
    pub quest_id: u32,
    pub component_id: u32,
    pub radius: f32,
    pub xyz: [f32; 3],
}

impl SphereQuest {
    /// `Contains(pos)`.
    pub fn contains(&self, pos: [f32; 3]) -> bool {
        calculate_distance(self.xyz, pos, true) <= self.radius
    }

    /// `DbSphere` (`GetSphere(GetSphereIdFromQuest(QuestId))`).
    pub fn db_sphere<'a>(&self, data: &'a SphereData) -> Option<&'a Sphere> {
        data.get_sphere(data.get_sphere_id_from_quest(self.quest_id))
    }
}

/// `string.Trim(' ').Trim('\t').Trim('\r')` (one character class at a time).
fn trim_line(s: &str) -> &str {
    s.trim_matches(' ').trim_matches('\t').trim_matches('\r')
}

/// .NET white space accepted around numbers by `NumberStyles.Integer` / `Float`.
fn trim_number(s: &str) -> &str {
    s.trim_matches(|c: char| matches!(c, '\t'..='\r' | ' '))
}

/// `uint.Parse(s)`.
fn parse_uint(s: &str) -> Option<u32> {
    let t = trim_number(s);
    let t = t.strip_prefix('+').unwrap_or(t);
    if let Some(d) = t.strip_prefix('-') {
        return (!d.is_empty() && d.bytes().all(|b| b == b'0')).then_some(0);
    }
    t.parse().ok()
}

/// `float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)`.
fn parse_float(s: &str) -> Option<f32> {
    let t = trim_number(s);
    if t.is_empty() || t.contains(',') {
        return None;
    }
    t.parse().ok()
}

/// `Substring(start)`: `None` when `start` is past the end (C# throws).
fn substring(s: &str, start: usize) -> Option<&str> {
    s.get(start..)
}

/// The spheres of one `quest_sign_sphere.g` (lines in `area` / `qtype` / `ctype` / `pos` /
/// `radius` blocks), converted to world coordinates with `origin` (the zone's origin cell,
/// `ZoneManager.ConvertToWorldCoordinates`). `Err` = the C# rethrows (bad number / short line).
pub fn parse_quest_sign_sphere(
    contents: &str,
    zone_id: u32,
    world_name: &str,
    origin: (i32, i32),
) -> Result<Vec<SphereQuest>, String> {
    let lower = contents.to_lowercase();
    let area: Vec<&str> = lower.split('\n').collect();
    let mut out = Vec::new();
    let mut i = 0usize;
    while (i as isize) < area.len() as isize - 4 {
        let l0 = trim_line(area[i]);
        let l1 = trim_line(area[i + 1]);
        let l2 = trim_line(area[i + 2]);
        let l3 = trim_line(area[i + 3]);
        let l4 = trim_line(area[i + 4]);
        if l0.starts_with("area")
            && l1.starts_with("qtype")
            && l2.starts_with("ctype")
            && l3.starts_with("pos")
            && l4.starts_with("radius")
        {
            let err = || format!("Loading SphereQuest error! (line {})", i + 1);
            let quest_id = substring(l1, 6).and_then(parse_uint).ok_or_else(err)?;
            let component_id = substring(l2, 6).and_then(parse_uint).ok_or_else(err)?;
            let sub_line: String = substring(l3, 4)
                .ok_or_else(err)?
                .chars()
                .filter(|c| !matches!(c, '(' | ')' | 'x' | 'y' | 'z' | ' '))
                .collect();
            let pos: Vec<&str> = sub_line.split(',').collect();
            let mut xyz = [0f32; 3];
            if pos.len() == 3 {
                for (k, p) in pos.iter().enumerate() {
                    xyz[k] = parse_float(p).ok_or_else(err)?;
                }
            }
            let radius = substring(l4, 7).and_then(parse_float).ok_or_else(err)?;
            // ZoneManager.ConvertToWorldCoordinates(zoneId, Xyz)
            let xyz = [
                origin.0 as f32 * 1024f32 + xyz[0],
                origin.1 as f32 * 1024f32 + xyz[1],
                xyz[2],
            ];
            out.push(SphereQuest {
                zone_id,
                world_id: world_name.to_string(),
                quest_id,
                component_id,
                radius,
                xyz,
            });
            i += 4;
        }
        i += 1;
    }
    Ok(out)
}

/// `Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)))` parsed as the zone.
fn zone_id_from_path(path: &str) -> Option<u32> {
    let parts: Vec<&str> = path.split(['/', '\\']).collect();
    if parts.len() < 3 {
        return None;
    }
    parts[parts.len() - 3].parse().ok()
}

/// The `_sphereQuests` table: component id → spheres (insertion order kept).
#[derive(Debug, Clone, Default)]
pub struct SphereQuestTable {
    entries: Vec<(u32, Vec<SphereQuest>)>,
    index: HashMap<u32, usize>,
}

impl SphereQuestTable {
    fn push(&mut self, sphere: SphereQuest) {
        match self.index.get(&sphere.component_id) {
            Some(&i) => self.entries[i].1.push(sphere),
            None => {
                self.index.insert(sphere.component_id, self.entries.len());
                self.entries.push((sphere.component_id, vec![sphere]));
            }
        }
    }

    /// `GetQuestSpheres(componentId)`.
    pub fn get(&self, component_id: u32) -> Option<&[SphereQuest]> {
        self.index
            .get(&component_id)
            .map(|&i| self.entries[i].1.as_slice())
    }

    /// `(componentId, spheres)` in insertion order.
    pub fn entries(&self) -> &[(u32, Vec<SphereQuest>)] {
        &self.entries
    }

    /// `GetSpheresForQuest(questId)`.
    pub fn spheres_for_quest(&self, quest_id: u32) -> Vec<SphereQuest> {
        self.entries
            .iter()
            .flat_map(|(_, v)| v.iter().filter(|s| s.quest_id == quest_id).cloned())
            .collect()
    }

    /// Number of spheres.
    pub fn sphere_count(&self) -> usize {
        self.entries.iter().map(|(_, v)| v.len()).sum()
    }
}

/// `SphereQuestManager.LoadQuestSpheres(worldTemplate)`.
pub fn load_quest_spheres(
    files: &ClientFileManager,
    worlds: &WorldTemplates,
    world_template: &WorldTemplate,
) -> Result<SphereQuestTable, String> {
    info!("Loading SphereQuest...");
    let dir = format!("game/worlds/{}/level_design/zone", world_template.name);
    let path_files = files.files_in_directory(&dir, "quest_sign_sphere.g", true);
    debug!("Loading {} quest sign sphere data files", path_files.len());
    let mut table = SphereQuestTable::default();
    for path in path_files {
        let Some(zone_id) = zone_id_from_path(&path) else {
            warn!("Unable to parse zoneId from {path}");
            continue;
        };
        let contents = files.read_to_string(&path);
        if contents.trim().is_empty() {
            warn!("{path} doesn't exists or is empty.");
            continue;
        }
        let origin = super::subzone::zone_origin_cell(worlds, zone_id);
        for s in parse_quest_sign_sphere(&contents, zone_id, &world_template.name, origin)
            .map_err(|e| format!("{path}: {e}"))?
        {
            table.push(s);
        }
    }
    Ok(table)
}

// ---------------------------------------------------------------------------------------------
// Triggers / starters
// ---------------------------------------------------------------------------------------------

/// What the sphere code needs from a `QuestComponentTemplate`
/// ([`super::hooks::QuestsBridge::quest_component`]).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct QuestComponentInfo {
    /// `ParentQuestTemplate.Id`.
    pub quest_id: u32,
    /// The component has a `QuestActConAcceptSphere` act.
    pub accept_sphere: bool,
}

#[derive(Debug, Default)]
struct TriggerState {
    /// `LastCheckLocation`.
    last_check: [f32; 3],
    /// `LastTick` (`None` = `DateTime.MinValue`).
    last_tick: Option<Instant>,
}

/// `SphereQuestTrigger` (per player and quest).
#[derive(Debug)]
pub struct SphereQuestTrigger {
    pub sphere: SphereQuest,
    pub owner: PlayerRef,
    /// `Quest.TemplateId`.
    pub quest_template_id: u32,
    /// `NpcTemplate` (non-zero: the check is centred on nearby NPCs).
    pub npc_template: u32,
    /// `TickRate` (ms).
    pub tick_rate: i32,
    state: Mutex<TriggerState>,
}

impl SphereQuestTrigger {
    pub fn new(
        sphere: SphereQuest,
        owner: PlayerRef,
        quest_template_id: u32,
        npc_template: u32,
        tick_rate: i32,
    ) -> Self {
        Self {
            sphere,
            owner,
            quest_template_id,
            npc_template,
            tick_rate,
            state: Mutex::new(TriggerState::default()),
        }
    }

    fn state(&self) -> MutexGuard<'_, TriggerState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// `Tick(delta)`.
    pub fn tick(&self, server: &GameServer, world: &WorldInstance) {
        let due = self.tick_rate > 0
            && self
                .state()
                .last_tick
                .is_none_or(|t| t.elapsed().as_secs_f64() * 1000.0 > f64::from(self.tick_rate));
        if !due {
            return;
        }
        let owner_obj = WorldObject::from(&self.owner);
        let p = world.world_pos_rot(&owner_obj);
        let owner_pos = [p.x, p.y, p.z];
        let data = &server.game_data.systems.npcs.sphere;
        let hook = server.systems.npcs.hooks.quests.get();
        let active = match self.sphere.db_sphere(data) {
            None => true,
            Some(s) => hook
                .as_ref()
                .is_none_or(|h| h.can_trigger_sphere(server, s.id, &owner_obj)),
        };
        if active {
            let last = self.state().last_check;
            let (mut old_inside, mut new_inside) = (false, false);
            if self.npc_template == 0 {
                old_inside = self.sphere.contains(last);
                new_inside = self.sphere.contains(owner_pos);
            } else {
                // Any NPC near the player (the template is not compared in C#).
                for (o, npc_pos) in
                    get_around_units(world, &owner_obj, self.sphere.radius * 1.5, false)
                {
                    if o.as_npc().is_none() {
                        continue;
                    }
                    if calculate_distance(npc_pos, last, true) <= self.sphere.radius {
                        old_inside = true;
                    }
                    if calculate_distance(npc_pos, owner_pos, true) <= self.sphere.radius {
                        new_inside = true;
                    }
                }
            }
            if let Some(h) = &hook {
                if !old_inside && new_inside {
                    h.on_enter_sphere(
                        server,
                        &self.owner,
                        self.quest_template_id,
                        &self.sphere,
                        last,
                    );
                } else if old_inside && !new_inside {
                    h.on_exit_sphere(
                        server,
                        &self.owner,
                        self.quest_template_id,
                        &self.sphere,
                        last,
                    );
                }
            }
        }
        let mut st = self.state();
        st.last_check = owner_pos;
        st.last_tick = Some(Instant::now());
    }
}

/// `SphereQuestStarter` (global quest starter sphere).
#[derive(Debug)]
pub struct SphereQuestStarter {
    pub sphere: SphereQuest,
    pub quest_template_id: u32,
    pub sphere_id: u32,
    /// `Region` (linked lazily).
    region: Mutex<Option<(i32, i32)>>,
}

/// `WorldInstance.SphereQuestManager`: the triggers and starters of one instance.
#[derive(Debug, Default)]
pub struct WorldSpheres {
    triggers: Mutex<Vec<Arc<SphereQuestTrigger>>>,
    add_queue: Mutex<Vec<Arc<SphereQuestTrigger>>>,
    remove_queue: Mutex<Vec<Arc<SphereQuestTrigger>>>,
    starters: Mutex<Vec<Arc<SphereQuestStarter>>>,
    basic: Mutex<Vec<Arc<SphereQuestStarter>>>,
    /// `_questStartingLastPositionChecks` (character id → position).
    last_positions: Mutex<HashMap<u32, [f32; 3]>>,
    linked: AtomicBool,
}

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

impl WorldSpheres {
    /// `GetSphereQuestTriggers()` (a snapshot).
    pub fn triggers(&self) -> Vec<Arc<SphereQuestTrigger>> {
        lock(&self.triggers).clone()
    }

    /// The starters that begin a quest when entered.
    pub fn starters(&self) -> Vec<Arc<SphereQuestStarter>> {
        lock(&self.starters).clone()
    }

    /// `_questSpheresBasic`.
    pub fn basic_starters(&self) -> Vec<Arc<SphereQuestStarter>> {
        lock(&self.basic).clone()
    }

    /// `AddSphereQuestTrigger(trigger)`.
    pub fn add_trigger(&self, trigger: Arc<SphereQuestTrigger>) {
        lock(&self.add_queue).push(trigger);
    }

    /// `RemoveSphereQuestTrigger(trigger)`.
    pub fn remove_trigger(&self, trigger: &Arc<SphereQuestTrigger>) {
        lock(&self.remove_queue).push(trigger.clone());
    }

    /// `RemoveSphereQuestTriggers(ownerId, questId)` (`questId` 0 = all of the player's).
    pub fn remove_triggers(&self, owner_character_id: u32, quest_id: u32) {
        for t in self.triggers() {
            if t.owner.character_id == owner_character_id
                && (quest_id == 0 || t.quest_template_id == quest_id)
            {
                self.remove_trigger(&t);
            }
        }
    }

    /// `Load()`'s starter linking (needs the quest components from the quests hook).
    fn link_starters(&self, server: &GameServer, table: &SphereQuestTable) {
        let Some(hook) = server.systems.npcs.hooks.quests.get() else {
            return;
        };
        let data = &server.game_data.systems.npcs.sphere;
        let mut new_starting = Vec::new();
        for (component_id, list) in table.entries() {
            let Some(component) = hook.quest_component(server, *component_id) else {
                continue;
            };
            let sphere_id = data.get_sphere_id_from_quest(component.quest_id);
            if sphere_id == 0 {
                continue;
            }
            for sphere in list {
                let s = Arc::new(SphereQuestStarter {
                    sphere: sphere.clone(),
                    quest_template_id: component.quest_id,
                    sphere_id,
                    region: Mutex::new(None),
                });
                lock(&self.basic).push(s.clone());
                if component.accept_sphere {
                    new_starting.push(s);
                }
            }
        }
        *lock(&self.starters) = new_starting;
        self.linked.store(true, Ordering::Release);
    }

    /// `Tick(delta)`.
    pub fn tick(&self, server: &GameServer, world: &WorldInstance) {
        // Add new player specific triggers (the C# duplicate check has no effect).
        {
            let mut q = lock(&self.add_queue);
            if !q.is_empty() {
                lock(&self.triggers).append(&mut q);
            }
        }
        for t in self.triggers() {
            if t.owner.region().is_some() {
                t.tick(server, world);
            }
        }
        let removed = std::mem::take(&mut *lock(&self.remove_queue));
        if !removed.is_empty() {
            let mut list = lock(&self.triggers);
            for r in &removed {
                if let Some(i) = list.iter().position(|t| Arc::ptr_eq(t, r)) {
                    list.remove(i);
                }
            }
        }
        // Global triggers for quest starters
        let data = &server.game_data.systems.npcs.sphere;
        let hook = server.systems.npcs.hooks.quests.get();
        for starter in self.starters() {
            let region = {
                let mut r = lock(&starter.region);
                if r.is_none() {
                    *r = world.region_by_pos(starter.sphere.xyz[0], starter.sphere.xyz[1]);
                }
                *r
            };
            let Some(region) = region else {
                continue;
            };
            if !region_has_player_activity(world, region) {
                continue;
            }
            let mut players: Vec<(u32, PlayerRef)> = Vec::new();
            for r in world.neighbors(region) {
                for o in world.objects_in_region(r) {
                    if let Some(p) = o.as_player() {
                        if players.iter().all(|(id, _)| *id != p.character_id) {
                            players.push((p.character_id, p.clone()));
                        }
                    }
                }
            }
            for (character_id, player) in players {
                let last = lock(&self.last_positions)
                    .get(&character_id)
                    .copied()
                    .unwrap_or_default();
                let obj = WorldObject::from(&player);
                let p = world.world_pos_rot(&obj);
                let new_pos = [p.x, p.y, p.z];
                let old_inside = starter.sphere.contains(last);
                let new_inside = starter.sphere.contains(new_pos);
                if !old_inside && new_inside {
                    if let Some(h) = &hook {
                        let can = match starter.sphere.db_sphere(data) {
                            None => true,
                            Some(s) => h.can_trigger_sphere(server, s.id, &obj),
                        };
                        if can {
                            h.on_enter_quest_starter_sphere(
                                server,
                                &player,
                                starter.quest_template_id,
                                starter.sphere_id,
                                &starter.sphere,
                                last,
                            );
                        }
                    }
                }
                lock(&self.last_positions).insert(character_id, new_pos);
            }
        }
    }
}

/// `server.systems.npcs.spheres`: the static sphere table + every instance's manager.
#[derive(Debug, Default)]
pub struct SphereQuestManager {
    /// `_sphereQuests` (static, loaded once).
    table: OnceLock<SphereQuestTable>,
    worlds: Mutex<HashMap<u32, Arc<WorldSpheres>>>,
}

impl SphereQuestManager {
    /// The loaded `_sphereQuests` (empty before [`start`]).
    pub fn table(&self) -> &SphereQuestTable {
        static EMPTY: OnceLock<SphereQuestTable> = OnceLock::new();
        self.table
            .get()
            .unwrap_or_else(|| EMPTY.get_or_init(SphereQuestTable::default))
    }

    /// Installs the sphere table (startup / tests); ignored once set (static in C#).
    pub fn set_table(&self, table: SphereQuestTable) -> bool {
        self.table.set(table).is_ok()
    }

    /// The `SphereQuestManager` of instance `world_id` (created on first use).
    pub fn for_world(&self, world_id: u32) -> Arc<WorldSpheres> {
        lock(&self.worlds).entry(world_id).or_default().clone()
    }

    fn all(&self) -> Vec<(u32, Arc<WorldSpheres>)> {
        let mut v: Vec<_> = lock(&self.worlds)
            .iter()
            .map(|(k, w)| (*k, w.clone()))
            .collect();
        v.sort_by_key(|(k, _)| *k);
        v
    }

    /// `GetQuestSpheres(componentId)`.
    pub fn get_quest_spheres(&self, component_id: u32) -> Option<&[SphereQuest]> {
        self.table().get(component_id)
    }

    /// `GetSpheresForQuest(questId)`.
    pub fn get_spheres_for_quest(&self, quest_id: u32) -> Vec<SphereQuest> {
        self.table().spheres_for_quest(quest_id)
    }

    /// Ticks every instance (also links the starters once the quests hook is installed).
    pub fn tick(&self, server: &GameServer) {
        for (world_id, spheres) in self.all() {
            let Some(world) = server.world.world(world_id) else {
                continue;
            };
            if !spheres.linked.load(Ordering::Acquire) {
                spheres.link_starters(server, self.table());
            }
            spheres.tick(server, &world);
        }
    }
}

/// `AddSphereQuestTriggers(owner, quest, componentId, npcTemplateId)`: one trigger per sphere
/// of the component in the owner's instance. Returns the number added.
pub fn add_sphere_quest_triggers(
    server: &GameServer,
    owner: &PlayerRef,
    quest_template_id: u32,
    component_id: u32,
    npc_template_id: u32,
) -> i32 {
    let m = &server.systems.npcs.spheres;
    let Some(spheres) = m.get_quest_spheres(component_id) else {
        return 0;
    };
    let instance_id = owner.data().unit.transform.instance_id;
    let w = m.for_world(instance_id);
    let mut res = 0;
    for sphere in spheres {
        w.add_trigger(Arc::new(SphereQuestTrigger::new(
            sphere.clone(),
            owner.clone(),
            quest_template_id,
            npc_template_id,
            500,
        )));
        res += 1;
    }
    res
}

/// `RemoveSphereQuestTriggers(ownerId, questId)` in every instance.
pub fn remove_sphere_quest_triggers(server: &GameServer, owner_character_id: u32, quest_id: u32) {
    for (_, w) in server.systems.npcs.spheres.all() {
        w.remove_triggers(owner_character_id, quest_id);
    }
}

/// `SphereGameData.IsInsideAreaSphere(sphereId, value2, worldPosition, requiredComponentId)`.
pub fn is_inside_area_sphere(
    server: &GameServer,
    sphere_id: u32,
    _value2: u32,
    world_position: [f32; 3],
    required_component_id: u32,
) -> Option<SphereQuest> {
    let data = &server.game_data.systems.npcs.sphere;
    let db_sphere = data.spheres.get(sphere_id)?;
    if db_sphere.sphere_detail_type != "SphereQuest" {
        return None;
    }
    let db_sphere_quest = data.sphere_quests.get(db_sphere.sphere_detail_id)?;
    let pak_spheres = server
        .systems
        .npcs
        .spheres
        .get_spheres_for_quest(db_sphere_quest.quest_id);
    let required = if required_component_id > 0 {
        server
            .systems
            .npcs
            .hooks
            .quests
            .get()
            .and_then(|h| h.quest_component(server, required_component_id))
    } else {
        None
    };
    pak_spheres.into_iter().find(|s| {
        let mut matches = required_component_id == 0 || s.component_id == required_component_id;
        if !matches
            && db_sphere.trigger_condition_id == trigger_condition::TRIGGER_EVERY_N_TIME_AFTER
            && required.is_some_and(|c| c.quest_id == db_sphere_quest.quest_id)
        {
            matches = true;
        }
        s.contains(world_position) && matches
    })
}

/// Startup: `SphereQuestManager.Load` (the static table from the first instance's template,
/// i.e. main_world) + `Initialize` (500 ms tick).
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    let m = &server.systems.npcs.spheres;
    let template = server
        .world
        .worlds()
        .into_iter()
        .min_by_key(|w| w.id)
        .map(|w| w.template.clone());
    if let (Some(template), Some(files)) = (template, server.systems.npcs.heightmaps.client_files())
    {
        let game_data = server.game_data.clone();
        let table = tokio::task::spawn_blocking(move || {
            load_quest_spheres(&files, &game_data.worlds, &template)
        })
        .await?
        .map_err(|e| {
            error!("Loading SphereQuest error! {e}");
            anyhow::anyhow!(e)
        })?;
        info!("Loaded {} quest spheres", table.sphere_count());
        m.set_table(table);
    }
    for w in server.world.worlds() {
        let spheres = m.for_world(w.id);
        spheres.link_starters(server, m.table());
    }
    let weak: Weak<GameServer> = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut interval = tokio::time::interval(TICK_DELAY);
        interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        loop {
            interval.tick().await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            server.systems.npcs.spheres.tick(&server);
        }
    });
    Ok(())
}

/// C->S packets (none).
pub fn register(_reg: &mut PacketRegistry) {}

#[cfg(test)]
mod tests {
    use super::*;

    const FILE: &str = "area 1\r\n  qtype 1234\r\n\tctype 5678\r\npos (x 10.5, y 20, z 30)\r\nradius 12.5\r\nnoise\nArea 2\nqtype 7\nctype 8\npos (1,2)\nradius 3\n";

    #[test]
    fn parses_sign_sphere_blocks() {
        let s = parse_quest_sign_sphere(FILE, 129, "main_world", (9, 14)).unwrap();
        assert_eq!(s.len(), 2);
        assert_eq!(
            (s[0].quest_id, s[0].component_id, s[0].radius),
            (1234, 5678, 12.5)
        );
        assert_eq!(s[0].xyz, [9.0 * 1024.0 + 10.5, 14.0 * 1024.0 + 20.0, 30.0]);
        assert_eq!(s[0].zone_id, 129);
        // "pos (1,2)": not 3 values -> Xyz stays zero before the zone offset
        assert_eq!(s[1].xyz, [9.0 * 1024.0, 14.0 * 1024.0, 0.0]);
        assert!(s[0].contains([9.0 * 1024.0 + 10.5, 14.0 * 1024.0 + 20.0, 42.5]));
        assert!(!s[0].contains([9.0 * 1024.0 + 10.5, 14.0 * 1024.0 + 20.0, 42.6]));
        // a bad number rethrows
        assert!(parse_quest_sign_sphere(
            "area\nqtype x\nctype 1\npos 1,2,3\nradius 1\n",
            1,
            "w",
            (0, 0)
        )
        .is_err());
        // a short qtype line (Substring(6) throws)
        assert!(parse_quest_sign_sphere(
            "area\nqtype\nctype 1\npos 1,2,3\nradius 1\n",
            1,
            "w",
            (0, 0)
        )
        .is_err());
        // the loop runs while i < Count - 4: a block needs its 5 lines
        assert_eq!(
            parse_quest_sign_sphere(
                "area\nqtype 1\nctype 1\npos 1,2,3\nradius 1",
                1,
                "w",
                (0, 0)
            )
            .unwrap()
            .len(),
            1
        );
        assert!(
            parse_quest_sign_sphere("area\nqtype 1\nctype 1\npos 1,2,3", 1, "w", (0, 0))
                .unwrap()
                .is_empty()
        );
        assert_eq!(
            zone_id_from_path(
                "game/worlds/main_world/level_design/zone/129/client/quest_sign_sphere.g"
            ),
            Some(129)
        );
    }

    #[test]
    fn sphere_id_from_quest_and_table() {
        let mut d = SphereData::default();
        d.sphere_quests.add(
            10,
            SphereQuests {
                id: 10,
                quest_id: 4706,
                quest_trigger_id: 3,
            },
        );
        d.sphere_quests.add(
            11,
            SphereQuests {
                id: 11,
                quest_id: 4706,
                quest_trigger_id: 3,
            },
        );
        d.spheres.add(
            1,
            Sphere {
                id: 1,
                sphere_detail_id: 11,
                sphere_detail_type: "SphereQuest".into(),
                ..Default::default()
            },
        );
        d.spheres.add(
            2,
            Sphere {
                id: 2,
                sphere_detail_id: 10,
                sphere_detail_type: "SphereBuff".into(),
                ..Default::default()
            },
        );
        assert_eq!(d.get_sphere_id_from_quest(4706), 1);
        assert_eq!(d.get_sphere_id_from_quest(1), 0);
        assert!(!d.spheres.add(1, Sphere::default()));
        let mut t = SphereQuestTable::default();
        for (c, q) in [(5, 1), (6, 2), (5, 2)] {
            t.push(SphereQuest {
                component_id: c,
                quest_id: q,
                ..Default::default()
            });
        }
        assert_eq!(t.get(5).unwrap().len(), 2);
        assert_eq!(t.spheres_for_quest(2).len(), 2);
        assert_eq!(t.sphere_count(), 3);
    }

    #[test]
    fn distance_uses_double_sums() {
        assert_eq!(
            calculate_distance([0.0, 0.0, 0.0], [3.0, 4.0, 12.0], true),
            13.0
        );
        assert_eq!(
            calculate_distance([0.0, 0.0, 0.0], [3.0, 4.0, 12.0], false),
            5.0
        );
    }
}

#[cfg(test)]
mod real_data_tests {
    //! `AAEMU_COMPACT_DB=... [AAEMU_GAME_PAK=...] cargo test -p aaemu-game real_sphere --
    //! --ignored --nocapture`.
    use rusqlite::{Connection, OpenFlags};

    use super::*;

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_sphere_tables() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let files = ClientFileManager::new();
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = SphereData::load(&ctx).unwrap();
        let rows = |t: &str| -> usize {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        assert_eq!(d.spheres.len(), rows("spheres"));
        assert_eq!(d.sphere_quests.len(), rows("sphere_quests"));
        assert_eq!(d.sphere_skills.len(), rows("sphere_skills"));
        assert_eq!(d.sphere_sounds.len(), rows("sphere_sounds"));
        assert_eq!(
            d.sphere_doodad_interacts.len(),
            rows("sphere_doodad_interacts")
        );
        assert_eq!(d.sphere_chat_bubbles.len(), rows("sphere_chat_bubbles"));
        assert_eq!(d.sphere_buffs.len(), rows("sphere_buffs"));
        assert_eq!(d.sphere_bubbles.len(), rows("sphere_bubbles"));
        assert_eq!(d.sphere_accept_quests.len(), rows("sphere_accept_quests"));
        assert_eq!(
            d.sphere_accept_quest_quests.len(),
            rows("sphere_accept_quest_quests")
        );
        assert!(d.get_sphere_id_from_quest(4706) > 0);
        eprintln!(
            "spheres {} sphere_quests {} chat bubbles {}",
            d.spheres.len(),
            d.sphere_quests.len(),
            d.sphere_chat_bubbles.len()
        );

        // quest_sign_sphere.g of main_world from the client pak
        let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
            return;
        };
        let app = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../AAEmu.Game");
        let files = ClientFileManager::from_sources(&[pak], &app);
        let worlds = WorldTemplates::load(&files, &app).unwrap();
        let main = worlds.get_world_template_by_name("main_world").unwrap();
        let table = load_quest_spheres(&files, &worlds, main).unwrap();
        let with_db = table
            .entries()
            .iter()
            .flat_map(|(_, v)| v)
            .filter(|s| d.get_sphere_id_from_quest(s.quest_id) > 0)
            .count();
        eprintln!(
            "quest spheres {} in {} components ({with_db} with a SphereQuest db sphere)",
            table.sphere_count(),
            table.entries().len()
        );
        assert!(table.sphere_count() > 0);
    }
}
