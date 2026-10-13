//! The NPC subsystem's implementations of the other subsystems' hooks:
//!
//! * [`NpcSkills`]: `skills::ext::NpcSkillHooks` (`Npc` attribute getters, `Npc.DoDie`, spawner
//!   respawn time, doodad skill hits / channel doodads / `SpawnDoodad`, portals, the NPC-owned
//!   effect templates);
//!
//! installed by [`install`] from `System::start`. The mate / slave / instance / AI parts of
//! `NpcSkillHooks` stay at their trait defaults (owned by the vehicles, instances and AI
//! subsystems).

use std::sync::Arc;

use crate::data::world_data::Portal;
use crate::models::npc::Npc;
use crate::models::transform::{PositionAndRotation, Transform};
use crate::server::GameServer;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::special::movement::{add_distance_to_front_deg, deg_to_rad};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::NpcSkillHooks;
use crate::systems::skills::geometry::{around_radius, Target};
use crate::systems::skills::stats::StatKind;
use crate::systems::skills::types::{BuffRemoveOn, SkillObject};
use crate::world::{NpcRef, PlayerRef, WorldInstance, WorldObject};

use super::doodad::manager as doodads;

/// `BaseUnitType.Npc`.
const BASE_UNIT_TYPE_NPC: u32 = 1;
/// Installs this subsystem's hook implementations into the other subsystems.
pub fn install(server: &GameServer) {
    install_economy(server);
    server.systems.skills.ext.npcs.install(Arc::new(NpcSkills));
}

/// `transform.CloneDetached()` of a world object: its world position / rotation, no parent.
pub fn detached_transform(world: &WorldInstance, obj: &WorldObject) -> Transform {
    let mut t = obj.transform();
    let pr = world.world_pos_rot(obj);
    t.parent_obj_id = 0;
    t.set_position(pr.x, pr.y, pr.z, pr.rot_x, pr.rot_y, pr.rot_z);
    t
}

/// The world instance and doodad handle of a doodad obj id.
fn doodad_by_obj_id(
    server: &GameServer,
    obj_id: u32,
) -> Option<(Arc<WorldInstance>, crate::world::DoodadRef)> {
    let obj = server.world.object(obj_id)?;
    let d = obj.as_doodad()?.clone();
    let world = server.world.world_of(&obj)?;
    Some((world, d))
}

/// `skills::ext::NpcSkillHooks` of the NPC subsystem.
pub struct NpcSkills;

impl NpcSkillHooks for NpcSkills {
    fn npc_attribute(&self, server: &GameServer, npc: &Npc, attr: StatKind) -> Option<f64> {
        use StatKind as S;
        // Only the getters `Npc` overrides; everything else is the `Unit` one.
        if !matches!(
            attr,
            S::Str
                | S::Dex
                | S::Sta
                | S::Int
                | S::Spi
                | S::Fai
                | S::MaxHp
                | S::HpRegen
                | S::PersistentHpRegen
                | S::MaxMp
                | S::MpRegen
                | S::PersistentMpRegen
                | S::LevelDps
                | S::Dps
                | S::DpsInc
                | S::OffhandDps
                | S::RangedDps
                | S::RangedDpsInc
                | S::MDps
                | S::MDpsInc
                | S::Armor
                | S::MagicResistance
        ) {
            return None;
        }
        let s = super::npc::stats::npc_stats(&server.game_data, npc)?;
        Some(match attr {
            S::Str => f64::from(s.str_),
            S::Dex => f64::from(s.dex),
            S::Sta => f64::from(s.sta),
            S::Int => f64::from(s.int),
            S::Spi => f64::from(s.spi),
            S::Fai => f64::from(s.fai),
            S::MaxHp => f64::from(s.max_hp),
            S::HpRegen => f64::from(s.hp_regen),
            S::PersistentHpRegen => f64::from(s.persistent_hp_regen),
            S::MaxMp => f64::from(s.max_mp),
            S::MpRegen => f64::from(s.mp_regen),
            S::PersistentMpRegen => f64::from(s.persistent_mp_regen),
            S::LevelDps => f64::from(s.level_dps),
            S::Dps => f64::from(s.dps),
            S::DpsInc => f64::from(s.dps_inc),
            S::OffhandDps => f64::from(s.offhand_dps),
            S::RangedDps => f64::from(s.ranged_dps),
            S::RangedDpsInc => f64::from(s.ranged_dps_inc),
            S::MDps => f64::from(s.mdps),
            S::MDpsInc => f64::from(s.mdps_inc),
            S::Armor => f64::from(s.armor),
            S::MagicResistance => f64::from(s.magic_resistance),
            _ => return None,
        })
    }

