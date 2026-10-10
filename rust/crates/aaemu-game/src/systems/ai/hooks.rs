//! How the rest of the server reaches the AI:
//!
//! * [`AiHooks`] (`server.systems.hooks.ai`): the `npc.Ai` call sites of the NPC subsystem
//!   (`NpcManager.Create`, `NpcSpawnerNpc.Spawn`, `Npc.DoDie`, `NpcSpawner.DoSpawnEffect`) and
//!   of the skills subsystem (`Unit.SetFaction`'s NPC tail);
//! * [`AiNpcSkillHooks`]: a decorator around whatever `NpcSkillHooks` is installed when the AI
//!   starts, adding the AI parts of `Npc.OnDamageReceived`, `npc.Ai?.OnNoAggroTarget()`,
//!   `Npc.CheckIfEmptyAggroToReturn`, `Npc.DoDie` (aggro clean-up + `Ai.GoToDead()`),
//!   `CurrentGameStance` / `CurrentAlertness`, and the `NpcControlEffect`.

use std::sync::{Arc, Mutex};

use crate::data::world_data::Portal;
use crate::models::movement::{GameStanceType, MoveTypeAlertness};
use crate::models::npc::Npc;
use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::{MainWorldPosition, NpcSkillHooks};
use crate::systems::skills::state::AggroKind;
use crate::systems::skills::stats::StatKind;
use crate::systems::skills::types::{SkillCastTarget, SkillCaster};
use crate::world::{MateRef, NpcRef, PlayerRef, WorldObject};

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
fn check_abuser_empty_aggro(server: &GameServer, abuser_id: u32) {
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
fn npc_control_effect(
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
            crate::systems::skills::combat::set_faction(server, &obj, 115);
            faction_set(server, target);
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

/// The NPC hooks of the skills subsystem with the AI parts added.
pub struct AiNpcSkillHooks {
    inner: Arc<dyn NpcSkillHooks>,
}

struct NoNpcHooks;
impl NpcSkillHooks for NoNpcHooks {}

impl AiNpcSkillHooks {
    /// Wraps `inner` (`None` = the defaults).
    pub fn new(inner: Option<Arc<dyn NpcSkillHooks>>) -> Self {
        Self {
            inner: inner.unwrap_or_else(|| Arc::new(NoNpcHooks)),
        }
    }
}

impl NpcSkillHooks for AiNpcSkillHooks {
    fn npc_attribute(&self, server: &GameServer, npc: &Npc, attr: StatKind) -> Option<f64> {
        self.inner.npc_attribute(server, npc, attr)
    }

    fn npc_before_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
        self.inner.npc_before_death(server, npc, killer);
    }

    fn npc_after_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
        self.inner.npc_after_death(server, npc, killer);
        on_npc_died(server, npc);
    }

    fn npc_respawn_time(&self, server: &GameServer, npc: &NpcRef) -> i32 {
        self.inner.npc_respawn_time(server, npc)
    }

    fn npc_damage_received(
        &self,
        server: &GameServer,
        npc: &NpcRef,
        attacker: &WorldObject,
        amount: i32,
    ) {
        self.inner
            .npc_damage_received(server, npc, attacker, amount);
        // Npc.OnDamageReceived: AddUnitAggro is done by the damage effect; Ai.OnAggroTargetChanged
        post(server, npc.obj_id, |c| c.on_aggro_target_changed());
    }

    fn npc_no_aggro_target(&self, server: &GameServer, npc: &NpcRef) {
        self.inner.npc_no_aggro_target(server, npc);
        post(server, npc.obj_id, |c| c.on_no_aggro_target());
    }

    fn npc_aggro_removed(&self, server: &GameServer, npc: &NpcRef, abuser: u32) {
        self.inner.npc_aggro_removed(server, npc, abuser);
        // `(this as Npc)?.CheckIfEmptyAggroToReturn(unit)` checks the *abuser*
        check_abuser_empty_aggro(server, abuser);
    }

    fn npc_engage_combat_quest(&self, server: &GameServer, npc: &Npc) -> u32 {
        self.inner.npc_engage_combat_quest(server, npc)
    }

    fn doodad_skill_hit(
        &self,
        server: &GameServer,
        doodad: &WorldObject,
        caster: &WorldObject,
        skill_id: u32,
    ) {
        self.inner
            .doodad_skill_hit(server, doodad, caster, skill_id);
    }

    fn spawn_channel_doodad(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        template_id: u32,
    ) -> Option<u32> {
        self.inner.spawn_channel_doodad(server, caster, template_id)
    }

    fn delete_doodad(&self, server: &GameServer, obj_id: u32) {
        self.inner.delete_doodad(server, obj_id);
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        kind: EffectKind,
        template: &EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        if let EffectTemplate::NpcControl(e) = template {
            if let Some(n) = args.target.as_obj().and_then(WorldObject::as_npc) {
                npc_control_effect(server, e, n);
            }
            return true;
        }
        self.inner.apply_effect(server, kind, template, args)
    }

    fn spawn_doodad(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        template_id: u32,
        zone_id: u32,
        pos: PositionAndRotation,
        delay_ms: i32,
    ) {
        self.inner
            .spawn_doodad(server, caster, template_id, zone_id, pos, delay_ms);
    }

    fn npc_pet_item_id(&self, server: &GameServer, npc: &NpcRef) -> u32 {
        self.inner.npc_pet_item_id(server, npc)
    }

    fn despawn_npc(&self, server: &GameServer, npc: &NpcRef) {
        self.inner.despawn_npc(server, npc);
    }

    fn npc_non_pushable_by_actor(&self, server: &GameServer, npc: &NpcRef) -> bool {
        self.inner.npc_non_pushable_by_actor(server, npc)
    }

    fn npc_move_state(
        &self,
        _server: &GameServer,
        npc: &NpcRef,
    ) -> (GameStanceType, MoveTypeAlertness) {
        let n = npc.data();
        (n.current_game_stance, n.current_alertness)
    }

    fn unmount_active_mates(&self, server: &GameServer, player: &PlayerRef) {
        self.inner.unmount_active_mates(server, player);
    }

    fn spawn_mount(&self, server: &GameServer, player: &PlayerRef, skill_item: &SkillCaster) {
        self.inner.spawn_mount(server, player, skill_item);
    }

    fn mate_is_max_level(&self, server: &GameServer, mate: &MateRef) -> bool {
        self.inner.mate_is_max_level(server, mate)
    }

    fn mate_add_exp(&self, server: &GameServer, mate: &MateRef, exp: i32) {
        self.inner.mate_add_exp(server, mate, exp);
    }

    fn bind_slave(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        slave_obj_id: u32,
        attach_point: i32,
    ) {
        self.inner
            .bind_slave(server, player, slave_obj_id, attach_point);
    }

    fn spawn_slave(&self, server: &GameServer, player: &PlayerRef, skill_item: &SkillCaster) {
        self.inner.spawn_slave(server, player, skill_item);
    }

    fn riders_escape(&self, server: &GameServer, player: &PlayerRef, target: &SkillCastTarget) {
        self.inner.riders_escape(server, player, target);
    }

    fn character_slave(&self, server: &GameServer, player: &PlayerRef) -> Option<WorldObject> {
        self.inner.character_slave(server, player)
    }

    fn world_gate(&self, server: &GameServer, id: u32) -> Option<Portal> {
        self.inner.world_gate(server, id)
    }

    fn closest_return_portal(&self, server: &GameServer, player: &PlayerRef) -> Option<Portal> {
        self.inner.closest_return_portal(server, player)
    }

    fn main_world_position(
        &self,
        server: &GameServer,
        player: &PlayerRef,
    ) -> Option<MainWorldPosition> {
        self.inner.main_world_position(server, player)
    }

    #[allow(clippy::too_many_arguments)]
    fn add_private_portal(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        x: f32,
        y: f32,
        z: f32,
        z_rot: f32,
        zone_id: u32,
        name: &str,
    ) {
        self.inner
            .add_private_portal(server, player, x, y, z, z_rot, zone_id, name);
    }

    fn rename_private_portal(&self, server: &GameServer, player: &PlayerRef, id: u32, name: &str) {
        self.inner.rename_private_portal(server, player, id, name);
    }

    fn leave_instance(&self, server: &GameServer, player: &PlayerRef) {
        self.inner.leave_instance(server, player);
    }

    fn apply_special_effect(
        &self,
        server: &GameServer,
        special_type: i32,
        args: &EffectArgs<'_>,
        values: [i32; 4],
    ) -> bool {
        self.inner
            .apply_special_effect(server, special_type, args, values)
    }
}

/// Installs the AI hooks (`System.start`, after the NPC subsystem installed its own).
pub fn install(server: &GameServer) {
    server.systems.hooks.ai.install(Arc::new(AiSubsystemHooks));
    let inner = server.systems.skills.ext.npcs.get();
    server
        .systems
        .skills
        .ext
        .npcs
        .install(Arc::new(AiNpcSkillHooks::new(inner)));
}
