//! `SlaveManager` (one per world instance in C#; here the state lives on the world objects and
//! the slave spawners), `SlaveSpawner`, `SummonSlave` item handling and the `Slave` behaviour
//! (`DoDie`, `DoHpChangeTrigger` / repair points, `RegenTick`, `Save`, `OnZoneChange`).
//!
//! Locking: one object data lock at a time; world calls never hold a data lock.

use std::sync::{Arc, Mutex};

use dashmap::DashMap;
use rand::Rng;
use sqlx::Row;

use crate::data::world_templates::WorldSpawnPosition;
use crate::models::doodad::doodad_owner_type;
use crate::models::item::{Item, ItemClass};
use crate::models::slave::Slave;
use crate::models::transform::Transform;
use crate::models::unit::{BaseUnitType, Bonus};
use crate::packets::world::SCUnitPointsPacket;
use crate::packets::world_objects::SCUnitAttachedPacket;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::SimpleSpawnerDef;
use crate::systems::npcs::spawn_manager;
use crate::systems::skills::packets::SCUnitDeathPacket;
use crate::systems::skills::types::{
    BuffEventTriggerKind, BuffRemoveOn, KillReason, SkillCastTarget, SkillCaster,
};
use crate::world::{Entity, PlayerRef, SlaveRef, WorldInstance, WorldObject};

use super::data::{attach_point, SlaveTemplate};
use super::packets::{
    item_task_type, SCItemTaskSuccessPacket, SCMySlavePacket, SCSlaveBoundPacket,
    SCSlaveCreatedPacket, SCSlaveDespawnPacket, SCSlaveRemovedPacket, SCUnitDetachedPacket,
};
use super::{broadcast, error, harpoon, now_ms, stats, world_of};

/// `AttachUnitReason`.
pub mod attach_reason {
    pub const NONE: u8 = 0;
    pub const SLAVE_BINDING: u8 = 5;
    pub const NEW_MASTER: u8 = 6;
    pub const BOARD_TRANSFER: u8 = 7;
    pub const PREFAB_CHANGED: u8 = 10;
}

/// `BuffConstants` used here.
pub mod buff {
    pub const UNTOUCHABLE: u32 = 545;
    pub const DETERIORATION: u32 = 3553;
    pub const TAX_PROTECTION: u32 = 3554;
    pub const OWNERS_MARK: u32 = 4867;
}

/// `Buoyancy.BaseWaterDensity` / `ShipWaterDensityMul` (physics constants used by the spawn
/// height adjustment).
const BASE_WATER_DENSITY: f32 = 1.025;
const SHIP_WATER_DENSITY_MUL: f32 = 3.0;

// ---------------------------------------------------------------------------------------------
// SlaveSpawner
// ---------------------------------------------------------------------------------------------

#[derive(Debug, Default)]
struct SpawnerState {
    spawned: Vec<u32>,
    last_spawn: Option<u32>,
    scheduled_count: i32,
    spawn_count: i32,
}

/// `SlaveSpawner` (`Spawner<Slave>` + `World`).
#[derive(Debug)]
pub struct SlaveSpawner {
    pub id: u32,
    pub world_id: u32,
    pub def: SimpleSpawnerDef,
    state: Mutex<SpawnerState>,
}

impl SlaveSpawner {
    pub fn new(world_id: u32, def: SimpleSpawnerDef) -> Self {
        Self {
            id: def.base.id,
            world_id,
            def,
            state: Mutex::new(SpawnerState::default()),
        }
    }

    /// `Spawn(objId)` (`DoSpawn`): returns `_lastSpawn`.
    pub fn spawn(self: &Arc<Self>, server: &GameServer) -> Option<u32> {
        let world = server.world.world(self.world_id)?;
        match create_core(server, &world, None, Some(self), 0, None, false, None, None) {
            None => {
                tracing::warn!(
                    "Slave {}, from spawn not exist at db",
                    self.def.base.unit_id
                );
            }
            Some(slave) => {
                let at_origin = {
                    let t = slave.data().unit.transform;
                    t.x == 0.0 && t.y == 0.0 && t.z == 0.0
                };
                if at_origin {
                    tracing::error!(
                        "Can't spawn slave {} from spawn {}",
                        self.def.base.unit_id,
                        self.id
                    );
                } else {
                    let mut st = self.state.lock().expect("spawner poisoned");
                    st.last_spawn = Some(slave.obj_id);
                    // C#: `_spawned` / counts are not updated by DoSpawn.
                }
            }
        }
        self.state.lock().expect("spawner poisoned").last_spawn
    }

    /// `DecreaseCount(slave)`: schedules the respawn (when `RespawnTime > 0` and below `Count`)
    /// and the despawn of a slave.
    pub fn decrease_count(&self, server: &GameServer, slave: &SlaveRef) {
        let Some(sm) = spawn_manager_of(server, self.world_id) else {
            return;
        };
        let now = spawn_manager::now();
        let respawn = {
            let mut st = self.state.lock().expect("spawner poisoned");
            st.spawn_count -= 1;
            st.spawned.retain(|id| *id != slave.obj_id);
            let r = self.def.base.respawn_time > 0
                && st.spawn_count + st.scheduled_count < self.def.count as i32;
            if r {
                st.scheduled_count += 1;
            }
            r
        };
        let obj = WorldObject::from(slave);
        if respawn {
            sm.add_respawn(
                server,
                obj.clone(),
                Some(spawn_manager::add_secs(
                    now,
                    f64::from(self.def.base.respawn_time),
                )),
            );
        }
        sm.add_despawn(
            server,
            obj,
            Some(spawn_manager::add_secs(
                now,
                f64::from(self.def.base.despawn_time),
            )),
        );
    }

    /// `Despawn(slave)`.
    pub fn despawn(&self, server: &GameServer, slave: &SlaveRef) {
        if let Some(world) = server.world.world_of(&WorldObject::from(slave)) {
            world.delete(slave);
        }
        let mut st = self.state.lock().expect("spawner poisoned");
        if server
            .systems
            .npcs
            .spawns
            .respawn_field(slave.obj_id)
            .is_none()
        {
            st.spawned.retain(|id| *id != slave.obj_id);
            server.ids.object.release_id(slave.obj_id);
            st.spawn_count -= 1;
        }
        if st.last_spawn.is_none_or(|l| l == slave.obj_id) {
            st.last_spawn = st.spawned.last().copied();
        }
    }
}

/// The loaded `SlaveSpawners` of every instance (`spawner id -> spawner`).
#[derive(Debug, Default)]
pub struct SlaveSpawners {
    spawners: DashMap<u32, Arc<SlaveSpawner>>,
}

impl SlaveSpawners {
    pub fn get(&self, id: u32) -> Option<Arc<SlaveSpawner>> {
        self.spawners.get(&id).map(|s| s.clone())
    }

    pub fn insert(&self, s: Arc<SlaveSpawner>) {
        self.spawners.insert(s.id, s);
    }
}

/// The slave part of `SpawnManager.SpawnAll` for one instance: `spawner.World = World;
/// spawner.Spawn(0)` per loaded spawner. Returns the number of spawners processed.
pub fn spawn_all(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    defs: Vec<SimpleSpawnerDef>,
) -> usize {
    let mut count = 0;
    for def in defs {
        let s = Arc::new(SlaveSpawner::new(world.id, def));
        server.systems.vehicles.slave_spawners.insert(s.clone());
        s.spawn(server);
        count += 1;
        if count % 25 == 0 {
            tracing::debug!(
                "In world {} Slaves spawned: {count}...",
                world.template.name
            );
        }
    }
    tracing::info!("In world {} slaves spawned: {count}", world.template.name);
    count
}

// ---------------------------------------------------------------------------------------------
// Lookups
// ---------------------------------------------------------------------------------------------

/// `World.GetAllSlaves()`.
pub fn all_slaves(world: &WorldInstance) -> Vec<SlaveRef> {
    world
        .all_of_kind(crate::world::ObjectKind::Slave)
        .into_iter()
        .filter_map(|o| o.as_slave().cloned())
        .collect()
}

/// `GetActiveSlaveByOwnerObjId(objId)`.
pub fn get_active_slave_by_owner_obj_id(world: &WorldInstance, obj_id: u32) -> Option<SlaveRef> {
    all_slaves(world).into_iter().find(|s| {
        let d = s.data();
        d.summoner_obj_id != 0 && d.summoner_obj_id == obj_id && !d.unit.is_dead()
    })
}

/// `GetActiveSlavesByKinds(kinds, worldId)` (`u32::MAX` = every world template).
pub fn get_active_slaves_by_kinds(
    server: &GameServer,
    world: &WorldInstance,
    kinds: &[u32],
    world_id: u32,
) -> Vec<SlaveRef> {
    let data = &server.game_data.systems.vehicles;
    all_slaves(world)
        .into_iter()
        .filter(|s| {
            let (tpl, wid) = {
                let d = s.data();
                (d.unit.template_id, d.unit.transform.world_id)
            };
            data.slave_template(tpl)
                .is_some_and(|t| kinds.contains(&t.slave_kind))
                && (world_id == u32::MAX || wid == world_id)
        })
        .collect()
}