    fn npc_before_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
        super::npc::death::before_death(server, npc, killer);
    }

    fn npc_after_death(&self, server: &GameServer, npc: &NpcRef, killer: Option<&WorldObject>) {
        super::npc::death::after_death(server, npc, killer);
    }

    fn npc_damage_received(
        &self,
        server: &GameServer,
        npc: &NpcRef,
        _attacker: &WorldObject,
        _amount: i32,
    ) {
        // Npc.OnDamageReceived: AddUnitAggro is done by the damage effect;
        // Ai.OnAggroTargetChanged (systems::ai)
        crate::systems::ai::hooks::on_damage_received(server, npc);
    }

    fn npc_no_aggro_target(&self, server: &GameServer, npc: &NpcRef) {
        crate::systems::ai::hooks::on_no_aggro_target(server, npc);
    }

    fn npc_aggro_removed(&self, server: &GameServer, _npc: &NpcRef, abuser: u32) {
        // `(this as Npc)?.CheckIfEmptyAggroToReturn(unit)` checks the *abuser*
        crate::systems::ai::hooks::check_abuser_empty_aggro(server, abuser);
    }

    fn npc_move_state(
        &self,
        _server: &GameServer,
        npc: &NpcRef,
    ) -> (
        crate::models::movement::GameStanceType,
        crate::models::movement::MoveTypeAlertness,
    ) {
        let n = npc.data();
        (n.current_game_stance, n.current_alertness)
    }

    fn npc_respawn_time(&self, server: &GameServer, npc: &NpcRef) -> i32 {
        server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(npc.obj_id)
            .map_or(0, |s| s.respawn_time())
    }

    // npc_damage_received / npc_no_aggro_target / npc_aggro_removed: the NPC AI (later phase).

    fn npc_engage_combat_quest(&self, server: &GameServer, npc: &Npc) -> u32 {
        server
            .game_data
            .systems
            .npcs
            .npc
            .template(npc.unit.template_id)
            .map_or(0, |t| t.engage_combat_give_quest_id)
    }

    fn doodad_skill_hit(
        &self,
        server: &GameServer,
        doodad: &WorldObject,
        caster: &WorldObject,
        skill_id: u32,
    ) {
        let (Some(d), Some(world)) = (doodad.as_doodad(), server.world.world_of(doodad)) else {
            return;
        };
        doodads::on_skill_hit(server, &world, d, Some(caster), skill_id);
    }

    fn spawn_channel_doodad(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        doodad_template_id: u32,
    ) -> Option<u32> {
        let world = server.world.world_of(caster)?;
        let doodad = doodads::create(server, &world, 0, doodad_template_id, Some(caster), true)?;
        let t = detached_transform(&world, caster);
        doodad.data().transform = t;
        doodads::init_doodad(server, &world, &doodad);
        doodads::spawn(server, &world, &doodad);
        Some(doodad.obj_id)
    }

    fn delete_doodad(&self, server: &GameServer, obj_id: u32) {
        if let Some((world, d)) = doodad_by_obj_id(server, obj_id) {
            doodads::delete(server, &world, &d);
        }
    }

    fn apply_effect(
        &self,
        server: &GameServer,
        kind: EffectKind,
        template: &EffectTemplate,
        args: &EffectArgs<'_>,
    ) -> bool {
        use EffectTemplate as T;
        // Slave `SpawnEffect`, `RepairSlaveEffect`, ... (vehicles subsystem).
        if crate::systems::vehicles::effects::apply_effect(server, kind, template, args) {
            return true;
        }
        match template {
            T::Interaction {
                world_interaction,
                doodad_id,
                ..
            } => {
                interaction_effect(server, *world_interaction, *doodad_id, args);
                true
            }
            T::Spawn(e) => {
                spawn_effect(server, e, args);
                true
            }
            T::NpcSpawnerSpawn(e) => {
                npc_spawner_spawn_effect(server, e, args);
                true
            }
            T::NpcSpawnerDespawn { spawner_id, .. } => {
                if let Some(world) = args.caster.as_ref().and_then(|c| server.world.world_of(c)) {
                    let manager = server.systems.npcs.spawns.get_or_create(&world);
                    for spawner in manager.get_npc_spawner(*spawner_id) {
                        spawner.despawn_npcs_now(server);
                    }
                }
                true
            }
            T::KillNpcWithoutCorpse(e) => {
                kill_npc_without_corpse(server, e, args);
                true
            }
            T::NpcControl(e) => {
                npc_control(server, e, args);
                true
            }
            T::SpawnGimmick(e) => {
                spawn_gimmick_effect(server, e, args);
                true
            }
            T::OpenPortal { distance, .. } => {
                open_portal_effect(server, *distance, args);
                true
            }
            T::ScopedF {
                range, doodad_id, ..
            } => {
                scoped_f_effect(server, *range, *doodad_id, args);
                true
            }
            T::MoveToRezPoint { .. } => {
                // The C# body only traces.
                tracing::trace!("MoveToRezPointEffect");
                true
            }
            T::SpawnFish { .. } => {
                spawn_fish_effect(server, args);
                true
            }
            // PutDownBackpack (items + housing) is not handled here.
            _ => false,
        }
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
        let Some(world) = server.world.world_of(caster) else {
            return;
        };
        let Some(doodad) = doodads::create(server, &world, 0, template_id, Some(caster), true)
        else {
            tracing::error!("SpawnDoodad: doodad template {template_id} does not exist");
            return;
        };
        // `GeoData.GetHeight(doodad.Transform.World.Position)` (the caster-based position).
        let base = detached_transform(&world, caster);
        let z = crate::systems::ai::collision::ground_height_or(
            server,
            world.id,
            base.x,
            base.y,
            base.z,
            || super::heightmap::get_height(server, zone_id, base.x, base.y, base.z),
        );
        {
            let mut d = doodad.data();
            d.transform = base;
            d.transform.zone_id = zone_id;
            d.transform
                .set_position(pos.x, pos.y, z, pos.rot_x, pos.rot_y, pos.rot_z);
        }
        doodads::init_doodad(server, &world, &doodad);
        if delay_ms > 0 {
            let (Some(srv), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current())
            else {
                doodads::spawn(server, &world, &doodad);
                return;
            };
            rt.spawn(async move {
                tokio::time::sleep(std::time::Duration::from_millis(delay_ms as u64)).await;
                doodads::spawn(&srv, &world, &doodad);
            });
        } else {
            doodads::spawn(server, &world, &doodad);
        }
    }

    fn npc_pet_item_id(&self, server: &GameServer, npc: &NpcRef) -> u32 {
        let template_id = npc.data().unit.template_id;
        server
            .game_data
            .systems
            .npcs
            .npc
            .template(template_id)
            .map_or(0, |t| t.pet_item_id)
    }

    fn despawn_npc(&self, server: &GameServer, npc: &NpcRef) {
        // `Npc.DoDespawn(npc)` -> `Spawner.DoDespawn(npc)` (C#: NRE without a spawner).
        match server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
            Some(s) => s.do_despawn(server, npc),
            None => tracing::error!(
                "DoDespawn: NPC {} has no spawner (NullReferenceException)",
                npc.obj_id
            ),
        }
    }

    fn npc_non_pushable_by_actor(&self, server: &GameServer, npc: &NpcRef) -> bool {
        let template_id = npc.data().unit.template_id;
        server
            .game_data
            .systems
            .npcs
            .npc
            .template(template_id)
            .is_some_and(|t| t.non_pushable_by_actor)
    }

    // npc_move_state: the NPC AI / movement simulation keeps the stance (later phase).
    // ---- mates / slaves: systems::vehicles ----

    fn unmount_active_mates(&self, server: &GameServer, player: &PlayerRef) {
        crate::systems::vehicles::mates::unmount_active_mates_on_blink(server, player);
    }

    fn spawn_mount(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        skill_item: &crate::systems::skills::types::SkillCaster,
    ) {
        if let crate::systems::skills::types::SkillCaster::Item { item_id, .. } = skill_item {
            crate::systems::vehicles::mates::spawn_mount(server, player, *item_id);
        }
    }

    fn mate_is_max_level(&self, server: &GameServer, mate: &crate::world::MateRef) -> bool {
        crate::systems::vehicles::mates::is_max_level(server, &mate.data())
    }

    fn mate_add_exp(&self, server: &GameServer, mate: &crate::world::MateRef, exp: i32) {
        crate::systems::vehicles::mates::add_exp(server, mate, exp);
    }

    fn bind_slave(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        slave_obj_id: u32,
        attach_point: i32,
    ) {
        use crate::systems::vehicles::slaves;
        slaves::bind_slave(
            server,
            player,
            slave_obj_id,
            attach_point as u8,
            slaves::attach_reason::NEW_MASTER,
        );
    }

    fn spawn_slave(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        skill_item: &crate::systems::skills::types::SkillCaster,
    ) {
        crate::systems::vehicles::slaves::create_from_skill(server, player, skill_item);
    }

    fn riders_escape(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        target: &crate::systems::skills::types::SkillCastTarget,
    ) {
        crate::systems::vehicles::slaves::riders_escape(server, player, target);
    }

    fn character_slave(&self, server: &GameServer, player: &PlayerRef) -> Option<WorldObject> {
        crate::systems::vehicles::slaves::character_slave(server, player)
    }

    fn world_gate(&self, server: &GameServer, id: u32) -> Option<Portal> {
        super::portal::world_gates_by_id(server, id).cloned()
    }

    fn closest_return_portal(&self, server: &GameServer, player: &PlayerRef) -> Option<Portal> {
        Some(super::portal::closest_return_portal(server, player))
    }

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
        super::portal::add_private_portal(server, player, x, y, z, z_rot, zone_id, name);
    }

    fn rename_private_portal(&self, server: &GameServer, player: &PlayerRef, id: u32, name: &str) {
        super::portal::change_private_portal_name(server, player, id, name);
    }

    // main_world_position / leave_instance: the trait defaults are the instances subsystem's.
}

