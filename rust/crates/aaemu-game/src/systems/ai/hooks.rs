//! How the rest of the server reaches the AI:
//!
//! * [`AiHooks`] (`server.systems.hooks.ai`): the `npc.Ai` call sites of the NPC subsystem
//!   (`NpcManager.Create`, `NpcSpawnerNpc.Spawn`, `Npc.DoDie`, `NpcSpawner.DoSpawnEffect`) and
//!   of the skills subsystem (`Unit.SetFaction`'s NPC tail);
//! * the AI parts of the npcs `NpcSkillHooks` (called directly by
//!   `systems::npcs::bridges::NpcSkills`): `Npc.OnDamageReceived`, `npc.Ai?.OnNoAggroTarget()`,
//!   `Npc.CheckIfEmptyAggroToReturn`, `Npc.DoDie` (aggro clean-up + `Ai.GoToDead()`),
//!   `CurrentGameStance` / `CurrentAlertness`, and the `NpcControlEffect`;
//! * [`AiAStarHooks`]: the AStar GM sub commands (`services` `ASTAR_HOOKS`).

use std::sync::{Arc, Mutex};

use crate::server::GameServer;
use crate::systems::skills::state::AggroKind;
use crate::world::{NpcRef, PlayerRef, WorldObject};

use super::framework::AiKind;
use super::manager::post;
use super::params::{AiCommandCategory, AiParamType};
use super::simulation::{self, Simulation};

/// The AI entry points other subsystems call (installed into `server.systems.hooks.ai`).
pub trait AiHooks: Send + Sync {
    /// `NpcManager.Create`: `if (Template.AiFileId > 0) { npc.Ai = AiUtils.GetAiByType(...);
    /// AIManager.AddAi(ai); ai.Start(); }`. Returns `npc.Ai != null`.
    fn npc_created(&self, _server: &GameServer, _npc: &NpcRef, _ai_file_id: i32) -> bool {
        false
    }

    /// `NpcSpawnerNpc.Spawn` before `npc.Spawn()`: `Ai.HomePosition = Ai.IdlePosition =
    /// world position; Ai.GoToSpawn()`.
    fn npc_spawn_positioned(&self, _server: &GameServer, _npc: &NpcRef) {}

    /// `NpcSpawnerNpc.Spawn` after `npc.Spawn()`: `RegisterNpcEvents`, `npc.Simulation = new
    /// Simulation(npc)`, `Ai.LoadAiPathPoints(FollowPath)`.
    fn npc_spawned(&self, _server: &GameServer, _npc: &NpcRef, _follow_path: &str) {}

    /// `npc.Ai.Owner.AddUnitAggro(Damage, unit, 1); npc.Ai.OnAggroTargetChanged()`.
    fn npc_engage(&self, _server: &GameServer, _npc: &NpcRef, _unit: &WorldObject) {}

    /// The end of `Npc.DoDie`: `ClearAllAggroTargetsAndCheckCombatState()` + `Ai?.GoToDead()`.
    fn npc_died(&self, _server: &GameServer, _npc: &NpcRef) {}

    /// `Npc.Delete()`: the AI is detached.
    fn npc_deleted(&self, _server: &GameServer, _obj_id: u32) {}

    /// `Unit.SetFaction` NPC tail (quest 2486): the NPC attacks the characters within 5 m it can
    /// attack.
    fn npc_faction_set(&self, _server: &GameServer, _npc: &NpcRef) {}
}

/// The installed implementation.
#[derive(Debug, Default)]
pub struct AiSubsystemHooks;

impl AiHooks for AiSubsystemHooks {
    fn npc_created(&self, server: &GameServer, npc: &NpcRef, ai_file_id: i32) -> bool {
        create_ai(server, npc, ai_file_id)
    }

    fn npc_spawn_positioned(&self, server: &GameServer, npc: &NpcRef) {
        spawn_positioned(server, npc);
    }

    fn npc_spawned(&self, server: &GameServer, npc: &NpcRef, follow_path: &str) {
        spawned(server, npc, follow_path);
    }

    fn npc_engage(&self, server: &GameServer, npc: &NpcRef, unit: &WorldObject) {
        engage(server, npc, unit);
    }

    fn npc_died(&self, server: &GameServer, npc: &NpcRef) {
        on_npc_died(server, npc);
    }

    fn npc_deleted(&self, server: &GameServer, obj_id: u32) {
        server.systems.ai.ais.remove(obj_id);
        server.systems.ai.simulations.remove(&obj_id);
    }

    fn npc_faction_set(&self, server: &GameServer, npc: &NpcRef) {
        faction_set(server, npc);
    }
}

