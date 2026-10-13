//! `SphereQuestManager` (quest sign spheres from `quest_sign_sphere.g`, per-player quest
//! sphere triggers, quest-starting spheres, the 500 ms tick), `SphereQuest` /
//! `SphereQuestTrigger` / `SphereQuestStarter` and the sphere part of `SphereGameData`
//! (`IsInsideAreaSphere`, `GetSpheresForQuest`).
//!
//! The C# has one manager per `WorldInstance` (with a static sphere list loaded from the first
//! world); only `main_world` exists here, so there is one manager for the server.

use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};

use super::data::{QuestData, RawSignSphere};
use super::model::sphere_trigger;
use super::templates::ActKind;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// `SphereQuest` (one `quest_sign_sphere.g` area, world coordinates).
#[derive(Debug, Clone, PartialEq)]
pub struct SphereQuest {
    pub zone_id: u32,
    pub world_id: String,
    pub quest_id: u32,
    pub component_id: u32,
    pub radius: f32,
    pub xyz: (f32, f32, f32),
}

impl SphereQuest {
    /// `Contains(pos)`: 3D distance (computed in double) <= radius.
    pub fn contains(&self, pos: (f32, f32, f32)) -> bool {
        distance3(self.xyz, pos) <= self.radius
    }
}

/// `MathUtil.CalculateDistance(a, b, true)`.
pub fn distance3(a: (f32, f32, f32), b: (f32, f32, f32)) -> f32 {
    let dx = f64::from(a.0) - f64::from(b.0);
    let dy = f64::from(a.1) - f64::from(b.1);
    let dz = f64::from(a.2) - f64::from(b.2);
    (dx * dx + dy * dy + dz * dz).sqrt() as f32
}

/// `SphereQuestTrigger`.
#[derive(Debug, Clone, PartialEq)]
pub struct SphereQuestTrigger {
    /// Identity (the C# compares the objects).
    pub id: u64,
    pub sphere: SphereQuest,
    /// `Owner.Id`.
    pub character_id: u32,
    /// `Quest.TemplateId`.
    pub quest_template_id: u32,
    pub npc_template: u32,
    pub last_check_location: (f32, f32, f32),
    pub tick_rate: i32,
    /// `LastTick` (unix ms, `DateTime.MinValue` = i64::MIN).
    pub last_tick_ms: i64,
}

/// `SphereQuestStarter`.
#[derive(Debug, Clone, PartialEq)]
pub struct SphereQuestStarter {
    pub sphere: SphereQuest,
    pub quest_template_id: u32,
    pub sphere_id: u32,
}

#[derive(Debug, Default)]
struct Spheres {
    /// `_sphereQuests`: component id -> spheres (dictionary insertion order).
    by_component: Vec<(u32, Vec<SphereQuest>)>,
    /// `_questStartingSpheres`.
    starters: Vec<SphereQuestStarter>,
    /// `_questSpheresBasic`.
    basic: Vec<SphereQuestStarter>,
}

#[derive(Debug, Default)]
struct Triggers {
    list: Vec<SphereQuestTrigger>,
    add_queue: Vec<SphereQuestTrigger>,
    remove_queue: Vec<u64>,
    next_id: u64,
}

/// `SphereQuestManager`.
#[derive(Debug, Default)]
pub struct SphereQuestManager {
    spheres: OnceLock<Spheres>,
    triggers: Mutex<Triggers>,
    /// `_questStartingLastPositionChecks` (character id -> position).
    last_positions: Mutex<HashMap<u32, (f32, f32, f32)>>,
    /// Serializes ticks.
    tick_lock: Mutex<()>,
}