/// `InteractionEffect.Apply`.
fn interaction_effect(
    server: &GameServer,
    world_interaction: i32,
    doodad_id: u32,
    args: &EffectArgs<'_>,
) {
    tracing::debug!("InteractionEffect, {world_interaction}");
    let Some(caster) = &args.caster else {
        return;
    };
    let wi_type = world_interaction as u32;
    if super::doodad::interactions::interaction_class(wi_type).is_none() {
        tracing::error!("InteractionEffect, Unknown world interaction: {world_interaction}");
        return;
    }
    crate::systems::skills::buffs::trigger_remove_on(server, caster, BuffRemoveOn::Interaction, 0);
    crate::systems::dispatch_buff_events(server, caster);
    let target = match &args.target {
        Target::Obj(o) => Some(o.clone()),
        Target::Pos(_) => None,
    };
    if let (Some(skill), Some(t)) = (args.source.skill(), &target) {
        super::doodad::interactions::execute_interaction(
            server,
            wi_type,
            caster,
            Some(t),
            skill.template.id,
            doodad_id,
            None,
        );
    }
    let Some(character) = caster.as_player() else {
        return;
    };
    if character.data().skill_char.skill_cancelled {
        return;
    }
    if let Some(d) = target.as_ref().and_then(WorldObject::as_doodad) {
        let template_id = d.data().template_id;
        crate::systems::quests::events::on_doodad_interaction(
            server,
            character,
            character,
            template_id,
        );
    }
}