/// `NpcManager.Create`'s AI part.
pub fn create_ai(server: &GameServer, npc: &NpcRef, ai_file_id: i32) -> bool {
    if ai_file_id <= 0 {
        npc.data().has_ai = false;
        return false;
    }
    let Some(kind) = AiKind::by_type(AiParamType::from_u32(ai_file_id as u32)) else {
        npc.data().has_ai = false;
        return false;
    };
    server.systems.ai.ais.add_ai(npc, kind);
    // `NpcTemplate.ActivateAiAlways`: tick this AI even without player activity nearby.
    let always = server
        .game_data
        .systems
        .npcs
        .npc
        .template(npc.data().unit.template_id)
        .is_some_and(|t| t.activate_ai_always);
    if always {
        server.systems.ai.ais.mark_always_active(npc.obj_id);
    }
    true
}

/// `NpcSpawnerNpc.Spawn`: home / idle position and `GoToSpawn()`.
pub fn spawn_positioned(server: &GameServer, npc: &NpcRef) {
    let obj = WorldObject::from(npc);
    let pos = super::npc::world_pos(server, &obj);
    let spawner_z = server
        .systems
        .npcs
        .spawns
        .npc_spawner_of(npc.obj_id)
        .map(|s| s.position().z);
    npc.data().spawner_z = spawner_z;
    post(server, npc.obj_id, move |c| {
        // RegisterNpcEvents() runs before the home position / GoToSpawn()
        c.ai.events_registered = true;
        c.ai.home_position = pos;
        c.ai.idle_position = pos;
        c.go_to_spawn();
    });
}

/// `NpcSpawnerNpc.Spawn` after `npc.Spawn()`.
pub fn spawned(server: &GameServer, npc: &NpcRef, follow_path: &str) {
    let folder = server.systems.ai.paths_folder();
    server
        .systems
        .ai
        .simulations
        .insert(npc.obj_id, Arc::new(Mutex::new(Simulation::new(folder))));
    let path = follow_path.to_string();
    let id = npc.obj_id;
    let template_id = npc.data().unit.template_id;
    post(server, npc.obj_id, move |c| {
        c.ai.has_simulation = true;
        if !path.trim().is_empty() && !c.load_ai_path_points(&path, false) {
            tracing::warn!("Failed to load {path} for NPC {template_id} ({id})");
        }
    });
}

/// `npc.Ai.Owner.AddUnitAggro(Damage, unit, 1); npc.Ai.OnAggroTargetChanged()`.
pub fn engage(server: &GameServer, npc: &NpcRef, unit: &WorldObject) {
    if !super::manager::has_ai(server, npc.obj_id) {
        tracing::error!(
            "npc {} has no AI (NullReferenceException in C#)",
            npc.obj_id
        );
        return;
    }
    super::combat::add_unit_aggro(server, &WorldObject::from(npc), AggroKind::Damage, unit, 1);
    post(server, npc.obj_id, |c| c.on_aggro_target_changed());
}

/// The AI part of `Npc.DoDie`.
pub fn on_npc_died(server: &GameServer, npc: &NpcRef) {
    let obj = WorldObject::from(npc);
    super::combat::clear_all_aggro_targets_and_check_combat_state(server, &obj);
    super::npc::set_current_aggro_target(npc, None);
    post(server, npc.obj_id, |c| c.go_to_dead());
}

/// `Unit.SetFaction`'s NPC tail.
pub fn faction_set(server: &GameServer, npc: &NpcRef) {
    let obj = WorldObject::from(npc);
    let around = super::npc::around(server, &obj, 5.0, false);
    for c in around.into_iter().filter(|o| o.as_player().is_some()) {
        if !super::combat::can_attack(server, &obj, &c) {
            continue;
        }
        tracing::info!(
            "SetFaction: npc={} attack the character={}",
            npc.obj_id,
            c.obj_id()
        );
        engage(server, npc, &c);
    }
}

/// `CheckIfEmptyAggroToReturn(unit)` on the removed abuser (when it is an NPC).
pub fn check_abuser_empty_aggro(server: &GameServer, abuser_id: u32) {
    let Some(abuser) = server.world.object(abuser_id) else {
        return;
    };
    let Some(n) = abuser.as_npc() else { return };
    if !n.data().unit.skill_state.aggro_table.is_empty() {
        return;
    }
    if super::manager::has_ai(server, n.obj_id) {
        post(
            server,
            n.obj_id,
            super::combat::check_if_empty_aggro_to_return,
        );
    } else {
        super::combat::set_in_battle(server, &abuser, false);
    }
}