impl SphereQuestManager {
    /// `LoadQuestSpheres` (zone-local -> world coordinates with the zone origin cells) +
    /// `Load` (quest starters). `origin(zone)` is `ZoneManager.GetZoneOriginCell`.
    pub fn load(
        &self,
        data: &QuestData,
        raw: &[RawSignSphere],
        world_name: &str,
        origin: impl Fn(u32) -> (i32, i32),
    ) {
        let mut s = Spheres::default();
        for r in raw {
            let (ox, oy) = origin(r.zone_id);
            let sphere = SphereQuest {
                zone_id: r.zone_id,
                world_id: world_name.to_string(),
                quest_id: r.quest_id,
                component_id: r.component_id,
                radius: r.radius,
                xyz: (
                    ox as f32 * 1024.0 + r.local[0],
                    oy as f32 * 1024.0 + r.local[1],
                    r.local[2],
                ),
            };
            match s
                .by_component
                .iter_mut()
                .find(|(c, _)| *c == r.component_id)
            {
                Some((_, v)) => v.push(sphere),
                None => s.by_component.push((r.component_id, vec![sphere])),
            }
        }
        // Link quest starters to spheres
        for (component_id, list) in &s.by_component {
            let Some(component) = data.component(*component_id) else {
                continue;
            };
            let sphere_id = data.sphere_id_from_quest(component.quest_id);
            if sphere_id == 0 {
                continue;
            }
            for sphere in list {
                let starter = SphereQuestStarter {
                    sphere: sphere.clone(),
                    quest_template_id: component.quest_id,
                    sphere_id,
                };
                s.basic.push(starter.clone());
                for &a in &component.acts {
                    if matches!(data.acts[a].kind, ActKind::ConAcceptSphere { .. }) {
                        s.starters.push(starter.clone());
                    }
                }
            }
        }
        tracing::info!(
            "Loaded {} quest sign spheres ({} starters)",
            raw.len(),
            s.starters.len()
        );
        let _ = self.spheres.set(s);
    }

    fn spheres(&self) -> Option<&Spheres> {
        self.spheres.get()
    }

    /// `GetQuestSpheres(componentId)`.
    pub fn quest_spheres(&self, component_id: u32) -> Vec<SphereQuest> {
        self.spheres()
            .and_then(|s| s.by_component.iter().find(|(c, _)| *c == component_id))
            .map(|(_, v)| v.clone())
            .unwrap_or_default()
    }

    /// `GetSpheresForQuest(questId)`.
    pub fn spheres_for_quest(&self, quest_id: u32) -> Vec<SphereQuest> {
        self.spheres()
            .map(|s| {
                s.by_component
                    .iter()
                    .flat_map(|(_, v)| v.iter())
                    .filter(|x| x.quest_id == quest_id)
                    .cloned()
                    .collect()
            })
            .unwrap_or_default()
    }

    /// The quest-starting spheres.
    pub fn starters(&self) -> Vec<SphereQuestStarter> {
        self.spheres()
            .map(|s| s.starters.clone())
            .unwrap_or_default()
    }

    /// `SphereGameData.IsInsideAreaSphere(sphereId, value2, worldPosition, requiredComponentId)`.
    pub fn is_inside_area_sphere(
        &self,
        data: &QuestData,
        sphere_id: u32,
        _value2: u32,
        pos: (f32, f32, f32),
        required_component_id: u32,
    ) -> Option<SphereQuest> {
        let db = data.spheres.get(&sphere_id)?;
        if db.sphere_detail_type != "SphereQuest" {
            return None;
        }
        let sq = data.sphere_quests.get(&db.sphere_detail_id)?;
        let required_quest = (required_component_id > 0)
            .then(|| data.component(required_component_id).map(|c| c.quest_id))
            .flatten();
        for s in self.spheres_for_quest(sq.quest_id) {
            let mut matches = required_component_id == 0 || s.component_id == required_component_id;
            if !matches
                && db.trigger_condition_id == sphere_trigger::TRIGGER_EVERY_N_TIME_AFTER
                && required_quest == Some(sq.quest_id)
            {
                matches = true;
            }
            if s.contains(pos) && matches {
                return Some(s);
            }
        }
        None
    }

    /// `AddSphereQuestTriggers(owner, quest, componentId, npcTemplateId)`: queued for the next
    /// tick. Returns the number of triggers.
    pub fn add_triggers(
        &self,
        character_id: u32,
        quest_template_id: u32,
        component_id: u32,
        npc_template: u32,
    ) -> usize {
        let spheres = self.quest_spheres(component_id);
        let mut t = self.triggers.lock().expect("triggers poisoned");
        for sphere in &spheres {
            if t.list.iter().chain(&t.add_queue).any(|x| {
                x.character_id == character_id
                    && x.quest_template_id == quest_template_id
                    && x.sphere == *sphere
                    && x.npc_template == npc_template
                    && !t.remove_queue.contains(&x.id)
            }) {
                continue;
            }
            t.next_id += 1;
            let id = t.next_id;
            t.add_queue.push(SphereQuestTrigger {
                id,
                sphere: sphere.clone(),
                character_id,
                quest_template_id,
                npc_template,
                last_check_location: (0.0, 0.0, 0.0),
                tick_rate: 500,
                last_tick_ms: i64::MIN,
            });
        }
        spheres.len()
    }