/// `SpawnEffect.Apply` (the NPC owner type; slaves are the vehicles subsystem's).
fn spawn_effect(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::SpawnEffect,
    args: &EffectArgs<'_>,
) {
    tracing::trace!(
        "SpawnEffect: OwnerTypeId={}, SubType={}, UseSummonerFaction={}, LifeTime={}",
        e.owner_type_id,
        e.sub_type,
        e.use_summoner_faction,
        e.life_time
    );
    if e.owner_type_id != BASE_UNIT_TYPE_NPC {
        if e.owner_type_id == 2 {
            tracing::debug!("SpawnEffect: slave {} (vehicles subsystem)", e.sub_type);
        }
        return;
    }
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    let target = match &args.target {
        Target::Obj(o) => o.clone(),
        Target::Pos(_) => caster.clone(),
    };
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let Some(spawner) = manager.get_npc_spawner_at(server, e.sub_type, &target) else {
        tracing::info!("SpawnEffect: SubType={} not found in spawners.", e.sub_type);
        return;
    };
    // dir id 1 = relative to target/spawner, 2 = relative to caster.
    let rel = |id: u32| match id {
        1 => Some(&target),
        2 => Some(caster),
        _ => None,
    };
    let (Some(pos_rel), Some(ori_rel)) = (rel(e.pos_dir_id), rel(e.ori_dir_id)) else {
        tracing::warn!(
            "SpawnEffect: Unhandled PosDirId {} or OriDirId {}",
            e.pos_dir_id,
            e.ori_dir_id
        );
        return;
    };
    let p = world.world_pos_rot(pos_rel);
    let o = world.world_pos_rot(ori_rel);
    let (xx, yy) = add_distance_to_front_deg(e.pos_distance, p.x, p.y, e.pos_angle);
    let mut position = spawner.position();
    position.x = xx;
    position.y = yy;
    position.z = p.z;
    position.yaw = o.rot_z + deg_to_rad(e.ori_angle);
    spawner.set_position(position);
    spawner.set_respawn_time(0); // don't respawn
    let params = super::npc::spawner::SpawnEffectParams {
        use_summoner_faction: e.use_summoner_faction,
        use_summoner_aggro_target: e.use_summoner_aggro_target,
        life_time: e.life_time,
    };
    spawner.do_spawn_effect(server, spawner.id, &params, Some(caster), Some(&target));
}