/// `TalkBehavior.SpawnSpyglassIfNeeded`: `new DoodadSpawner { UnitId, Position, ParentWorld
/// }.Spawn(0)`.
pub fn spawn_spyglass(
    server: &GameServer,
    npc: &WorldObject,
    unit_id: u32,
    x: f32,
    y: f32,
    z: f32,
    yaw: f32,
) {
    use crate::data::world_templates::WorldSpawnPosition;
    use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
    let t = npc.transform();
    let def = DoodadSpawnerDef {
        base: SpawnerBase {
            id: 0,
            unit_id,
            position: WorldSpawnPosition {
                world_id: t.world_id,
                zone_id: t.zone_id,
                x,
                y,
                z,
                yaw,
                pitch: 0.0,
                roll: 0.0,
            },
            ..SpawnerBase::default()
        },
        scale: 1.0,
        count: 1,
        related_ids: None,
        respawn_doodad_template_id: 0,
    };
    let spawner = Arc::new(DoodadSpawner::new(def, Some(t.instance_id)));
    spawner.spawn(server, 0);
}

/// `NpcControlEffect.Apply` (target is an NPC).
pub fn npc_control_effect(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::NpcControlEffect,
    target: &NpcRef,
) {
    tracing::info!(
        "NpcControllEffect: CategoryId={}, ParamString={}, ParamInt={}",
        e.category_id,
        e.param_string,
        e.param_int
    );
    let sim = server
        .systems
        .ai
        .simulations
        .get(&target.obj_id)
        .map(|s| s.clone());
    match e.category_id {
        // Signal, FollowUnit, GoAway
        0 | 1 | 4 => {}
        2 => {
            // FollowPath
            if target.data().unit.is_in_patrol {
                return;
            }
            target.data().unit.is_in_patrol = true;
            if let Some(sim) = sim {
                {
                    let mut s = sim.lock().expect("simulation poisoned");
                    s.running_mode = false;
                    s.move_to_path_enabled = false;
                    s.move_file_name = e.param_string.clone();
                }
                simulation::go_to_path(server, &sim, target, true, 0, 0);
            }
        }
        3 => {
            // AttackUnit: SetFaction(Monstrosity)
            let obj = WorldObject::from(target);
            // Unit.SetFaction's nearby-player aggro runs through the npc_faction_set hook.
            crate::systems::skills::combat::set_faction(server, &obj, 115);
        }
        5 => {
            // RunCommandSet
            let Some(cmds) = server
                .game_data
                .systems
                .ai
                .ai
                .ai_commands(e.param_int)
                .map(<[_]>::to_vec)
            else {
                return;
            };
            if cmds.is_empty() {
                return;
            }
            let queued = cmds.clone();
            post(server, target.obj_id, move |c| {
                c.enqueue_ai_commands(&queued, false)
            });
            let (mut file, mut file2, mut skill_id, mut timeout) =
                (String::new(), String::new(), 0u32, 0u32);
            for c in &cmds {
                match c.cmd_id {
                    AiCommandCategory::FollowUnit => {}
                    AiCommandCategory::FollowPath => {
                        if file.is_empty() {
                            file = c.param2.clone();
                        } else {
                            file2 = c.param2.clone();
                        }
                    }
                    AiCommandCategory::UseSkill => skill_id = c.param1,
                    AiCommandCategory::Timeout => timeout = c.param1,
                    AiCommandCategory::Other(v) => {
                        tracing::error!("NpcControlEffect: AI command {v} (NotSupportedException)");
                        return;
                    }
                }
            }
            if !file.is_empty() {
                if target.data().unit.is_in_patrol {
                    return;
                }
                target.data().unit.is_in_patrol = true;
                if let Some(sim) = sim {
                    {
                        let mut s = sim.lock().expect("simulation poisoned");
                        s.running_mode = false;
                        s.cycle = false;
                        s.move_to_path_enabled = false;
                        s.move_file_name = file;
                        s.move_file_name2 = file2;
                    }
                    simulation::go_to_path(server, &sim, target, true, skill_id, timeout);
                }
            }
        }
        other => tracing::error!("NpcControlEffect: category {other} (NotSupportedException)"),
    }
}

/// `Npc.OnDamageReceived`: `Ai.OnAggroTargetChanged()` (the damage effect adds the aggro).
pub fn on_damage_received(server: &GameServer, npc: &NpcRef) {
    post(server, npc.obj_id, |c| c.on_aggro_target_changed());
}

/// `npc.Ai?.OnNoAggroTarget()`.
pub fn on_no_aggro_target(server: &GameServer, npc: &NpcRef) {
    post(server, npc.obj_id, |c| c.on_no_aggro_target());
}

/// Installs the AI hooks (`System.start`).
pub fn install(server: &GameServer) {
    server.systems.hooks.ai.install(Arc::new(AiSubsystemHooks));
    crate::systems::services::commands::sub::ASTAR_HOOKS.install(Arc::new(AiAStarHooks));
}