    /// `RemoveSphereQuestTriggers(ownerId, questId)` (0 = all of the owner): queues the
    /// matching triggers of the *live* list (triggers still in the add queue are not found).
    pub fn remove_triggers(&self, character_id: u32, quest_id: u32) {
        let mut t = self.triggers.lock().expect("triggers poisoned");
        t.add_queue.retain(|x| {
            !(x.character_id == character_id && (quest_id == 0 || x.quest_template_id == quest_id))
        });
        let ids: Vec<u64> = t
            .list
            .iter()
            .filter(|x| {
                x.character_id == character_id && (quest_id == 0 || x.quest_template_id == quest_id)
            })
            .map(|x| x.id)
            .collect();
        t.remove_queue.extend(ids);
    }

    /// `GetSphereQuestTriggers()` (live list).
    pub fn triggers(&self) -> Vec<SphereQuestTrigger> {
        self.triggers
            .lock()
            .expect("triggers poisoned")
            .list
            .clone()
    }

    /// One `Tick` (every 500 ms). A throwing quest event handler aborts the rest of the tick,
    /// like the C# exception caught around the whole tick.
    pub fn tick(&self, server: &GameServer) {
        let _serial = self.tick_lock.lock().expect("tick poisoned");
        let now = chrono::Utc::now().timestamp_millis();
        // Add new player specific triggers
        let list = {
            let mut t = self.triggers.lock().expect("triggers poisoned");
            let remove = std::mem::take(&mut t.remove_queue);
            t.list.retain(|x| !remove.contains(&x.id));
            let added = std::mem::take(&mut t.add_queue);
            t.list.extend(added);
            t.list.clone()
        };
        // Handle player specific Triggers
        for trigger in list {
            let Some(player) = server.world.character_by_id(trigger.character_id) else {
                continue;
            };
            if player.region().is_none() {
                continue;
            }
            match tick_trigger(server, &player, &trigger, now) {
                Ok(Some(last)) => {
                    let mut t = self.triggers.lock().expect("triggers poisoned");
                    if let Some(x) = t.list.iter_mut().find(|x| x.id == trigger.id) {
                        x.last_check_location = last;
                        x.last_tick_ms = now;
                    }
                }
                Ok(None) => {}
                Err(e) => {
                    tracing::error!("Error in SphereQuestTrigger tick ! {e}");
                    return;
                }
            }
        }
        // Remove player specific triggers
        {
            let mut t = self.triggers.lock().expect("triggers poisoned");
            let remove = std::mem::take(&mut t.remove_queue);
            for id in remove {
                if let Some(i) = t.list.iter().position(|x| x.id == id) {
                    t.list.remove(i);
                }
            }
        }
        // Handle Global triggers for quest starters
        let Some(world) = server.world.world(0) else {
            return;
        };
        let mut current_positions = HashMap::new();
        for starter in self.starters() {
            let (sx, sy, _) = starter.sphere.xyz;
            let Some(region) = world.region_by_pos(sx, sy) else {
                continue;
            };
            let has_players = world
                .objects_in_region(region)
                .iter()
                .any(|o| o.as_player().is_some());
            if !has_players {
                continue;
            }
            let mut players: Vec<PlayerRef> = Vec::new();
            for n in world.neighbors(region) {
                for o in world.objects_in_region(n) {
                    if let Some(p) = o.as_player() {
                        if !players.iter().any(|x| x.character_id == p.character_id) {
                            players.push(p.clone());
                        }
                    }
                }
            }
            for p in players {
                let pos = {
                    let c = p.data();
                    let t = &c.unit.transform;
                    (t.x, t.y, t.z)
                };
                let last = self
                    .last_positions
                    .lock()
                    .expect("positions poisoned")
                    .get(&p.character_id)
                    .copied()
                    .unwrap_or((0.0, 0.0, 0.0));
                let old_inside = starter.sphere.contains(last);
                let new_inside = starter.sphere.contains(pos);
                if !old_inside && new_inside {
                    let data = &server.game_data.systems.quests;
                    let db_sphere = data
                        .spheres
                        .get(&data.sphere_id_from_quest(starter.sphere.quest_id));
                    let can = match db_sphere {
                        None => true,
                        Some(db) => {
                            let (id, or) = (db.id, db.or_unit_reqs);
                            super::events::with_quests(server, &p, None, move |ctx| {
                                ctx.can_trigger_sphere(id, or)
                            })
                            .unwrap_or(false)
                        }
                    };
                    if can {
                        super::events::on_enter_quest_starter_sphere(
                            server,
                            &p,
                            starter.quest_template_id,
                            starter.sphere_id,
                        );
                    }
                }
                current_positions.insert(p.character_id, pos);
            }
        }
        self.last_positions
            .lock()
            .expect("positions poisoned")
            .extend(current_positions);
    }
}