/// `SpawnFishEffect.Apply`: a fish from the fish school near the fishing character is spawned
/// at the bobber (the target) through a temporary spawner, then it and the character target
/// each other.
fn spawn_fish_effect(server: &GameServer, args: &EffectArgs<'_>) {
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(player) = caster.as_player() else {
        return;
    };
    let fish_spawner_id = get_fish_spawner_id(server, caster);
    if fish_spawner_id == 0 {
        let name = player.data().unit.name.clone();
        tracing::debug!("Fish Spawner ID not found for player {name}.");
        return;
    }
    let template = server
        .game_data
        .systems
        .npcs
        .npc_spawners
        .get_npc_spawner_template(fish_spawner_id);
    let Some(template) = template.filter(|t| !t.npcs.is_empty()) else {
        tracing::warn!("No NPC templates available for fish spawner {fish_spawner_id}.");
        return;
    };
    // Weighted random fish selection
    let entry = if template.npcs.len() == 1 {
        &template.npcs[0]
    } else {
        select_weighted_fish(&template.npcs, rand::random::<f64>())
    };
    tracing::debug!(
        "Selected fish template {} from spawner {fish_spawner_id}",
        entry.member_id
    );
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    // Temporary spawner at the bobber (`target.Transform.Clone().CloneAsSpawnPosition()`).
    let position = match &args.target {
        Target::Obj(o) => {
            let t = o.transform();
            let pr = world.world_pos_rot(o);
            crate::data::world_templates::WorldSpawnPosition {
                world_id: t.world_id,
                zone_id: t.zone_id,
                x: pr.x,
                y: pr.y,
                z: pr.z,
                roll: pr.rot_x,
                pitch: pr.rot_y,
                yaw: pr.rot_z,
            }
        }
        // A position unit is a detached clone of the caster's transform.
        Target::Pos(p) => crate::data::world_templates::WorldSpawnPosition {
            world_id: caster.transform().world_id,
            zone_id: p.zone_id,
            x: p.pos.x,
            y: p.pos.y,
            z: p.pos.z,
            roll: p.pos.rot_x,
            pitch: p.pos.rot_y,
            yaw: p.pos.rot_z,
        },
    };
    let temp_spawner = Arc::new(super::npc::spawner::NpcSpawner::new(
        super::npc::spawner::NpcSpawnerInit {
            id: 0,
            spawner_id: fish_spawner_id,
            unit_id: entry.member_id,
            follow_path: String::new(),
            follow_npc: 0,
            respawn_time: 15,
            despawn_time: 20,
            special_link: String::new(),
            count: 1,
            npc_spawner_ids: Vec::new(),
            parent_world: Some(world.id),
            template: Some(template.clone()),
            spawnable_npcs: Vec::new(),
        },
        super::npc::spawner::NpcSpawnerShared::new(position),
    ));
    let (player_id, player_name) = {
        let c = player.data();
        (c.id, c.unit.name.clone())
    };
    let spawned = match super::npc::spawner::spawn_member(server, entry, &temp_spawner, player_id) {
        Ok(Some(v)) if !v.is_empty() => v,
        Ok(_) => {
            tracing::warn!(
                "npcTemplate.Spawn returned no fish for template {}",
                entry.member_id
            );
            return;
        }
        Err(e) => {
            tracing::error!("Error spawning fish from template {}: {e}", entry.member_id);
            return;
        }
    };
    // Register so that Despawn() -> RemoveNpcFromSpawnedList doesn't log a false warning
    temp_spawner.try_add_spawned(fish_spawner_id, spawned.clone());
    let fish = WorldObject::from(&spawned[0]);
    // Aggro & targeting
    spawned[0].data().unit.current_target = player.obj_id;
    crate::systems::skills::combat::add_unit_aggro(
        server,
        &fish,
        crate::systems::skills::state::AggroKind::Damage,
        caster,
        10_000,
    );
    player.data().unit.current_target = fish.obj_id();
    crate::systems::skills::broadcast(
        server,
        caster,
        &crate::packets::world_objects::SCTargetChangedPacket {
            obj_id: player.obj_id,
            target_obj_id: fish.obj_id(),
        },
        true,
    );
    tracing::debug!(
        "Successfully spawned fish {} (owner {player_id}) at bobber for {player_name}",
        entry.member_id
    );
}

/// `SpawnFishEffect.SelectWeightedRandom(npcs)` with `roll01 = Random.Shared.NextDouble()`.
fn select_weighted_fish(
    npcs: &[super::npc::spawner_data::NpcSpawnerNpc],
    roll01: f64,
) -> &super::npc::spawner_data::NpcSpawnerNpc {
    let total_weight: f32 = npcs.iter().map(|n| n.weight).sum();
    if total_weight <= 0.0 {
        return &npcs[0];
    }
    let roll = roll01 * f64::from(total_weight);
    let mut current = 0.0f64;
    for entry in npcs {
        current += f64::from(entry.weight);
        if roll < current {
            return entry;
        }
    }
    &npcs[0]
}

/// `SpawnFishEffect.GetFishSpawnerId(player)`: the `DoodadFuncFishSchool.NpcSpawnerId` of the
/// first current phase func of a fish school doodad (group 65) within 100 m (0 = none).
fn get_fish_spawner_id(server: &GameServer, player: &WorldObject) -> u32 {
    let data = &server.game_data.systems.npcs.doodad;
    for o in around_radius(server, &Target::Obj(player.clone()), 100.0, false) {
        let Some(doodad) = o.as_doodad() else {
            continue;
        };
        let (template_id, func_group_id) = {
            let d = doodad.data();
            (d.template_id, d.func_group_id)
        };
        if data.template(template_id).map(|t| t.group_id) != Some(FISH_SCHOOL_DOODAD_GROUP) {
            continue;
        }
        for func in data.phase_funcs(func_group_id) {
            if let Some(super::doodad::templates::FuncTemplate::FishSchool(f)) =
                data.phase_func_template(func.func_id, &func.func_type)
            {
                return f.npc_spawner_id;
            }
        }
    }
    0
}

/// The `DoodadGroups` id of fish school doodads (`GetFishSpawnerId`).
const FISH_SCHOOL_DOODAD_GROUP: u32 = 65;

