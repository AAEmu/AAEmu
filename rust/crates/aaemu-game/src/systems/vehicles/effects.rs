//! Skill effects owned by the vehicles subsystem (routed through the `NpcSkillHooks` wrapper):
//! `RepairSlaveEffect`, the shipyard branches of `CraftEffect`, and the backpack drop of a
//! destroyed vehicle (`Slave.DestroyAttachedItems`).

use std::sync::Arc;

use rand::Rng;

use crate::models::item::ItemClass;
use crate::models::unit::BaseUnitType;
use crate::server::GameServer;
use crate::systems::npcs::area_trigger::world_interaction_group;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::types::{CastAction, SkillCastTarget};
use crate::world::{DoodadRef, WorldInstance, WorldObject};

use super::packets::{item_task_type, ItemUpdateTask, SCItemTaskSuccessPacket};
use super::{error, shipyards};

/// Handles the effects of this subsystem; `false` = not ours.
pub fn apply_effect(
    server: &GameServer,
    kind: EffectKind,
    template: &EffectTemplate,
    args: &EffectArgs<'_>,
) -> bool {
    match (kind, template) {
        (EffectKind::RepairSlaveEffect, EffectTemplate::RepairSlave { id, .. }) => {
            repair_slave(server, *id, args);
            true
        }
        (
            EffectKind::CraftEffect,
            EffectTemplate::Craft {
                world_interaction, ..
            },
        ) => craft_on_shipyard(server, *world_interaction, args),
        (EffectKind::SpawnEffect, EffectTemplate::Spawn(t))
            if t.owner_type_id == BaseUnitType::Slave as u32 =>
        {
            spawn_effect_slave(server, t, args);
            true
        }
        // `case BaseUnitType.Mate: break;`
        (EffectKind::SpawnEffect, EffectTemplate::Spawn(t))
            if t.owner_type_id == BaseUnitType::Mate as u32 =>
        {
            true
        }
        _ => false,
    }
}

/// The `BaseUnitType.Slave` branch of `SpawnEffect.Apply` (e.g. Mirage test vehicles): a slave
/// without an owner in front of the caster, despawned after 500 s
/// (`ForceDismountAndDespawn(slave, 500000)`).
fn spawn_effect_slave(
    server: &GameServer,
    t: &crate::systems::skills::data::effects::SpawnEffect,
    args: &EffectArgs<'_>,
) {
    tracing::trace!(
        "SpawnEffect: OwnerTypeId={}, SubType={}, UseSummonerFaction={}, LifeTime={}",
        t.owner_type_id,
        t.sub_type,
        t.use_summoner_faction,
        t.life_time
    );
    let Some(player) = args
        .caster
        .as_ref()
        .and_then(WorldObject::as_player)
        .cloned()
    else {
        return;
    };
    let me = WorldObject::from(&player);
    let Some(world) = super::world_of(server, &me) else {
        return;
    };
    // TODO (C#): Implement OriDirId, PosDirId and MateStateId
    let w = world.world_pos_rot(&me);
    let mut transform = crate::models::transform::Transform {
        instance_id: world.id,
        zone_id: player.data().unit.transform.zone_id,
        ..Default::default()
    };
    transform.set_position(w.x, w.y, w.z, w.rot_x, w.rot_y, w.rot_z);
    // `if (PosDistance == 0) { PosDistance = 2; }` (stored back into the template in C#)
    let distance = if t.pos_distance == 0.0 {
        2.0
    } else {
        t.pos_distance
    };
    super::slaves::add_distance_to_front(&mut transform, distance);
    // `Rotate(Rotation with { Z = OriAngle.DegToRad() })` adds the vector to the rotation.
    transform.rot_x += transform.rot_x;
    transform.rot_y += transform.rot_y;
    transform.rot_z += t.ori_angle * std::f32::consts::PI / 180.0;
    let slave = super::slaves::create_core(
        server,
        &world,
        None,
        None,
        t.sub_type,
        None,
        true,
        Some(transform),
        None,
    );
    if slave.is_none()
        && server
            .game_data
            .systems
            .vehicles
            .slave_template(t.sub_type)
            .is_none()
    {
        tracing::info!("SpawnEffect: SubType={} not found...", t.sub_type);
    }
    super::force_dismount_and_despawn_test_slave(server, &player, slave.as_ref(), 500_000);
}