/// The AStar GM sub commands' view of an NPC's `Ai.PathNode` (`services::commands::sub`
/// `ASTAR_HOOKS`). The path node lives in the NPC's AI: the calls run through
/// [`post`] and read the result back when the AI was free to run them right away (an AI in the
/// middle of its step on the AI thread answers with the defaults).
pub struct AiAStarHooks;

/// Runs `f` on the AI of `npc` and returns its result when it ran synchronously.
fn with_ai_now<T: Send + 'static>(
    server: &GameServer,
    npc: &NpcRef,
    f: impl FnOnce(&mut super::framework::AiCtx<'_>) -> T + Send + 'static,
) -> Option<T> {
    let out = Arc::new(Mutex::new(None));
    let slot = out.clone();
    post(server, npc.obj_id, move |c| {
        *slot.lock().expect("astar poisoned") = Some(f(c));
    });
    let r = out.lock().expect("astar poisoned").take();
    r
}

fn v3(p: [f32; 3]) -> crate::systems::skills::controllers::Vec3 {
    crate::systems::skills::controllers::Vec3::new(p[0], p[1], p[2])
}

fn arr(v: crate::systems::skills::controllers::Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

impl crate::systems::services::commands::sub::AStarHooks for AiAStarHooks {
    fn set_start(&self, server: &GameServer, npc: &NpcRef, pos: [f32; 3]) {
        post(server, npc.obj_id, move |c| {
            c.ai.path_node.start_point_pos = v3(pos)
        });
    }

    fn set_end(&self, server: &GameServer, npc: &NpcRef, pos: [f32; 3]) {
        post(server, npc.obj_id, move |c| {
            c.ai.path_node.end_point_pos = v3(pos)
        });
    }

    fn start_end(&self, server: &GameServer, npc: &NpcRef) -> ([f32; 3], [f32; 3]) {
        with_ai_now(server, npc, |c| {
            (
                arr(c.ai.path_node.start_point_pos),
                arr(c.ai.path_node.end_point_pos),
            )
        })
        .unwrap_or_default()
    }

    fn find_path(&self, server: &GameServer, npc: &NpcRef, zone_key: u32) -> Vec<[f32; 3]> {
        let obj = WorldObject::from(npc);
        let Some(geo) = crate::systems::skills::geometry::world_of(server, &obj)
            .and_then(|w| server.systems.ai.geodata.get(w.template.id))
        else {
            return Vec::new();
        };
        with_ai_now(server, npc, move |c| {
            c.ai.path_node.zone_key = zone_key;
            let (start, end) = (c.ai.path_node.start_point_pos, c.ai.path_node.end_point_pos);
            let mut res = match c.ai.path_node.find_path(&geo, start, end) {
                Ok(r) => r,
                Err(e) => {
                    tracing::error!("AStar FindPath for npc {}: {}", c.npc.obj_id, e.0);
                    return Vec::new();
                }
            };
            // Skip the first node in this list if not on it
            if !res.is_empty() && start != res[0] {
                res.remove(0);
            }
            match geo.reduce_path(&res, 10) {
                Ok(reduced) => c.ai.path_node.found_path = reduced,
                Err(_) => tracing::error!(
                    "AStar ReducePath for npc {}: NullReferenceException",
                    c.npc.obj_id
                ),
            }
            res.into_iter().map(arr).collect()
        })
        .unwrap_or_default()
    }

    fn found_path(&self, server: &GameServer, npc: &NpcRef) -> Vec<[f32; 3]> {
        with_ai_now(server, npc, |c| {
            c.ai.path_node.found_path.iter().copied().map(arr).collect()
        })
        .unwrap_or_default()
    }

    fn spawn_marker(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        template_id: u32,
        pos: [f32; 3],
    ) {
        use crate::data::world_templates::WorldSpawnPosition;
        use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
        let t = WorldObject::from(owner).transform();
        let def = DoodadSpawnerDef {
            base: SpawnerBase {
                id: 0,
                unit_id: template_id,
                position: WorldSpawnPosition {
                    world_id: t.world_id,
                    zone_id: t.zone_id,
                    x: pos[0],
                    y: pos[1],
                    z: pos[2],
                    yaw: 0.0,
                    pitch: 0.0,
                    roll: 0.0,
                },
                ..SpawnerBase::default()
            },
            scale: 1.0,
            count: 1,
            related_ids: None,
            respawn_doodad_template_id: 0,
        };
        let spawner = Arc::new(DoodadSpawner::new(def, Some(t.instance_id)));
        // Spawn(0, 0, ownerObjId)
        if let Some(d) = spawner.spawn(server, 0) {
            d.data().owner_obj_id = owner.obj_id;
        }
    }
}