/// `NpcSpawnerSpawnEffect.Apply`.
fn npc_spawner_spawn_effect(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::NpcSpawnerSpawnEffect,
    args: &EffectArgs<'_>,
) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    tracing::info!(
        "NpcSpawnerSpawnEffect: SpawnerId={}, LifeTime={}, UseSummonerAggroTarget={}, ActivationState={}",
        e.spawner_id,
        e.life_time,
        e.use_summoner_aggro_target,
        e.activation_state
    );
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    let manager = server.systems.npcs.spawns.get_or_create(&world);
    let spawners = manager.get_npc_spawner(e.spawner_id);
    if spawners.is_empty() {
        tracing::info!(
            "NpcSpawnerSpawnEffect: SpawnerId={} not found in spawners.",
            e.spawner_id
        );
        return;
    }
    let world_id = caster.transform().world_id;
    for spawner in spawners {
        // spawn in the same world as for caster
        let mut position = spawner.position();
        position.world_id = world_id;
        spawner.set_position(position);
        let existing: Vec<_> = spawner.spawned_npcs().into_values().flatten().collect();
        let Some(npc) = spawner.force_spawn(server, 0) else {
            continue;
        };
        let spawned: Vec<_> = spawner
            .spawned_npcs()
            .into_values()
            .flatten()
            .filter(|n| !existing.iter().any(|old| std::sync::Arc::ptr_eq(old, n)))
            .collect();
        crate::systems::instances::tower_def::on_summoned(server, caster, &spawned);
        if let Some(s) = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
            s.set_respawn_time(0);
        }
        if e.use_summoner_aggro_target && e.life_time != 0.0 {
            // Npc attacks the character (or the targeted NPC). `Ai.OnAggroTargetChanged` and
            // the `LifeTime == 0` NPC-vs-NPC branch (sight range) are the AI's.
            let npc_obj = WorldObject::from(&npc);
            let victim = match &args.target {
                Target::Obj(o) if o.as_npc().is_some() => o.clone(),
                _ => caster.clone(),
            };
            crate::systems::skills::combat::add_unit_aggro(
                server,
                &npc_obj,
                crate::systems::skills::state::AggroKind::Damage,
                &victim,
                1,
            );
        }
        if e.life_time > 0.0 {
            super::npc::spawner::schedule_npc_do_despawn(server, &npc, e.life_time);
        }
    }
}

/// `KillNpcWithoutCorpseEffect.Apply` (mirrors the C#: the *caster* is removed for every
/// matching NPC around).
fn kill_npc_without_corpse(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::KillNpcWithoutCorpseEffect,
    args: &EffectArgs<'_>,
) {
    tracing::info!(
        "KillNpcWithoutCorpseEffect npc={}, Radius={}, Vanish={}",
        e.npc_id,
        e.radius,
        e.vanish
    );
    let Some(caster) = &args.caster else {
        return;
    };
    if caster.as_player().is_some() {
        return; // does not apply to the character
    }
    if e.vanish {
        remove_effects_and_delete(server, caster);
        return;
    }
    let mut npcs: Vec<WorldObject> = around_radius(server, &args.target, e.radius, false)
        .into_iter()
        .filter(|o| o.as_npc().is_some())
        .collect();
    if caster.as_npc().is_some() {
        npcs.push(caster.clone());
    }
    for n in npcs {
        let template_id = n.as_npc().map_or(0, |n| n.data().unit.template_id);
        if template_id == e.npc_id {
            remove_effects_and_delete(server, caster);
        }
    }
}

/// `KillNpcWithoutCorpseEffect.RemoveEffectsAndDelete(unit)`.
fn remove_effects_and_delete(server: &GameServer, unit: &WorldObject) {
    crate::systems::skills::buffs::remove_all_effects(server, unit);
    crate::systems::dispatch_buff_events(server, unit);
    let Some(npc) = unit.as_npc() else {
        return;
    };
    if let Some(s) = server.systems.npcs.spawns.npc_spawner_of(npc.obj_id) {
        s.do_despawn(server, npc);
        s.despawn_with_respawn(server, npc);
    }
}

/// `NpcControlEffect.Apply` on an NPC target (`systems::ai`: follow path, command sets,
/// `AttackUnit` -> `SetFaction(Monstrosity)`).
fn npc_control(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::NpcControlEffect,
    args: &EffectArgs<'_>,
) {
    if let Some(n) = args.target.as_obj().and_then(WorldObject::as_npc) {
        crate::systems::ai::hooks::npc_control_effect(server, e, n);
    }
}

/// `SpawnGimmickEffect.Apply`.
fn spawn_gimmick_effect(
    server: &GameServer,
    e: &crate::systems::skills::data::effects::SpawnGimmickEffect,
    args: &EffectArgs<'_>,
) {
    let Some(caster) = &args.caster else {
        return;
    };
    if !caster.is_unit() {
        return;
    }
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    tracing::trace!(
        "SpawnGimmickEffect GimmickId={}, scale={}",
        e.gimmick_id,
        e.scale
    );
    let params = super::gimmick::manager::SpawnGimmickParams {
        gimmick_id: e.gimmick_id,
        offset_from_source: e.offset_from_source,
        offset_coordinate_id: e.offset_coordinate_id as i32,
        offset_x: e.offset[0],
        offset_y: e.offset[1],
        offset_z: e.offset[2],
        scale: e.scale,
        velocity_coordinate_id: e.velocity_coordinate_id as i32,
        velocity_x: e.velocity[0],
        velocity_y: e.velocity[1],
        velocity_z: e.velocity[2],
        ang_vel_coordinate_id: e.ang_vel_coordinate_id as i32,
        ang_vel_x: e.ang_vel[0],
        ang_vel_y: e.ang_vel[1],
        ang_vel_z: e.ang_vel[2],
    };
    super::gimmick::manager::spawn_from_effect(server, &world, &params, caster);
}