/// `RepairSlaveEffect.Apply`.
pub fn repair_slave(server: &GameServer, effect_id: u32, args: &EffectArgs<'_>) {
    let SkillCastTarget::Item { obj_id, id, .. } = args.target_caster else {
        tracing::warn!("RepairSlaveEffect target is not a SkillCastItemTarget");
        return;
    };
    let Some(target_player) = server.world.character_by_obj_id(obj_id) else {
        tracing::warn!("RepairSlaveEffect target unit {obj_id} is not a player");
        return;
    };
    let item = super::mates::owned_item(server, &target_player, id);
    let Some(item) = item.filter(|i| i.class == ItemClass::SummonSlave) else {
        tracing::warn!("RepairSlaveEffect target item {id} is not a salve summon item");
        return;
    };
    let slave_id = match server
        .game_data
        .items
        .template(item.template_id)
        .map(|t| &t.kind)
    {
        Some(crate::data::items::TemplateKind::SummonSlave { slave_id }) => *slave_id,
        _ => return,
    };
    if !server
        .game_data
        .systems
        .vehicles
        .has_repair_effect_id(slave_id, effect_id)
    {
        error(&target_player, error::ITEM_FAILED_REPAIR);
        tracing::warn!(
            "{} tried to use the wrong repair item {} (template: {} for slave type {slave_id}",
            target_player.data().unit.name,
            item.id,
            item.template_id
        );
        return;
    }
    let now = chrono::Utc::now().timestamp();
    let task = super::mates::update_owned_item(server, Some(&target_player), id, |i| {
        i.details.slave_is_destroyed = 0;
        // RepairStartTime setter (a set time also clears IsDestroyed)
        i.details.slave_repair_start_time = now;
        i.dirty = true;
        ItemUpdateTask::of(i)
    });
    if let Some(task) = task {
        target_player.send(SCItemTaskSuccessPacket {
            action: item_task_type::REPAIR_SLAVES,
            tasks: vec![task],
        });
    }
    tracing::debug!(
        "{} repaired slave on item {}",
        target_player.data().unit.name,
        item.id
    );
}

/// The shipyard branches of `CraftEffect.Apply` (`WorldInteractionGroup.Craft` on a shipyard
/// target, and the "no wi group" completion). `false` = not a shipyard target (economy).
fn craft_on_shipyard(server: &GameServer, world_interaction: u32, args: &EffectArgs<'_>) -> bool {
    let Some(character) = args
        .caster
        .as_ref()
        .and_then(WorldObject::as_player)
        .cloned()
    else {
        return false;
    };
    let Some(shipyard) = args
        .target
        .as_obj()
        .and_then(WorldObject::as_shipyard)
        .cloned()
    else {
        return false;
    };
    tracing::debug!("CraftEffect, {world_interaction}");
    let wi_group = server
        .game_data
        .systems
        .npcs
        .area_trigger
        .get_world_interaction_group(world_interaction);
    let used_skill = match args.cast {
        CastAction::Skill { skill_id, .. } => skill_id,
        _ => 0,
    };
    let items = server.systems.vehicles.hooks.items();
    match wi_group {
        Some(world_interaction_group::CRAFT) => {
            let step = shipyards::current_step_template(server, &shipyard);
            match step {
                Some(skill_id) if used_skill != skill_id => {
                    tracing::warn!(
                        "{} tried to build a ship using the wrong skill, {used_skill} instead of {skill_id}",
                        character.data().unit.name
                    );
                    if let Some(s) = args.source.skill() {
                        s.state().cancelled = true;
                    }
                }
                _ => {
                    shipyards::add_build_action(server, &shipyard);
                    shipyards::sync_build_state(server, &shipyard);
                    let data = shipyard.data().data.clone();
                    super::broadcast(
                        server,
                        &WorldObject::from(&character),
                        &crate::packets::world_objects::SCShipyardStatePacket { data },
                        true,
                    );
                    items.end_craft(server, &character);
                }
            }
        }
        Some(world_interaction_group::COLLECT) => items.end_craft(server, &character),
        Some(world_interaction_group::BUILDING) => {
            // `Building when target is House`: not a house -> default branch.
            default_branch(server, &character, &shipyard, world_interaction, wi_group);
        }
        _ => default_branch(server, &character, &shipyard, world_interaction, wi_group),
    }
    let tpl = shipyard.data().unit.template_id;
    crate::systems::quests::events::on_doodad_interaction(server, &character, &character, tpl);
    true
}