/// `SphereQuestTrigger.Tick`: returns the new `LastCheckLocation` when the trigger ran.
fn tick_trigger(
    server: &GameServer,
    player: &PlayerRef,
    trigger: &SphereQuestTrigger,
    now: i64,
) -> Result<Option<(f32, f32, f32)>, String> {
    let elapsed = if trigger.last_tick_ms == i64::MIN {
        f64::INFINITY
    } else {
        (now - trigger.last_tick_ms) as f64
    };
    if !(trigger.tick_rate > 0 && elapsed > f64::from(trigger.tick_rate)) {
        return Ok(None);
    }
    let data = &server.game_data.systems.quests;
    let db_sphere = data
        .spheres
        .get(&data.sphere_id_from_quest(trigger.sphere.quest_id))
        .map(|d| (d.id, d.or_unit_reqs));
    let mut env = super::events::capture_env(server, player);
    if trigger.npc_template > 0 {
        env.nearby_npcs = super::events::nearby_npcs(server, player);
    }
    let sphere = trigger.sphere.clone();
    let last = trigger.last_check_location;
    let npc_template = trigger.npc_template;
    let r = super::events::with_quests(server, player, Some(env), move |ctx| {
        let active = match db_sphere {
            None => true,
            Some((id, or)) => ctx.can_trigger_sphere(id, or),
        };
        let pos = ctx.env.position;
        if active {
            let (old_inside, new_inside) = if npc_template == 0 {
                // Normal distance check
                (sphere.contains(last), sphere.contains(pos))
            } else {
                // Using NPC Template, find nearby NPCs with it first (GetAround radius 1.5x)
                let r = sphere.radius * 1.5;
                let mut old_inside = false;
                let mut new_inside = false;
                for &(tid, x, y, z) in &ctx.env.nearby_npcs {
                    if tid != npc_template {
                        continue;
                    }
                    let (dx, dy) = (x - pos.0, y - pos.1);
                    if dx * dx + dy * dy >= r * r {
                        continue;
                    }
                    if distance3((x, y, z), last) <= sphere.radius {
                        old_inside = true;
                    }
                    if distance3((x, y, z), pos) <= sphere.radius {
                        new_inside = true;
                    }
                }
                (old_inside, new_inside)
            };
            let ev = if !old_inside && new_inside {
                Some(super::events::QuestEvent::EnterSphere { sphere })
            } else if old_inside && !new_inside {
                Some(super::events::QuestEvent::ExitSphere { sphere })
            } else {
                None
            };
            if let Some(ev) = ev {
                ctx.dispatch(&ev).map_err(|e| e.0)?;
            }
        }
        Ok(pos)
    });
    match r {
        Some(Ok(pos)) => Ok(Some(pos)),
        Some(Err(e)) => Err(e),
        None => Ok(None),
    }
}

impl super::engine::Ctx<'_> {
    /// `UnitRequirementsGameData.CanTriggerSphere(sphere, Owner)`.
    pub fn can_trigger_sphere(&self, sphere_id: u32, or_unit_reqs: bool) -> bool {
        let owner = super::engine::OwnerView {
            c: self.c,
            st: self.st,
            server: self.server,
        };
        let env = super::engine::EnvView {
            server: self.server,
            env: &self.env,
        };
        match &self.env.target {
            Some(t) => self.data.unit_reqs.can_trigger_sphere(
                sphere_id,
                or_unit_reqs,
                &owner,
                &super::engine::TargetView(t, self.server),
                &env,
            ),
            None => self.data.unit_reqs.can_trigger_sphere(
                sphere_id,
                or_unit_reqs,
                &owner,
                &owner,
                &env,
            ),
        }
    }
}