/// `OpenPortalEffect.Apply`.
fn open_portal_effect(server: &GameServer, distance: f32, args: &EffectArgs<'_>) {
    let Some(owner) = args.caster.as_ref().and_then(WorldObject::as_player) else {
        tracing::error!("OpenPortalEffect: caster is not a character (InvalidCastException)");
        return;
    };
    let SkillObject::PortalInfo { kind, id, x, y, z } = args.skill_object.clone() else {
        tracing::error!("OpenPortalEffect: no SkillObjectPortalInfo (InvalidCastException)");
        return;
    };
    let me = WorldObject::from(owner);
    let pos = match server.world.world_of(&me) {
        Some(w) => w.world_pos_rot(&me),
        None => owner.data().unit.transform.local(),
    };
    if x > pos.x + distance || y > pos.y + distance {
        return;
    }
    let effect = super::portal::PortalEffectObject { kind, id, x, y, z };
    super::portal::open_portal(server, owner, &effect);
}

/// `ScopedFEffect.Apply`: the first doodad of `doodad_id` within `range / 1000` m is used.
fn scoped_f_effect(server: &GameServer, range: i32, doodad_id: u32, args: &EffectArgs<'_>) {
    tracing::trace!("ScopedFEffect");
    let Some(caster) = &args.caster else {
        return;
    };
    if caster.as_player().is_none() {
        return;
    }
    let skill_id = args.source.skill().map_or(0, |s| s.template.id);
    let around = around_radius(
        server,
        &Target::Obj(caster.clone()),
        range as f32 / 1000.0,
        false,
    );
    for o in around {
        let Some(d) = o.as_doodad() else {
            continue;
        };
        if d.data().template_id != doodad_id {
            continue;
        }
        if let Some(world) = server.world.world_of(&o) {
            doodads::use_doodad(server, &world, d, Some(caster), skill_id, 0);
        }
        break;
    }
}

// ---------------------------------------------------------------------------------------------
// Economy hooks (shops, loot, treasure maps)
// ---------------------------------------------------------------------------------------------

/// The NPC subsystem's implementations of the economy hooks: `NpcEconomyHooks` (shop flags,
/// merchant goods, specialty coin, coin item funcs), `LootNpcHooks` (tagging, despawn times,
/// func-driven loot doodads) and `TreasureMapSource` (treasure chest spawners).
pub struct NpcsForEconomy {
    server: std::sync::Weak<GameServer>,
}

/// Installs the NPC subsystem's implementations of the economy hooks.
pub fn install_economy(server: &GameServer) {
    let me = Arc::new(NpcsForEconomy {
        server: server
            .self_arc()
            .map_or_else(std::sync::Weak::new, |s| Arc::downgrade(&s)),
    });
    let e = &server.systems.economy;
    e.hooks.npcs.install(me.clone());
    e.loot.hooks.npcs.install(me.clone());
    server.items.treasure_maps.install(me);
}

impl crate::systems::economy::hooks::NpcEconomyHooks for NpcsForEconomy {
    fn shop_info(
        &self,
        server: &GameServer,
        npc: &NpcRef,
    ) -> Option<crate::systems::economy::hooks::NpcShopInfo> {
        let template_id = npc.data().unit.template_id;
        let t = server.game_data.systems.npcs.npc.template(template_id)?;
        Some(crate::systems::economy::hooks::NpcShopInfo {
            merchant: t.merchant,
            merchant_pack_id: t.merchant_pack_id,
            blacksmith: t.blacksmith,
            auctioneer: t.auctioneer,
            banker: t.banker,
            specialty: t.specialty,
            stabler: t.stabler,
            priest: t.priest,
        })
    }

    fn merchant_sells(&self, server: &GameServer, pack_id: u32, item_id: u32) -> bool {
        server
            .game_data
            .systems
            .npcs
            .npc
            .goods
            .get(&pack_id)
            .is_some_and(|g| g.sells_item(item_id))
    }

    fn specialty_coin_id(&self, server: &GameServer, npc: &NpcRef) -> u32 {
        let template_id = npc.data().unit.template_id;
        server
            .game_data
            .systems
            .npcs
            .npc
            .template(template_id)
            .map_or(0, |t| t.specialty_coin_id)
    }

    fn buy_coin_item(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        doodad: &crate::world::DoodadRef,
        id: u32,
    ) {
        // DoodadManager.GetFunc(id) + GetFuncTemplate(func.FuncId, func.FuncType) +
        // funcTemplate.Use(character, doodad, 0)
        let data = &server.game_data.systems.npcs.doodad;
        let template = data
            .func(id)
            .and_then(|f| data.func_template(f.func_id, &f.func_type));
        let (Some(template), Some(ctx)) = (
            template,
            super::doodad::runtime::DoodadCtx::of(server, doodad),
        ) else {
            tracing::warn!(
                "BuyCoinItem, no doodadFunc for objId: {}, id: {id}",
                doodad.obj_id
            );
            return;
        };
        let caster = WorldObject::from(player);
        super::doodad::funcs::use_func(template, &ctx, Some(&caster), 0, 0);
    }
}