/// `GetSlaveByTlId(tlId)`.
pub fn get_slave_by_tl_id(world: &WorldInstance, tl_id: u32) -> Option<SlaveRef> {
    all_slaves(world)
        .into_iter()
        .find(|s| u32::from(s.data().unit.tl_id) == tl_id)
}

/// `GetSlaveByDbId(dbId)`.
pub fn get_slave_by_db_id(world: &WorldInstance, db_id: u32) -> Option<SlaveRef> {
    all_slaves(world).into_iter().find(|s| s.data().id == db_id)
}

/// `GetIsMounted(objId, out attachPoint)`.
pub fn get_is_mounted(world: &WorldInstance, obj_id: u32) -> Option<(SlaveRef, u8)> {
    for s in all_slaves(world) {
        let p = s
            .data()
            .attached_characters
            .iter()
            .find(|(_, c)| *c == obj_id)
            .map(|(p, _)| *p);
        if let Some(p) = p {
            return Some((s, p));
        }
    }
    None
}

fn player_world(server: &GameServer, p: &PlayerRef) -> Option<Arc<WorldInstance>> {
    world_of(server, &WorldObject::from(p))
}

/// `SlaveManager.GetIsMounted(character.ObjId) ?? GetActiveSlaveByOwnerObjId(character.ObjId)`.
pub fn character_slave(server: &GameServer, p: &PlayerRef) -> Option<WorldObject> {
    let world = player_world(server, p)?;
    get_is_mounted(&world, p.obj_id)
        .map(|(s, _)| s)
        .or_else(|| get_active_slave_by_owner_obj_id(&world, p.obj_id))
        .map(WorldObject::from)
}

// ---------------------------------------------------------------------------------------------
// Bind / unbind
// ---------------------------------------------------------------------------------------------

/// `ClearSlaveAttachmentState(character)`.
fn clear_slave_attachment_state(server: &GameServer, character: &PlayerRef) {
    let me = WorldObject::from(character);
    if let Some(w) = world_of(server, &me) {
        w.detach_child(&me);
    }
    let mut c = character.data();
    c.is_riding = false;
    c.attached_point = attach_point::NONE;
}

fn trigger_remove_on(server: &GameServer, obj: &WorldObject, on: BuffRemoveOn) {
    crate::systems::skills::buffs::trigger_remove_on(server, obj, on, 0);
    crate::systems::dispatch_buff_events(server, obj);
}

/// `UnbindSlave(character, tlId, reason)`.
pub fn unbind_slave(server: &GameServer, character: &PlayerRef, tl_id: u32, reason: u8) {
    let me = WorldObject::from(character);
    let slave = player_world(server, character).and_then(|w| get_slave_by_tl_id(&w, tl_id));
    let Some(slave) = slave else {
        clear_slave_attachment_state(server, character);
        trigger_remove_on(server, &me, BuffRemoveOn::Unmount);
        broadcast(
            server,
            &me,
            &SCUnitDetachedPacket {
                obj_id: character.obj_id,
                reason,
            },
            true,
        );
        return;
    };
    let point = slave
        .data()
        .attached_characters
        .iter()
        .find(|(_, c)| *c == character.obj_id)
        .map_or(attach_point::NONE, |(p, _)| *p);
    if point != attach_point::NONE {
        slave
            .data()
            .attached_characters
            .retain(|(p, _)| *p != point);
        if point == attach_point::DRIVER {
            super::ship_physics::set_driver(server, &slave, false);
        }
        clear_slave_attachment_state(server, character);
        harpoon::on_operator_left_slave(server, &slave);
    }
    trigger_remove_on(server, &me, BuffRemoveOn::Unmount);
    clear_slave_attachment_state(server, character);
    broadcast(
        server,
        &me,
        &SCUnitDetachedPacket {
            obj_id: character.obj_id,
            reason,
        },
        true,
    );
}