fn default_branch(
    server: &GameServer,
    character: &crate::world::PlayerRef,
    shipyard: &crate::world::ShipyardRef,
    world_interaction: u32,
    wi_group: Option<u32>,
) {
    tracing::warn!("CraftEffect, {world_interaction} does not have a wi group ({wi_group:?})");
    let (owner_name, caster_name) = {
        let s = shipyard.data().data.owner_name.clone();
        (s, character.data().unit.name.clone())
    };
    if owner_name == caster_name {
        shipyards::shipyard_completed_task(server, shipyard);
    } else {
        error(character, error::NO_PERMISSION_TO_LOOT);
    }
}

/// The backpack part of `Slave.DestroyAttachedItems`: a backpack carried by a vehicle doodad
/// is dropped as its `PutDownBackpackEffect` doodad near the vehicle doodad, and the carrying
/// doodad is reset through its `DoodadFuncRecoverItem` phase.
pub fn drop_carried_backpack(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    doodad: &DoodadRef,
    item_id: u64,
) {
    let Some(item) = server.items.item_by_id(item_id) else {
        return;
    };
    if item.class != ItemClass::Backpack {
        return;
    }
    let Some((use_skill_id, _)) = server
        .systems
        .skills
        .ext
        .items()
        .item_use_skill(server, item.template_id)
    else {
        return;
    };
    let skills = &server.game_data.systems.skills;
    let Some(skill) = skills.skill(use_skill_id).cloned() else {
        tracing::error!("Backpack skill {use_skill_id} missing (NullReferenceException)");
        return;
    };
    let physics = server.systems.vehicles.hooks.physics();
    for e in &skill.effects {
        let Some((kind, id)) = e.template else {
            continue;
        };
        let Some(EffectTemplate::PutDownBackpack {
            backpack_doodad_id, ..
        }) = skills.effect(kind, id)
        else {
            continue;
        };
        let Some(new_doodad) = crate::systems::npcs::doodad::manager::create(
            server,
            world,
            0,
            *backpack_doodad_id,
            None,
            true,
        ) else {
            tracing::warn!(
                "Dropped Doodad {backpack_doodad_id}, from BackpackDoodadId could not be created"
            );
            break;
        };
        let pos = world.world_pos_rot(&WorldObject::from(doodad));
        let mut rng = rand::thread_rng();
        let (x, y) = (
            pos.x + rng.gen::<f32>() * 2.0 - 1.0,
            pos.y + rng.gen::<f32>() * 2.0 - 1.0,
        );
        let floor =
            crate::systems::npcs::heightmap::world_height(server, world, x, y).unwrap_or(0.0);
        let surface = physics.water_surface(server, world, [x, y, pos.z]);
        let depth = surface - floor;
        let z = if depth < 30.0 {
            floor
        } else {
            floor.max(surface)
        };
        {
            let mut d = new_doodad.data();
            d.is_persistent = true;
            d.transform.instance_id = world.id;
            d.transform
                .set_position(x, y, z, pos.rot_x, pos.rot_y, pos.rot_z);
            d.attach_point = 0;
            d.item_id = item.id;
            d.item_template_id = item.template_id;
            d.ucc_id = item.ucc_id;
            d.scale = 1.0;
            d.plant_time = chrono::Utc::now().timestamp();
            d.faction_id = crate::data::world_data::factions::FRIENDLY;
        }
        crate::systems::npcs::doodad::manager::init_doodad(server, world, &new_doodad);
        crate::systems::npcs::doodad::manager::spawn(server, world, &new_doodad);
        crate::systems::npcs::doodad::db::save(server, &new_doodad);
        let func_group = {
            let mut d = doodad.data();
            d.item_template_id = 0;
            d.item_id = 0;
            d.func_group_id
        };
        let next = server
            .game_data
            .systems
            .npcs
            .doodad
            .doodad_funcs(func_group)
            .iter()
            .find(|f| f.func_type == "DoodadFuncRecoverItem")
            .map(|f| f.next_phase);
        if let Some(next) = next {
            crate::systems::npcs::doodad::manager::do_change_phase(
                server, world, doodad, None, next,
            );
        }
        crate::systems::npcs::doodad::db::save(server, doodad);
    }
}