impl crate::systems::economy::looting::LootNpcHooks for NpcsForEconomy {
    fn tagging(
        &self,
        server: &GameServer,
        npc: &NpcRef,
    ) -> crate::systems::economy::looting::NpcTagging {
        let (team, tagger) = {
            let n = npc.data();
            let t = &n.unit.skill_state.tagging;
            (
                if t.tag_team != 0 {
                    t.tag_team
                } else {
                    n.tag_team
                },
                if t.tagger != 0 {
                    t.tagger
                } else {
                    n.tagger_obj_id
                },
            )
        };
        crate::systems::economy::looting::NpcTagging {
            tag_team: team,
            tagger: (tagger != 0)
                .then(|| server.world.character_by_obj_id(tagger))
                .flatten(),
        }
    }

    fn spawner_despawn_time(&self, server: &GameServer, npc: &NpcRef) -> Option<f64> {
        server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(npc.obj_id)
            .map(|s| f64::from(s.despawn_time))
    }

    fn set_despawn(&self, server: &GameServer, npc: &NpcRef, at_ms: i64) {
        let delta = (at_ms - chrono::Utc::now().timestamp_millis()) as f64 / 1000.0;
        let at = super::spawn_manager::add_secs(super::spawn_manager::now(), delta);
        server.systems.npcs.spawns.set_despawn_field(npc.obj_id, at);
    }

    fn try_handle_func_driven_loot(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        doodad: &WorldObject,
    ) -> bool {
        /// `CSLootOpenBagPacket.GenericRecoverItemSkillId`.
        const GENERIC_RECOVER_ITEM_SKILL_ID: u32 = 11361;
        let Some(d) = doodad.as_doodad() else {
            return false;
        };
        if !d.data().has_loot_item() {
            return false;
        }
        let Some(world) = server.world.world_of(doodad) else {
            return false;
        };
        let group = d.data().func_group_id;
        let recover = server
            .game_data
            .systems
            .npcs
            .doodad
            .doodad_funcs(group)
            .iter()
            .any(|f| {
                f.func_type == "DoodadFuncRecoverItem"
                    && f.skill_id == GENERIC_RECOVER_ITEM_SKILL_ID
            });
        if recover {
            // new RecoverItem().Execute(character, null, doodad, null, skillId, 0, null)
            super::doodad::interactions::execute_interaction(
                server,
                super::doodad::interactions::world_interaction_type::RECOVER_ITEM,
                &WorldObject::from(player),
                Some(doodad),
                GENERIC_RECOVER_ITEM_SKILL_ID,
                0,
                None,
            );
            return true;
        }
        doodads::loot_open_bag_doodad(server, &world, player, d);
        true
    }

    fn distance_to(&self, server: &GameServer, a: &WorldObject, b: &WorldObject) -> Option<f32> {
        Some(crate::systems::skills::geometry::distance_to(
            server,
            &Target::Obj(a.clone()),
            &Target::Obj(b.clone()),
            false,
        ))
    }
}

impl crate::managers::item::TreasureMapSource for NpcsForEconomy {
    fn pick_treasure_chest(&self) -> Option<crate::models::item::TreasureMapTarget> {
        use rand::Rng;
        let server = self.server.upgrade()?;
        let spawns = server
            .systems
            .npcs
            .spawns
            .get(crate::models::character::DEFAULT_INSTANCE_ID)?;
        let chests = spawns.get_treasure_chest_doodad_spawners(&server);
        if chests.is_empty() {
            return None;
        }
        let chest = &chests[rand::thread_rng().gen_range(0..chests.len())];
        let data = &server.game_data.systems.npcs.doodad;
        let group = data
            .template(chest.unit_id)
            .or_else(|| data.template(chest.respawn_doodad_template_id()))
            .map(|t| t.group_id);
        Some(crate::models::item::TreasureMapTarget {
            x: chest.position.x,
            y: chest.position.y,
            chest_group_id: group,
        })
    }
}

#[cfg(test)]
mod spawn_fish_tests {
    use super::super::npc::spawner_data::NpcSpawnerNpc;
    use super::select_weighted_fish;

    fn npc(member_id: u32, weight: f32) -> NpcSpawnerNpc {
        NpcSpawnerNpc {
            id: member_id,
            npc_spawner_template_id: 1,
            member_id,
            member_type: "Npc".into(),
            weight,
            unit_id: 0,
        }
    }

    #[test]
    fn weighted_fish_selection_follows_the_roll() {
        let npcs = [npc(10, 1.0), npc(20, 3.0), npc(30, 0.0)];
        // total 4: [0, 1) -> 10, [1, 4) -> 20
        assert_eq!(select_weighted_fish(&npcs, 0.0).member_id, 10);
        assert_eq!(select_weighted_fish(&npcs, 0.2499).member_id, 10);
        assert_eq!(select_weighted_fish(&npcs, 0.25).member_id, 20);
        assert_eq!(select_weighted_fish(&npcs, 0.9999).member_id, 20);
        // no weight: the first entry
        let zero = [npc(1, 0.0), npc(2, 0.0)];
        assert_eq!(select_weighted_fish(&zero, 0.7).member_id, 1);
    }
}