/// `BindSlave(character, objId, attachPoint, bondKind)`.
pub fn bind_slave(
    server: &GameServer,
    character: &PlayerRef,
    obj_id: u32,
    point: u8,
    bond_kind: u8,
) {
    let Some(world) = player_world(server, character) else {
        return;
    };
    let Some(slave) = world.slave(obj_id) else {
        return;
    };
    let (taken, has_mark, summoner) = {
        let d = slave.data();
        (
            d.has_attached_at(point),
            d.unit.buffs.check_buff(buff::OWNERS_MARK),
            d.summoner_obj_id,
        )
    };
    if taken {
        return;
    }
    if point == attach_point::DRIVER && has_mark && summoner != character.obj_id {
        error(character, error::SLAVE_ALREADY_HAS_MASTER);
        return;
    }
    let me = WorldObject::from(character);
    broadcast(
        server,
        &me,
        &SCUnitAttachedPacket {
            child_obj_id: character.obj_id,
            point,
            reason: bond_kind,
            obj_id,
        },
        true,
    );
    character.data().attached_point = point;
    if point == attach_point::DRIVER {
        let char_id = character.data().id;
        broadcast(
            server,
            &me,
            &SCSlaveBoundPacket {
                master_id: char_id,
                slave_obj_id: obj_id,
            },
            true,
        );
    }
    slave
        .data()
        .attached_characters
        .push((point, character.obj_id));
    if point == attach_point::DRIVER {
        super::ship_physics::set_driver(server, &slave, true);
    }
    world.attach_child(&WorldObject::from(&slave), &me);
    // TODO (C#): move to attach point's position
    character
        .data()
        .unit
        .transform
        .set_position(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
}

/// `BindSlave(connection, tlId)` (`CSBindSlavePacket`).
pub fn bind_slave_by_tl(server: &GameServer, character: &PlayerRef, tl_id: u32) {
    let Some(world) = player_world(server, character) else {
        return;
    };
    let Some(slave) = get_slave_by_tl_id(&world, tl_id) else {
        return;
    };
    if slave.data().unit.is_dead() {
        return;
    }
    bind_slave(
        server,
        character,
        slave.obj_id,
        attach_point::DRIVER,
        attach_reason::NEW_MASTER,
    );
}

// ---------------------------------------------------------------------------------------------
// Delete / despawn
// ---------------------------------------------------------------------------------------------

fn spawn_manager_of(
    server: &GameServer,
    world_id: u32,
) -> Option<Arc<spawn_manager::SpawnManager>> {
    server.systems.npcs.spawns.get(world_id)
}

/// `Delete(owner, objId, ignoreAttachedItemWarning)`.
pub fn delete(
    server: &GameServer,
    owner: Option<&PlayerRef>,
    instance_id: u32,
    obj_id: u32,
    ignore_attached_item_warning: bool,
) {
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    let Some(slave) = world.slave(obj_id) else {
        return;
    };
    save(server, &slave);
    let (attached_chars, tl) = {
        let d = slave.data();
        (d.attached_characters.clone(), u32::from(d.unit.tl_id))
    };
    for (_, c) in attached_chars {
        if let Some(p) = server.world.character_by_obj_id(c) {
            unbind_slave(server, &p, tl, attach_reason::SLAVE_BINDING);
        }
    }
    let (doodad_ids, slave_ids, portal_time, template_id) = {
        let d = slave.data();
        let portal = server
            .game_data
            .systems
            .vehicles
            .slave_template(d.unit.template_id)
            .map_or(0.0, |t| t.portal_time);
        (
            d.attached_doodads.clone(),
            d.attached_slaves.clone(),
            portal,
            d.unit.template_id,
        )
    };
    if !ignore_attached_item_warning {
        for id in &doodad_ids {
            if let Some(d) = world.doodad(*id) {
                let (item, tpl) = {
                    let d = d.data();
                    (d.item_id, d.item_template_id)
                };
                if item != 0 || tpl != 0 {
                    if let Some(o) = owner {
                        error(o, error::SLAVE_EQUIPMENT_LOADED_ITEM);
                    }
                    return;
                }
            }
        }
    }
    let now = spawn_manager::now();
    let despawn_delayed = spawn_manager::add_secs(now, f64::from(portal_time - 0.5));
    // slaveInfo.Transform.DetachAll()
    let slave_obj = WorldObject::from(&slave);
    world.detach_child(&slave_obj);
    for child in slave_obj.core().take_children() {
        child.with_transform(|t| t.parent_obj_id = 0);
    }
    let spawns = spawn_manager_of(server, world.id);
    for id in &doodad_ids {
        let Some(d) = world.doodad(*id) else { continue };
        if owner.is_some() {
            d.data().is_persistent = false;
        }
        if let Some(s) = &spawns {
            s.add_despawn(server, WorldObject::from(&d), Some(despawn_delayed));
        }
    }
    for id in &slave_ids {
        let Some(s) = world.slave(*id) else { continue };
        world.remove_object(*id);
        if let Some(sp) = &spawns {
            sp.add_despawn(server, WorldObject::from(&s), Some(despawn_delayed));
        }
    }
    super::ship_physics::remove_ship(server, &slave);
    if let Some(o) = owner {
        let me = WorldObject::from(o);
        broadcast(server, &me, &SCSlaveDespawnPacket { obj_id }, true);
        broadcast(
            server,
            &me,
            &SCSlaveRemovedPacket {
                obj_id: o.obj_id,
                tl_id: tl as u16,
            },
            true,
        );
    }
    world.remove_object(obj_id);
    if let Some(s) = &spawns {
        s.add_despawn(
            server,
            slave_obj,
            Some(spawn_manager::add_secs(now, f64::from(portal_time + 0.5))),
        );
    }
    let _ = template_id;
}

/// `RemoveActiveSlave(character, slaveTlId, forceDelete)`.
pub fn remove_active_slave(server: &GameServer, character: &PlayerRef, tl_id: u16, force: bool) {
    let Some(world) = player_world(server, character) else {
        return;
    };
    let Some(slave) = get_slave_by_tl_id(&world, u32::from(tl_id)) else {
        return;
    };
    let (summoner, name) = {
        let d = slave.data();
        (d.summoner_obj_id, d.unit.name.clone())
    };
    if summoner == 0 || summoner != character.obj_id {
        tracing::warn!(
            "Non-owner is trying to desummon a slave {} => {name} (ObjId: {})",
            character.data().unit.name,
            slave.obj_id
        );
        return;
    }
    delete(server, Some(character), world.id, slave.obj_id, force);
}

/// `RemoveAndDespawnAllActiveOwnedSlaves(owner, forceDelete)`.
pub fn remove_and_despawn_all_active_owned_slaves(
    server: &GameServer,
    owner: &PlayerRef,
    force: bool,
) {
    if let Some(world) = player_world(server, owner) {
        if let Some(active) = get_active_slave_by_owner_obj_id(&world, owner.obj_id) {
            save(server, &active);
            delete(server, Some(owner), world.id, active.obj_id, force);
        }
    }
    clear_slave_attachment_state(server, owner);
}

/// `RidersEscape(player, positionTarget)`.
pub fn riders_escape(server: &GameServer, player: &PlayerRef, target: &SkillCastTarget) {
    let SkillCastTarget::Position { x, y, z, rot, .. } = *target else {
        return;
    };
    let Some(world) = player_world(server, player) else {
        return;
    };
    let Some(slave) = get_active_slave_by_owner_obj_id(&world, player.obj_id) else {
        tracing::warn!(
            "{} using Rider's Escape with no slave active!",
            player.data().unit.name
        );
        return;
    };
    let obj = WorldObject::from(&slave);
    let tl = slave.data().unit.tl_id;
    broadcast(
        server,
        &obj,
        &SCSlaveDespawnPacket {
            obj_id: slave.obj_id,
        },
        true,
    );
    broadcast(
        server,
        &obj,
        &SCSlaveRemovedPacket {
            obj_id: slave.obj_id,
            tl_id: tl,
        },
        true,
    );
    // mySlave.SendPacket(SCUnitsRemovedPacket): a slave has no connection.
    let (sx, sy) = server
        .game_data
        .systems
        .vehicles
        .slave_template(slave.data().unit.template_id)
        .map_or((0.0, 0.0), |t| (t.spawn_x_offset, t.spawn_y_offset));
    {
        let mut d = slave.data();
        let t = &mut d.unit.transform;
        t.set_position(x, y, z, 0.0, 0.0, rot);
        add_distance_to_front(t, sx / 2.0);
        add_distance_to_right(t, sy / 2.0);
    }
    world.hide(&obj);
    let changes = world.spawn(&obj, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
}

/// `PositionAndRotation.AddDistanceToFront(distance)`.
pub fn add_distance_to_front(t: &mut Transform, distance: f32) {
    t.x += -distance * t.rot_z.sin();
    t.y += distance * t.rot_z.cos();
}

/// `PositionAndRotation.AddDistanceToRight(distance)`.
pub fn add_distance_to_right(t: &mut Transform, distance: f32) {
    t.x += distance * t.rot_z.cos();
    t.y += distance * t.rot_z.sin();
}

// ---------------------------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------------------------

/// The `slaves` row of a previously saved player vehicle (`load_saved_slave`).
#[derive(Debug, Clone, PartialEq)]
pub struct SavedSlave {
    pub id: u32,
    pub name: String,
    pub hp: i32,
    pub mp: i32,
}

/// A saved child slave row (`owner_type = 2`), by attach point.
#[derive(Debug, Clone, PartialEq)]
pub struct SavedChildSlave {
    pub id: u32,
    pub template_id: u32,
    pub attach_point: u8,
    pub name: String,
    pub hp: i32,
    pub mp: i32,
}

/// `SlaveManager.Create(owner, skillData)` (the `SpawnSlave` special effect): runs the async
/// creation on a task.
pub fn create_from_skill(server: &GameServer, owner: &PlayerRef, caster: &SkillCaster) {
    let SkillCaster::Item {
        item_id,
        item_template_id,
        ..
    } = *caster
    else {
        return;
    };
    let Some(server) = server.self_arc() else {
        return;
    };
    let owner = owner.clone();
    let fut = async move {
        create_for_owner(&server, &owner, item_id, item_template_id, false, None).await;
    };
    match tokio::runtime::Handle::try_current() {
        Ok(rt) => {
            rt.spawn(fut);
        }
        Err(_) => tracing::error!("SlaveManager.Create: no runtime"),
    }
}

/// `Create(owner, skillData, hideSpawnEffect, positionOverride)`.
pub async fn create_for_owner(
    server: &GameServer,
    owner: &PlayerRef,
    item_id: u64,
    item_template_id: u32,
    hide_spawn_effect: bool,
    position_override: Option<Transform>,
) -> Option<SlaveRef> {
    let world = player_world(server, owner)?;
    if let Some(active) = get_active_slave_by_owner_obj_id(&world, owner.obj_id) {
        save(server, &active);
        // TODO (C#): If too far away, don't delete
        delete(server, Some(owner), world.id, active.obj_id, false);
    }
    if item_id == 0 || item_template_id == 0 {
        return None;
    }
    // skillData.SkillSourceItem = ItemManager.GetItemByItemId(ItemId)
    let item = super::mates::owned_item(server, owner, item_id)?;
    let slave_id = match server
        .game_data
        .items
        .template(item.template_id)
        .map(|t| &t.kind)
    {
        Some(crate::data::items::TemplateKind::SummonSlave { slave_id }) => *slave_id,
        _ => return None,
    };
    create(
        server,
        &world,
        Some(owner),
        slave_id,
        Some(item),
        hide_spawn_effect,
        position_override,
    )
    .await
}

/// `Create(owner, null, templateId, item, hideSpawnEffect, positionOverride)` with the DB parts
/// (saved slave, its persistent doodads, its saved child slaves).
pub async fn create(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    owner: Option<&PlayerRef>,
    template_id: u32,
    item: Option<Item>,
    hide_spawn_effect: bool,
    position_override: Option<Transform>,
) -> Option<SlaveRef> {
    server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)?;
    let owner_id = owner.map_or(0, |o| o.data().id);
    let mut saved = None;
    if owner_id > 0 {
        if let Some(item) = &item {
            if item.id > 0 {
                saved = load_saved_slave(server, owner_id, item.id).await;
            }
        }
    }
    let spawned = create_spawn(
        server,
        world,
        owner,
        None,
        template_id,
        item.clone(),
        hide_spawn_effect,
        position_override,
        saved,
    )?;
    let db_id = spawned.slave.data().id;
    if spawned.is_loaded {
        // If this was a previously saved slave, load doodads from DB and spawn them
        if let Some(sm) = spawn_manager_of(server, world.id) {
            match sm
                .spawn_persistent_doodads(
                    server,
                    doodad_owner_type::SLAVE,
                    i64::from(db_id),
                    Some(&WorldObject::from(&spawned.slave)),
                    true,
                )
                .await
            {
                Ok(n) => tracing::debug!(
                    "Loaded {n} doodads from DB for Slave {} (Db: {db_id}",
                    spawned.slave.obj_id
                ),
                Err(e) => tracing::error!("SpawnPersistentDoodads (slave {db_id}): {e}"),
            }
        }
    }
    let children = if db_id > 0 {
        load_saved_children(server, db_id, owner_id).await
    } else {
        Vec::new()
    };
    finish_create(
        server,
        world,
        owner,
        &spawned.slave,
        &spawned.template,
        item.as_ref(),
        spawned.is_loaded,
        children,
    )
}

async fn load_saved_slave(server: &GameServer, owner_id: u32, item_id: u64) -> Option<SavedSlave> {
    let r = sqlx::query(
        "SELECT CAST(id AS UNSIGNED) AS id, name, CAST(hp AS SIGNED) AS hp, CAST(mp AS SIGNED) AS mp \
         FROM slaves WHERE (owner_type = 0) AND (owner_id = ?) AND (summoner = ?) AND (item_id = ?) LIMIT 1",
    )
    .bind(owner_id)
    .bind(owner_id)
    .bind(item_id)
    .fetch_optional(&server.db)
    .await;
    match r {
        Ok(Some(r)) => Some(SavedSlave {
            id: r.try_get::<u64, _>("id").ok()? as u32,
            name: r
                .try_get::<Option<String>, _>("name")
                .ok()?
                .unwrap_or_default(),
            hp: r.try_get::<Option<i64>, _>("hp").ok()?.unwrap_or(0) as i32,
            mp: r.try_get::<Option<i64>, _>("mp").ok()?.unwrap_or(0) as i32,
        }),
        Ok(None) => None,
        Err(e) => {
            tracing::error!("load saved slave: {e}");
            None
        }
    }
}

async fn load_saved_children(
    server: &GameServer,
    parent_db_id: u32,
    summoner_id: u32,
) -> Vec<SavedChildSlave> {
    let rows = sqlx::query(
        "SELECT CAST(id AS UNSIGNED) AS id, CAST(template_id AS UNSIGNED) AS template_id, \
         CAST(attach_point AS SIGNED) AS attach_point, name, CAST(hp AS SIGNED) AS hp, \
         CAST(mp AS SIGNED) AS mp FROM slaves WHERE (owner_type = 2) AND (owner_id = ?) AND (summoner = ?)",
    )
    .bind(parent_db_id)
    .bind(summoner_id)
    .fetch_all(&server.db)
    .await;
    match rows {
        Ok(rows) => rows
            .into_iter()
            .filter_map(|r| {
                Some(SavedChildSlave {
                    id: r.try_get::<u64, _>("id").ok()? as u32,
                    template_id: r
                        .try_get::<Option<u64>, _>("template_id")
                        .ok()?
                        .unwrap_or(0) as u32,
                    attach_point: r
                        .try_get::<Option<i64>, _>("attach_point")
                        .ok()?
                        .unwrap_or(0) as u8,
                    name: r
                        .try_get::<Option<String>, _>("name")
                        .ok()?
                        .unwrap_or_default(),
                    hp: r.try_get::<Option<i64>, _>("hp").ok()?.unwrap_or(0) as i32,
                    mp: r.try_get::<Option<i64>, _>("mp").ok()?.unwrap_or(0) as i32,
                })
            })
            .collect(),
        Err(e) => {
            tracing::error!("load saved child slaves: {e}");
            Vec::new()
        }
    }
}

/// `ApplySlaveBonuses(summonedSlave)`.
fn apply_slave_bonuses(server: &GameServer, obj: &WorldObject, template: &SlaveTemplate) {
    for passive in &template.passive_buffs {
        match server.game_data.skills.passive_buff(*passive) {
            Some(p) => {
                crate::systems::skills::buffs::add_buff_by_id(server, obj, p.buff_id, obj, 0)
            }
            None => tracing::error!(
                "Slave {}: passive buff {passive} missing (NullReferenceException)",
                template.id
            ),
        }
    }
    for buff_id in &template.initial_buffs {
        crate::systems::skills::buffs::add_buff_by_id(server, obj, *buff_id, obj, 0);
    }
    obj.with_unit(|u| {
        for b in &template.bonuses {
            u.unit_mut().bonuses.add_bonus(
                0,
                Bonus {
                    template: b.clone(),
                    value: b.value, // TODO (C#): using LinearLevelBonus
                },
            );
        }
    });
}

/// `ApplyAttachPointLocation(slave, baseUnit, attachPoint)`.
fn apply_attach_point_location(
    server: &GameServer,
    world: &WorldInstance,
    slave: &SlaveRef,
    child: &WorldObject,
    point: u8,
) {
    let model_id = slave.data().unit.model_id;
    match server
        .game_data
        .systems
        .vehicles
        .attach_points_for_slave(model_id)
    {
        Some(points) => match points.iter().find(|(p, _)| *p == point) {
            Some((_, v)) => {
                let v: WorldSpawnPosition = *v;
                // Transform = slave.Transform.CloneAttached(child); Parent = slave.Transform
                child.with_transform(|t| {
                    t.set_position(v.x, v.y, v.z, v.roll, v.pitch, v.yaw);
                });
                world.attach_child(&WorldObject::from(slave), child);
                tracing::debug!(
                    "Model id: {model_id} attachment {point} => pos ({}, {}, {})",
                    v.x,
                    v.y,
                    v.z
                );
            }
            None => tracing::warn!("Model id: {model_id} incomplete attach point information"),
        },
        None => tracing::warn!("Model id: {model_id} has no attach point information"),
    }
}

/// A slave spawned by the first half of `Create` (up to `summonedSlave.Spawn()`).
pub(crate) struct Spawned {
    pub slave: SlaveRef,
    pub template: SlaveTemplate,
    pub is_loaded: bool,
}

/// `Create(owner, useSpawner, templateId, item, hideSpawnEffect, positionOverride)` without DB
/// data (spawners, tests): both halves back to back.
#[allow(clippy::too_many_arguments)]
pub(crate) fn create_core(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    owner: Option<&PlayerRef>,
    spawner: Option<&Arc<SlaveSpawner>>,
    template_id: u32,
    item: Option<Item>,
    hide_spawn_effect: bool,
    position_override: Option<Transform>,
    saved: Option<SavedSlave>,
) -> Option<SlaveRef> {
    let s = create_spawn(
        server,
        world,
        owner,
        spawner,
        template_id,
        item.clone(),
        hide_spawn_effect,
        position_override,
        saved,
    )?;
    finish_create(
        server,
        world,
        owner,
        &s.slave,
        &s.template,
        item.as_ref(),
        s.is_loaded,
        Vec::new(),
    )
}

/// The first half of `Create`: template, position, summon item checks, `SCSlaveCreatedPacket`,
/// the slave itself (bonuses, HP / MP, initial items) and `Spawn()`. `saved` is the
/// `load_saved_slave` row (owner + item only).
#[allow(clippy::too_many_arguments)]
pub(crate) fn create_spawn(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    owner: Option<&PlayerRef>,
    spawner: Option<&Arc<SlaveSpawner>>,
    template_id: u32,
    item: Option<Item>,
    hide_spawn_effect: bool,
    position_override: Option<Transform>,
    saved: Option<SavedSlave>,
) -> Option<Spawned> {
    let data = &server.game_data.systems.vehicles;
    let template = data
        .slave_template(spawner.map_or(template_id, |s| s.def.base.unit_id))?
        .clone();
    let tl_id = server.ids.tl.get_next_id().ok()?;
    let obj_id = server.ids.object.get_next_id().ok()?;

    let mut spawn_pos = position_override.unwrap_or_default();
    spawn_pos.instance_id = world.id;
    let mut db_id = 0u32;
    let mut slave_name = String::new();
    let (mut slave_hp, mut slave_mp) = (1, 1);
    let is_loaded = saved.is_some();
    if let Some(s) = &saved {
        db_id = s.id;
        slave_name = s.name.clone();
        slave_hp = s.hp;
        slave_mp = s.mp;
    }
    let (owner_id, owner_obj_id, owner_name, owner_faction) = match owner {
        Some(o) => {
            let c = o.data();
            (c.id, o.obj_id, c.unit.name.clone(), c.unit.faction_id)
        }
        None => (0, 0, String::new(), 0),
    };

    // Put it at the correct location
    if spawn_pos.x == 0.0 && spawn_pos.y == 0.0 && spawn_pos.z == 0.0 {
        if owner.is_none() && spawner.is_none() {
            tracing::warn!(
                "Tried creating a slave without a defined position, either use a Owner, Spawner or PositionOverride"
            );
            return None;
        }
        if let Some(sp) = spawner {
            let p = sp.def.base.position;
            spawn_pos.instance_id = crate::models::character::DEFAULT_INSTANCE_ID;
            spawn_pos.zone_id = p.zone_id;
            spawn_pos.set_position(p.x, p.y, p.z, p.roll, p.pitch, p.yaw);
        } else if let Some(o) = owner {
            let me = WorldObject::from(o);
            let w = world.world_pos_rot(&me);
            let t = o.data().unit.transform;
            spawn_pos.instance_id = t.instance_id;
            spawn_pos.zone_id = t.zone_id;
            spawn_pos.set_position(w.x, w.y, w.z, w.rot_x, w.rot_y, w.rot_z);
        }
        if owner.is_some() {
            add_distance_to_front(&mut spawn_pos, template.spawn_y_offset.clamp(5.0, 50.0));
        }
        if template.is_a_boat() {
            let water = super::ship_physics::water_surface(
                server,
                world,
                [spawn_pos.x, spawn_pos.y, spawn_pos.z],
            );
            spawn_pos.set_height(water);
            let Some(ship) = data.ship_model(template.model_id).copied() else {
                tracing::error!(
                    "Slave {}: no ship model {} (NullReferenceException)",
                    template.id,
                    template.model_id
                );
                return None;
            };
            let min_depth = ship.mass_box_size_z - ship.mass_center_z + 1.0;
            let mut offset_z = 0.0;
            offset_z += (if ship.mass_center_z < 0.0 {
                ship.mass_center_z / 2.0
            } else {
                0.0
            }) - ship.keel_height;
            let mut in_front = 0.0f32;
            while in_front < 50.0 + ship.mass_box_size_x {
                let mut check = spawn_pos;
                add_distance_to_front(&mut check, in_front);
                let floor =
                    crate::systems::npcs::heightmap::world_height(server, world, check.x, check.y)
                        .unwrap_or(0.0);
                if floor > 0.0 {
                    let surface = super::ship_physics::water_surface(
                        server,
                        world,
                        [check.x, check.y, check.z],
                    );
                    if surface - floor > min_depth {
                        spawn_pos.set_position(
                            check.x,
                            check.y,
                            check.z,
                            check.rot_x,
                            check.rot_y,
                            check.rot_z,
                        );
                        break;
                    }
                }
                in_front += 1.0;
            }
            spawn_pos.z += offset_z;
        } else {
            // TODO (C#): check for maximum height difference for summoning
            let h = crate::systems::npcs::heightmap::world_height(
                server,
                world,
                spawn_pos.x,
                spawn_pos.y,
            )
            .unwrap_or(0.0);
            if h > 0.0 {
                spawn_pos.set_height(h);
            }
        }
        // Always spawn horizontal(level) and 90° CCW of the player
        let yaw = match owner {
            Some(o) => {
                world.world_pos_rot(&WorldObject::from(o)).rot_z + std::f32::consts::PI / 2.0
            }
            None => spawner.map_or(0.0, |s| s.def.base.position.yaw),
        };
        spawn_pos.rot_x = 0.0;
        spawn_pos.rot_y = 0.0;
        spawn_pos.rot_z = yaw;
    }

    // Get new Id to save if it has a player as owner
    if owner_id > 0 && db_id == 0 {
        db_id = server.ids.character.get_next_id().ok()?;
    }

    // Update the summoning item
    if let (Some(it), Some(o)) = (&item, owner) {
        if it.class == ItemClass::SummonSlave {
            let now = chrono::Utc::now().timestamp();
            let res = super::mates::update_owned_item(server, Some(o), it.id, |i| {
                i.details.slave_type = 0x02;
                i.details.slave_db_id = db_id;
                if i.details.slave_is_destroyed > 0 || i.details.slave_repair_start_time > 0 {
                    let left = (i.details.slave_repair_start_time + 600 - now) as f64;
                    if left > 0.0 {
                        return Err(left);
                    }
                }
                // SummonLocation (not part of the 1.2 detail bytes); RepairStartTime = MinValue
                i.details.slave_repair_start_time = 0;
                i.dirty = true;
                Ok(super::packets::item_update(i))
            });
            match res {
                Some(Err(left)) => {
                    // Slave was destroyed and is on cooldown
                    o.send(crate::packets::world::SCErrorMsgPacket::new(
                        error::SLAVE_SPAWN_ERROR_NEED_REPAIR_TIME,
                        left.round_ties_even() as u32,
                        true,
                    ));
                    return None;
                }
                Some(Ok(task)) => o.send(SCItemTaskSuccessPacket {
                    action: item_task_type::UPDATE_SUMMON_MATE_ITEM,
                    tasks: vec![task],
                    force_remove: Vec::new(),
                }),
                None => {}
            }
        }
    }

    // Create the Slave (packet)
    if let Some(o) = owner {
        broadcast(
            server,
            &WorldObject::from(o),
            &SCSlaveCreatedPacket {
                owner_obj_id,
                tl_id: tl_id as u16,
                slave_obj_id: obj_id,
                hide_spawn_effect,
                unk_id: 0,
                creator_name: owner_name.clone(),
            },
            true,
        );
    }
    let faction = if owner.is_some() {
        owner_faction
    } else {
        template.faction_id
    };
    let mut s = Slave::new(db_id, template.id);
    s.unit.tl_id = tl_id as u16;
    s.unit.name = if slave_name.trim().is_empty() {
        template.name.clone()
    } else {
        slave_name
    };
    s.unit.level = template.level as u8;
    s.unit.model_id = template.model_id;
    s.unit.hp = slave_hp;
    s.unit.mp = slave_mp;
    s.unit.faction_id = faction;
    s.unit.owner_id = owner_id;
    s.summoner_id = owner_id;
    s.summoner_obj_id = owner_obj_id;
    s.summoner_name = owner_name.clone();
    s.summoning_item_id = item.as_ref().map_or(0, |i| i.id);
    s.spawn_time_ms = now_ms();
    s.spawner_id = spawner.map_or(0, |sp| sp.id);
    s.spawner_despawn_time = spawner.map(|sp| sp.def.base.despawn_time);
    s.owner_type = if owner.is_some() {
        BaseUnitType::Character
    } else {
        BaseUnitType::Invalid
    };
    s.owner_obj_id = owner_obj_id;
    let slave = Entity::new(obj_id, s);
    let slave_obj = WorldObject::from(&slave);
    apply_slave_bonuses(server, &slave_obj, &template);
    stats::refresh_object_pools(server, &slave_obj);
    {
        let mut d = slave.data();
        if !is_loaded {
            d.unit.hp = d.unit.max_hp;
            d.unit.mp = d.unit.max_mp;
        }
    }
    // Equip it's default items. TODO (C#): Implement vehicle customization
    if let Some(pack) = data.slave_initial_item_pack(template.slave_initial_item_pack_id) {
        let now = chrono::Utc::now().timestamp();
        for initial in pack {
            let Some(t) = server.game_data.items.template(initial.item_id) else {
                continue;
            };
            let mut it = Item::create(0, t, 1, 0, server.config.id, &server.game_data.items, now);
            it.slot = i32::from(initial.equip_slot_id);
            let mut d = slave.data();
            if let Some(slot) = d.equipment.get_mut(usize::from(initial.equip_slot_id)) {
                *slot = Some(it);
            }
        }
    }
    {
        let mut d = slave.data();
        d.unit.hp = d.unit.hp.min(d.unit.max_hp);
        d.unit.mp = d.unit.mp.min(d.unit.max_mp);
        if d.unit.hp <= 0 {
            d.unit.hp = d.unit.max_hp;
        }
        d.unit.transform = spawn_pos;
        d.unit.transform.parent_obj_id = 0;
    }
    if template.is_a_boat() && template.portal_time > 0.0 {
        let pos = world.world_pos_rot(&slave_obj);
        let water_at_final =
            super::ship_physics::water_surface(server, world, [pos.x, pos.y, pos.z]);
        if water_at_final > 0.0 {
            let mul = SHIP_WATER_DENSITY_MUL;
            if mul > 0.0 {
                let target_submerged = 1.0 / (BASE_WATER_DENSITY * mul);
                let target_z = water_at_final - target_submerged;
                let mut d = slave.data();
                if d.unit.transform.z < target_z {
                    d.unit.transform.set_height(target_z);
                }
            }
        }
    }
    let changes = world.spawn(&slave, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
    Some(Spawned {
        slave,
        template,
        is_loaded,
    })
}

/// The part of `Create` after `summonedSlave.Spawn()` (and, for a loaded slave, after its
/// persistent doodads were spawned).
#[allow(clippy::too_many_arguments)]
fn finish_create(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    owner: Option<&PlayerRef>,
    slave: &SlaveRef,
    template: &SlaveTemplate,
    item: Option<&Item>,
    is_loaded: bool,
    saved_children: Vec<SavedChildSlave>,
) -> Option<SlaveRef> {
    let data = &server.game_data.systems.vehicles;
    let slave_obj = WorldObject::from(slave);
    if is_loaded {
        // The persistent doodads were spawned by `create` (async) before this point.
        let ids: Vec<u32> = world
            .all_doodads()
            .into_iter()
            .filter(|d| d.data().parent_obj_id == slave.obj_id)
            .map(|d| d.obj_id)
            .collect();
        let mut d = slave.data();
        for id in ids {
            if !d.attached_doodads.contains(&id) {
                d.attached_doodads.push(id);
            }
        }
    }
    // UpdateGearBonuses(null, null): vehicle parts customization (economy) not in 1.2.
    let (owner_id, owner_obj_id, slave_db_id, faction, spawn_time) = {
        let d = slave.data();
        (
            d.summoner_id,
            d.summoner_obj_id,
            d.id,
            d.unit.faction_id,
            d.spawn_time_ms,
        )
    };
    // Create all remaining doodads that where not previously loaded
    for binding in &template.doodad_bindings {
        let loaded_points: Vec<u8> = {
            let ids = slave.data().attached_doodads.clone();
            ids.iter()
                .filter_map(|id| world.doodad(*id))
                .map(|d| d.data().attach_point)
                .collect()
        };
        if loaded_points.contains(&binding.attach_point_id) {
            continue;
        }
        let Some(doodad) = crate::systems::npcs::doodad::manager::create(
            server,
            world,
            0,
            binding.doodad_id,
            None,
            true,
        ) else {
            tracing::error!(
                "Slave {}: doodad template {} missing",
                template.id,
                binding.doodad_id
            );
            continue;
        };
        let item_ucc = item
            .filter(|i| i.flags & crate::models::item::ITEM_FLAG_HAS_UCC != 0 && i.ucc_id > 0)
            .map(|i| i.ucc_id);
        {
            let mut d = doodad.data();
            d.owner_obj_id = owner_obj_id;
            d.parent_obj_id = slave.obj_id;
            d.attach_point = binding.attach_point_id;
            d.owner_id = owner_id;
            d.plant_time = spawn_time / 1000;
            d.owner_type = doodad_owner_type::SLAVE;
            d.owner_db_id = slave_db_id;
            d.data = i32::from(binding.attach_point_id);
            d.faction_id = faction;
            d.type2 = 1;
            d.scale = binding.scale;
            d.transform.instance_id = world.id;
            d.transform.set_position(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
            // NOTE (C#): In 1.2 we can't replace slave parts like sail, so the UCC goes to all
            if let Some(u) = item_ucc {
                d.ucc_id = u;
            }
        }
        let start = crate::systems::npcs::doodad::phase::get_func_group_id(
            &server.game_data.systems.npcs.doodad,
            binding.doodad_id,
        );
        crate::systems::npcs::doodad::manager::set_func_group_id(server, &doodad, start);
        let dobj = WorldObject::from(&doodad);
        world.attach_child(&slave_obj, &dobj);
        apply_attach_point_location(server, world, slave, &dobj, binding.attach_point_id);
        slave.data().attached_doodads.push(doodad.obj_id);
        crate::systems::npcs::doodad::manager::init_doodad(server, world, &doodad);
        crate::systems::npcs::doodad::manager::spawn(server, world, &doodad);
        // Only set IsPersistent if the binding is defined as such
        if owner_id > 0 && item.is_some_and(|i| i.id > 0) && binding.persist {
            doodad.data().is_persistent = true;
            crate::systems::npcs::doodad::db::save(server, &doodad);
        }
    }

    // Spawn Slave's slaves
    for binding in &template.slave_bindings {
        if binding.owner_type != "Slave" {
            continue;
        }
        let mut child_db_id = 0u32;
        let mut child_template_id = 0u32;
        let mut child_name = String::new();
        if slave_db_id > 0 {
            if let Some(c) = saved_children
                .iter()
                .find(|c| c.attach_point == binding.attach_point_id)
            {
                child_db_id = c.id;
                child_template_id = c.template_id;
                child_name = c.name.clone();
            }
        }
        if slave_db_id > 0 && child_db_id == 0 {
            child_db_id = server.ids.character.get_next_id().ok()?;
        }
        let tpl_id = if child_template_id > 0 {
            child_template_id
        } else {
            binding.slave_id
        };
        let Some(child_template) = data.slave_template(tpl_id).cloned() else {
            tracing::error!(
                "Slave {}: child template {tpl_id} missing (NullReferenceException)",
                template.id
            );
            return None;
        };
        let child_tl = server.ids.tl.get_next_id().ok()?;
        let child_obj_id = server.ids.object.get_next_id().ok()?;
        let mut c = Slave::new(child_db_id, child_template.id);
        c.unit.tl_id = child_tl as u16;
        c.unit.name = if child_name.trim().is_empty() {
            child_template.name.clone()
        } else {
            child_name
        };
        c.unit.level = child_template.level as u8;
        c.unit.model_id = child_template.model_id;
        c.unit.faction_id = faction;
        c.summoner_id = owner_id;
        c.summoner_obj_id = owner_obj_id;
        c.summoner_name = slave.data().summoner_name.clone();
        c.spawn_time_ms = now_ms();
        c.attach_point_id = binding.attach_point_id as i8;
        c.owner_obj_id = slave.obj_id;
        c.owner_type = BaseUnitType::Slave;
        c.unit.owner_id = slave_db_id;
        let child = Entity::new(child_obj_id, c);
        let child_obj = WorldObject::from(&child);
        apply_slave_bonuses(server, &child_obj, &child_template);
        stats::refresh_object_pools(server, &child_obj);
        {
            // TODO (C#): persistent HP for child slaves when customization is enabled
            let mut d = child.data();
            d.unit.hp = d.unit.max_hp;
            d.unit.mp = d.unit.max_mp;
            // Transform = summonedSlave.Transform.CloneDetached(child); Parent = slave
            let w = world.world_pos_rot(&slave_obj);
            d.unit.transform.instance_id = world.id;
            d.unit
                .transform
                .set_position(w.x, w.y, w.z, w.rot_x, w.rot_y, w.rot_z);
        }
        world.attach_child(&slave_obj, &child_obj);
        apply_attach_point_location(server, world, slave, &child_obj, binding.attach_point_id);
        slave.data().attached_slaves.push(child_obj_id);
        let changes = world.spawn(&child, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        let hp = child.data().unit.hp;
        crate::systems::skills::combat::post_update_current_hp(
            server,
            &child_obj,
            Some(&child_obj),
            0,
            hp,
            KillReason::Unknown,
        );
    }

    // If it's a boat, add it to boat physics
    if template.is_a_boat() {
        if owner.is_none() {
            // C#: `GetWorld(owner.Transform.InstanceId)` throws for an owner-less boat.
            tracing::error!(
                "Slave {} ({}): owner-less boat (NullReferenceException in C#)",
                template.id,
                slave.obj_id
            );
            return None;
        }
        super::ship_physics::add_ship(server, slave);
    }

    if let Some(o) = owner {
        let w = world.world_pos_rot(&slave_obj);
        let p = {
            let d = slave.data();
            SCMySlavePacket {
                obj_id: slave.obj_id,
                tl_id: d.unit.tl_id,
                name: d.unit.name.clone(),
                template_id: d.unit.template_id,
                hp: d.unit.hp,
                max_hp: d.unit.max_hp,
                x: w.x,
                y: w.y,
                z: w.z,
            }
        };
        o.send(p);
    }
    // Save to DB
    save(server, slave);
    let hp = slave.data().unit.hp;
    crate::systems::skills::combat::post_update_current_hp(
        server,
        &slave_obj,
        Some(&slave_obj),
        0,
        hp,
        KillReason::Unknown,
    );
    update_slave_repair_points(server, slave);
    Some(slave.clone())
}

// ---------------------------------------------------------------------------------------------
// Repair points
// ---------------------------------------------------------------------------------------------

/// `UpdateSlaveRepairPoints(slave)`.
pub fn update_slave_repair_points(server: &GameServer, slave: &SlaveRef) {
    let Some(world) = server.world.world_of(&WorldObject::from(slave)) else {
        return;
    };
    let (hp, max_hp, template_id, attached, owner_obj, summoner_id, db_id, faction) = {
        let d = slave.data();
        (
            d.unit.hp,
            d.unit.max_hp,
            d.unit.template_id,
            d.attached_doodads.clone(),
            d.owner_obj_id,
            d.summoner_id,
            d.id,
            d.unit.faction_id,
        )
    };
    let Some(template) = server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)
    else {
        return;
    };
    if max_hp == 0 {
        tracing::error!("UpdateSlaveRepairPoints: MaxHp 0 (DivideByZeroException)");
        return;
    }
    let hp_percent = hp * 100 / max_hp;
    let repair_points = if (75..100).contains(&hp_percent) {
        template.hp75_doodad_count
    } else if (50..75).contains(&hp_percent) {
        template.hp50_doodad_count
    } else if (25..50).contains(&hp_percent) {
        template.hp25_doodad_count
    } else if hp_percent < 25 {
        template.healing_point_doodads.len() as i32
    } else {
        0
    };
    let mut current = Vec::new();
    let mut unused: Vec<u8> = template
        .healing_point_doodads
        .iter()
        .map(|b| b.attach_point_id)
        .collect();
    for id in &attached {
        let Some(d) = world.doodad(*id) else { continue };
        let ap = d.data().attach_point;
        if !(attach_point::HEAL_POINT0..=attach_point::HEAL_POINT9).contains(&ap) {
            continue;
        }
        current.push(d.clone());
        if let Some(i) = unused.iter().position(|p| *p == ap) {
            unused.remove(i);
        }
    }
    let points_to_add = repair_points - current.len() as i32;
    let mut rng = rand::thread_rng();
    if points_to_add < 0 {
        let spawns = spawn_manager_of(server, world.id);
        for _ in points_to_add..0 {
            if current.is_empty() {
                break;
            }
            let i = rng.gen_range(0..current.len());
            let d = current[i].clone();
            world.hide(&d);
            if let Some(s) = &spawns {
                s.add_despawn(server, WorldObject::from(&d), Some(spawn_manager::now()));
            }
            slave.data().attached_doodads.retain(|x| *x != d.obj_id);
            current.remove(i);
            unused.push(d.data().attach_point);
            crate::systems::npcs::doodad::manager::delete(server, &world, &d);
        }
    }
    if points_to_add > 0 && !unused.is_empty() {
        let mut i_add = 0;
        while i_add < points_to_add && !unused.is_empty() {
            let pick = unused[rng.gen_range(0..unused.len())];
            unused.retain(|p| *p != pick);
            let Some(binding) = template
                .healing_point_doodads
                .iter()
                .find(|b| b.attach_point_id == pick)
            else {
                tracing::error!(
                    "Somehow failed to grab a healing point {pick} for {}",
                    template.id
                );
                return;
            };
            let Some(doodad) = crate::systems::npcs::doodad::manager::create(
                server,
                &world,
                0,
                binding.doodad_id,
                None,
                true,
            ) else {
                i_add += 1;
                continue;
            };
            {
                let mut d = doodad.data();
                d.owner_obj_id = owner_obj;
                d.parent_obj_id = slave.obj_id;
                d.attach_point = pick;
                d.owner_id = summoner_id;
                d.plant_time = chrono::Utc::now().timestamp();
                d.owner_type = doodad_owner_type::SLAVE;
                d.owner_db_id = db_id;
                d.data = i32::from(pick);
                d.faction_id = faction;
                d.type2 = 1;
                d.is_persistent = false;
                d.scale = 1.0;
                d.transform.instance_id = world.id;
            }
            let dobj = WorldObject::from(&doodad);
            apply_attach_point_location(server, &world, slave, &dobj, pick);
            let start = crate::systems::npcs::doodad::phase::get_func_group_id(
                &server.game_data.systems.npcs.doodad,
                binding.doodad_id,
            );
            crate::systems::npcs::doodad::manager::set_func_group_id(server, &doodad, start);
            slave.data().attached_doodads.push(doodad.obj_id);
            current.push(doodad.clone());
            crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);
            i_add += 1;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Slave behaviour
// ---------------------------------------------------------------------------------------------

/// `Slave.DoHpChangeTrigger(triggerValue, ...)`.
pub fn do_hp_change_trigger(server: &GameServer, slave: &SlaveRef, trigger_value: i32) {
    let (name, summoner) = {
        let d = slave.data();
        (d.unit.name.clone(), d.summoner_name.clone())
    };
    let summoner = if summoner.is_empty() {
        "unknown".to_string()
    } else {
        summoner
    };
    tracing::debug!("{name} from {summoner}'s HP is now at {trigger_value}%");
    update_slave_repair_points(server, slave);
}

/// `Slave.DoDie(killer, killReason)`.
pub fn do_die(
    server: &GameServer,
    slave: &SlaveRef,
    killer: Option<&WorldObject>,
    kill_reason: KillReason,
) {
    let obj = WorldObject::from(slave);
    crate::systems::skills::skill::interrupt_skills(server, &obj);
    crate::systems::skills::buffs::fire_unit_event(
        server,
        slave.obj_id,
        BuffEventTriggerKind::Death,
        0,
    );
    crate::systems::skills::buffs::remove_effects_on_death(server, &obj);
    match killer {
        Some(k) => {
            let killer_info = k
                .is_unit()
                .then(|| (k.obj_id(), k.with_base_unit(|b| b.name().to_string())));
            broadcast(
                server,
                k,
                &SCUnitDeathPacket {
                    obj_id: slave.obj_id,
                    kill_reason,
                    resurrect_wait_time: 0,
                    lost_exp: 0,
                    durability_loss_rate: 0,
                    killer: killer_info,
                },
                true,
            );
        }
        None => tracing::error!("Slave.DoDie without a killer (NullReferenceException)"),
    }
    destroy_attached_items(server, slave);
    distribute_slave_drop_doodads(server, slave);
    mark_summoning_item_as_destroyed(server, slave);
    let (summoner, tl, packet, attached, template_id, despawn_time) = {
        let d = slave.data();
        (
            d.summoner_obj_id,
            d.unit.tl_id,
            (
                d.unit.name.clone(),
                d.unit.template_id,
                d.unit.hp,
                d.unit.max_hp,
            ),
            d.attached_characters.clone(),
            d.unit.template_id,
            d.spawner_despawn_time.unwrap_or(20),
        )
    };
    let world = server.world.world_of(&obj);
    let pos = world
        .as_ref()
        .map(|w| w.world_pos_rot(&obj))
        .unwrap_or_default();
    if let Some(s) = (summoner != 0)
        .then(|| server.world.character_by_obj_id(summoner))
        .flatten()
    {
        s.send(SCMySlavePacket {
            obj_id: slave.obj_id,
            tl_id: tl,
            name: packet.0,
            template_id: packet.1,
            hp: packet.2,
            max_hp: packet.3,
            x: pos.x,
            y: pos.y,
            z: pos.z,
        });
        broadcast(
            server,
            &WorldObject::from(&s),
            &SCSlaveRemovedPacket {
                obj_id: s.obj_id,
                tl_id: tl,
            },
            true,
        );
    }
    crate::systems::skills::combat::clear_all_aggro(server, &obj);
    for (_, c) in attached {
        if let Some(p) = server.world.character_by_obj_id(c) {
            unbind_slave(server, &p, u32::from(tl), attach_reason::NONE);
        }
    }
    if server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)
        .is_some_and(SlaveTemplate::is_a_boat)
    {
        super::ship_physics::remove_ship(server, slave);
    }
    if let Some(s) = world.and_then(|w| spawn_manager_of(server, w.id)) {
        s.add_despawn(
            server,
            obj,
            Some(spawn_manager::add_secs(
                spawn_manager::now(),
                f64::from(despawn_time),
            )),
        );
    }
}

/// `DestroyAttachedItems()`: child doodads (dropping a carried backpack) and child slaves.
fn destroy_attached_items(server: &GameServer, slave: &SlaveRef) {
    let Some(world) = server.world.world_of(&WorldObject::from(slave)) else {
        return;
    };
    let (doodads, slaves) = {
        let d = slave.data();
        (d.attached_doodads.clone(), d.attached_slaves.clone())
    };
    for id in doodads {
        let Some(doodad) = world.doodad(id) else {
            continue;
        };
        let item_id = doodad.data().item_id;
        if item_id > 0 {
            super::effects::drop_carried_backpack(server, &world, &doodad, item_id);
        }
        server.ids.object.release_id(doodad.obj_id);
        doodad.data().is_persistent = false;
        crate::systems::npcs::doodad::manager::delete(server, &world, &doodad);
    }
    for id in slaves {
        server.ids.object.release_id(id);
        if let Some(s) = world.slave(id) {
            world.delete(&s);
        }
    }
}

/// `DistributeSlaveDropDoodads()` (debris of destroyed vehicles).
fn distribute_slave_drop_doodads(server: &GameServer, slave: &SlaveRef) {
    let obj = WorldObject::from(slave);
    let Some(world) = server.world.world_of(&obj) else {
        return;
    };
    let template_id = slave.data().unit.template_id;
    let Some(template) = server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)
    else {
        return;
    };
    let center = world.world_pos_rot(&obj);
    let mut rng = rand::thread_rng();
    for drop in &template.slave_drop_doodads {
        for _ in 0..drop.count {
            let Some(doodad) = crate::systems::npcs::doodad::manager::create(
                server,
                &world,
                0,
                drop.doodad_id,
                None,
                true,
            ) else {
                tracing::error!(
                    "Slave drop doodad {} missing (NullReferenceException)",
                    drop.doodad_id
                );
                continue;
            };
            let mut v = [rng.gen::<f32>() * 2.0 - 1.0, rng.gen::<f32>() * 2.0 - 1.0];
            let len = (v[0] * v[0] + v[1] * v[1]).sqrt();
            if len > 0.0 {
                v = [v[0] / len, v[1] / len];
            }
            let r = rng.gen::<f32>() * drop.radius;
            let (x, y, z) = (center.x + v[0] * r, center.y + v[1] * r, center.z);
            let h = if !drop.on_water {
                crate::systems::npcs::heightmap::world_height(server, &world, x, y).unwrap_or(0.0)
            } else {
                super::ship_physics::water_surface(server, &world, [x, y, z])
            };
            let rot = (rng.gen::<f64>() * std::f64::consts::PI * 2.0) as f32;
            {
                let mut d = doodad.data();
                d.transform.instance_id = world.id;
                d.transform.set_position(x, y, h, 0.0, 0.0, rot);
            }
            crate::systems::npcs::doodad::manager::init_doodad(server, &world, &doodad);
            crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);
        }
    }
}

/// `MarkSummoningItemAsDestroyed()`.
fn mark_summoning_item_as_destroyed(server: &GameServer, slave: &SlaveRef) {
    let (item_id, summoner) = {
        let d = slave.data();
        (d.summoning_item_id, d.summoner_obj_id)
    };
    if item_id == 0 {
        return;
    }
    let summoner = server.world.character_by_obj_id(summoner);
    let task = super::mates::update_owned_item(server, summoner.as_ref(), item_id, |i| {
        if i.class != ItemClass::SummonSlave {
            return None;
        }
        i.details.slave_is_destroyed = 1;
        i.details.slave_repair_start_time = 0;
        i.dirty = true;
        Some(super::packets::item_update(i))
    })
    .flatten();
    if let (Some(task), Some(s)) = (task, summoner) {
        s.send(SCItemTaskSuccessPacket {
            action: item_task_type::MATE_DEATH,
            tasks: vec![task],
            force_remove: Vec::new(),
        });
    }
}

/// `Slave.RegenTick(delta)`.
pub fn regen_tick(server: &GameServer, slave: &SlaveRef) {
    let obj = WorldObject::from(slave);
    let (needs, dead, tl, attached) = {
        let d = slave.data();
        (
            d.unit.needs_regen(),
            d.unit.is_dead(),
            u32::from(d.unit.tl_id),
            d.attached_characters.clone(),
        )
    };
    if !needs {
        return;
    }
    if dead {
        for (_, c) in attached {
            if let Some(p) = server.world.character_by_obj_id(c) {
                unbind_slave(server, &p, tl, attach_reason::NONE);
            }
        }
        return;
    }
    stats::refresh_object_pools(server, &obj);
    let (old, hp, mp) = {
        let mut d = slave.data();
        let u = &mut d.unit;
        let old = u.hp;
        if u.is_in_battle {
            u.hp += u.persistent_hp_regen;
            u.mp += u.persistent_mp_regen;
        } else {
            u.hp += u.hp_regen;
            u.mp += u.mp_regen;
        }
        u.hp = u.hp.min(u.max_hp);
        u.mp = u.mp.min(u.max_mp);
        (old, u.hp, u.mp)
    };
    broadcast(
        server,
        &obj,
        &SCUnitPointsPacket {
            obj_id: slave.obj_id,
            hp,
            mp,
        },
        false,
    );
    crate::systems::skills::combat::post_update_current_hp(
        server,
        &obj,
        Some(&obj),
        old,
        hp,
        KillReason::Unknown,
    );
}

/// `Slave.OnZoneChange(last, new)`: every passenger's zone change.
pub fn on_zone_change(server: &GameServer, slave: &SlaveRef, last_zone: u32, new_zone: u32) {
    let attached = slave.data().attached_characters.clone();
    for (_, c) in attached {
        if let Some(p) = server.world.character_by_obj_id(c) {
            crate::world::session::on_zone_change(server, &p, last_zone, new_zone);
        }
    }
}

/// `SendMySlavePacketToAllOwners()` (`SendMySlaveTask`, every 5 s).
pub fn send_my_slave_packet_to_all_owners(server: &GameServer) {
    for world in server.world.worlds() {
        for slave in all_slaves(&world) {
            let obj = WorldObject::from(&slave);
            let (summoner, p) = {
                let d = slave.data();
                (
                    d.summoner_obj_id,
                    (
                        d.unit.tl_id,
                        d.unit.name.clone(),
                        d.unit.template_id,
                        d.unit.hp,
                        d.unit.max_hp,
                    ),
                )
            };
            if summoner == 0 {
                continue;
            }
            let Some(owner) = server.world.character_by_obj_id(summoner) else {
                continue;
            };
            let w = world.world_pos_rot(&obj);
            owner.send(SCMySlavePacket {
                obj_id: slave.obj_id,
                tl_id: p.0,
                name: p.1,
                template_id: p.2,
                hp: p.3,
                max_hp: p.4,
                x: w.x,
                y: w.y,
                z: w.z,
            });
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Persistence
// ---------------------------------------------------------------------------------------------

/// The values of one `REPLACE INTO slaves` row.
#[derive(Debug, Clone, PartialEq)]
pub struct SlaveRow {
    pub id: u32,
    pub item_id: u64,
    pub template_id: u32,
    pub attach_point: i8,
    pub name: String,
    pub owner_type: u8,
    pub owner_id: u32,
    pub summoner: u32,
    pub hp: i32,
    pub mp: i32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

/// The row of a slave (`None` when `Id <= 0`), plus its children's rows.
pub fn snapshot(server: &GameServer, slave: &SlaveRef) -> Vec<SlaveRow> {
    let obj = WorldObject::from(slave);
    let pos = server
        .world
        .world_of(&obj)
        .map(|w| w.world_pos_rot(&obj))
        .unwrap_or_else(|| slave.data().unit.transform.local());
    let (row, children) = {
        let d = slave.data();
        if d.id == 0 {
            return Vec::new();
        }
        (
            SlaveRow {
                id: d.id,
                item_id: d.summoning_item_id,
                template_id: d.unit.template_id,
                attach_point: d.attach_point_id,
                name: d.unit.name.clone(),
                owner_type: d.owner_type as u8,
                owner_id: d.unit.owner_id,
                summoner: d.summoner_id,
                hp: d.unit.hp,
                mp: d.unit.mp,
                x: pos.x,
                y: pos.y,
                z: pos.z,
            },
            d.attached_slaves.clone(),
        )
    };
    let mut out = vec![row];
    if let Some(world) = server.world.world_of(&obj) {
        for c in children {
            if let Some(cs) = world.slave(c) {
                out.extend(snapshot(server, &cs));
            }
        }
    }
    out
}

/// `REPLACE INTO slaves ...`.
pub async fn write_row(conn: &mut sqlx::MySqlConnection, r: &SlaveRow) -> sqlx::Result<()> {
    sqlx::query(
        "REPLACE INTO slaves(`id`,`item_id`,`template_id`,`attach_point`,`name`,`owner_type`,`owner_id`,`summoner`,`updated_at`,`hp`,`mp`,`x`,`y`,`z`) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(r.id)
    .bind(r.item_id)
    .bind(r.template_id)
    .bind(r.attach_point)
    .bind(&r.name)
    .bind(r.owner_type)
    .bind(r.owner_id)
    .bind(r.summoner)
    .bind(chrono::Utc::now().naive_utc())
    .bind(r.hp)
    .bind(r.mp)
    .bind(r.x)
    .bind(r.y)
    .bind(r.z)
    .execute(conn)
    .await?;
    Ok(())
}

/// `Slave.Save()` (new connection, fire and forget): nothing for `Id <= 0` or without a
/// summoning item.
pub fn save(server: &GameServer, slave: &SlaveRef) {
    if slave.data().summoning_item_id == 0 {
        return;
    }
    let rows = snapshot(server, slave);
    if rows.is_empty() {
        return;
    }
    let db = server.db.clone();
    let fut = async move {
        let mut conn = match db.acquire().await {
            Ok(c) => c,
            Err(e) => {
                tracing::error!("Slave.Save: {e}");
                return;
            }
        };
        for r in &rows {
            if let Err(e) = write_row(&mut conn, r).await {
                tracing::error!("Slave.Save: {e}");
            }
        }
    };
    if let Ok(rt) = tokio::runtime::Handle::try_current() {
        rt.spawn(fut);
    }
}

/// The `SaveManager.DoSave` slave part: `slave.Save(connection, transaction)` for every slave
/// of every world (children are written again by their parent, as in C#). Returns the number
/// of slaves saved.
pub async fn save_all(
    server: &GameServer,
    tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
) -> crate::Result<usize> {
    let mut n = 0;
    for world in server.world.worlds() {
        for slave in all_slaves(&world) {
            let rows = snapshot(server, &slave);
            if rows.is_empty() {
                continue;
            }
            let mut ok = true;
            for (i, r) in rows.iter().enumerate() {
                if let Err(e) = write_row(tx, r).await {
                    tracing::error!("{e}");
                    if i == 0 {
                        ok = false;
                    }
                }
            }
            if ok {
                n += 1;
            }
        }
    }
    Ok(n)
}

/// `OnDeleteSlaveItem(summonSlaveItem)` (called by the item code when a summon item is
/// destroyed): despawns the active slave and deletes its rows.
pub async fn on_delete_slave_item(
    server: &GameServer,
    owner: &PlayerRef,
    slave_db_id: u32,
) -> bool {
    if slave_db_id == 0 {
        return false;
    }
    if let Some(world) = player_world(server, owner) {
        if let Some(s) = get_slave_by_db_id(&world, slave_db_id) {
            let summoner = s.data().summoner_obj_id;
            if let Some(p) = server.world.character_by_obj_id(summoner) {
                let tl = s.data().unit.tl_id;
                remove_active_slave(server, &p, tl, true);
            }
        }
    }
    let mut conn = match server.db.acquire().await {
        Ok(c) => c,
        Err(e) => {
            tracing::error!("{e}");
            return false;
        }
    };
    delete_slave_by_id(server, &mut conn, slave_db_id).await
}

/// `DeleteSlaveById(connection, transaction, dbId)`: the row, its child doodads and (recursive)
/// child slaves.
pub fn delete_slave_by_id<'a>(
    server: &'a GameServer,
    conn: &'a mut sqlx::MySqlConnection,
    db_id: u32,
) -> futures::future::BoxFuture<'a, bool> {
    Box::pin(async move {
        let delete_count = match sqlx::query("DELETE FROM slaves WHERE `id` = ?")
            .bind(db_id)
            .execute(&mut *conn)
            .await
        {
            Ok(r) => r.rows_affected(),
            Err(e) => {
                tracing::error!("{e}");
                0
            }
        };
        let doodads: Vec<u32> = sqlx::query(
            "SELECT CAST(id AS UNSIGNED) AS id FROM doodads WHERE (owner_type = 2) AND (house_id = ?)",
        )
        .bind(db_id)
        .fetch_all(&mut *conn)
        .await
        .map(|rows| {
            rows.iter()
                .filter_map(|r| r.try_get::<u64, _>("id").ok().map(|v| v as u32))
                .collect()
        })
        .unwrap_or_default();
        let children: Vec<u32> = sqlx::query(
            "SELECT CAST(id AS UNSIGNED) AS id FROM slaves WHERE (owner_type = 2) AND (owner_id = ?)",
        )
        .bind(db_id)
        .fetch_all(&mut *conn)
        .await
        .map(|rows| {
            rows.iter()
                .filter_map(|r| r.try_get::<u64, _>("id").ok().map(|v| v as u32))
                .collect()
        })
        .unwrap_or_default();
        for d in doodads {
            if let Err(e) =
                crate::systems::npcs::doodad::db::delete_doodad_by_id(server, &mut *conn, d).await
            {
                tracing::error!("{e}");
            }
        }
        for c in children {
            delete_slave_by_id(server, conn, c).await;
        }
        if delete_count == 0 {
            tracing::error!("Slave could not be deleted or did not exist, Id {db_id}");
            return false;
        }
        server.ids.character.release_id(db_id);
        true
    })
}
