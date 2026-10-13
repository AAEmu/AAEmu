//! World-instance lifecycle (`WorldManager.CreateWorldInstance` / `RemoveWorld`,
//! `WorldInstance.CleanupInstance`, `WorldIdManager`) and the character transforms the
//! instance code moves around (`Character.MainWorldPosition`, `Transform.CloneDetached`,
//! `Transform.ApplyWorldSpawnPosition`, `CSInstanceLoadedPacket`).
//!
//! Moving a player between instances works as in C#: the transform gets the new instance id and
//! the client is told with `SCLoadInstancePacket`; the region switch happens on the next
//! `AddVisibleObject` of the player (its first movement after `CSInstanceLoadedPacket`), where
//! `world::session::move_unit` leaves the old instance's region grid first
//! (`WorldManager::leave_other_instance`, the cross-world branch of the C# region change).

use std::sync::{Arc, Mutex};

use dashmap::DashMap;

use crate::data::world_templates::{WorldSpawnPosition, WorldTemplate};
use crate::models::character::DEFAULT_INSTANCE_ID;
use crate::models::transform::Transform;
use crate::packets::world::{SCDetailedTimeOfDayPacket, SCErrorMsgPacket};
use crate::server::GameServer;
use crate::systems::npcs::portal::packets::SCLoadInstancePacket;
use crate::world::{session, PlayerRef, WorldInstance, WorldObject};

use super::packets::SCProcessingInstancePacket;

/// `WorldInstance` members the Rust `world::WorldInstance` does not carry.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct WorldInfo {
    /// `ChannelId`.
    pub channel_id: u32,
    /// `IsFixedInstanceId` (the id is not released when the instance goes away).
    pub fixed_id: bool,
}

/// Per-instance bookkeeping of this subsystem (`server.systems.instances.worlds`).
#[derive(Debug, Default)]
pub struct InstanceWorlds {
    info: DashMap<u32, WorldInfo>,
    creation: Mutex<()>,
}

impl InstanceWorlds {
    /// `WorldInstance.ChannelId` (0 for instances not created here, e.g. main_world).
    pub fn channel_id(&self, world_id: u32) -> u32 {
        self.info.get(&world_id).map_or(0, |i| i.channel_id)
    }

    pub fn info(&self, world_id: u32) -> Option<WorldInfo> {
        self.info.get(&world_id).map(|i| *i)
    }
}

/// `MathUtil.DegToRad` (float).
pub fn deg_to_rad(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

/// `ErrorMessageType` values used by this subsystem.
pub mod error_message {
    pub const NO_SERVER_INSTANCE_RESOURCE: i16 = 253;
    pub const TRY_LATER_INSTANCE: i16 = 254;
    pub const INVALID_RETURN_POS_INSTANCE: i16 = 257;
    pub const PROHIBITED_IN_INSTANCE: i16 = 258;
    pub const ALREADY_UNBOUND_INSTANCE: i16 = 259;
    pub const INSTANCE_VISIT_LIMIT: i16 = 453;
    pub const INSTANCE_LEVEL: i16 = 470;
    pub const INSTANCE_QUOTA: i16 = 471;
    pub const INSTANCE_LEAVE_PARTY: i16 = 472;
    pub const CANNOT_USE_PORTAL_IN_TRIAL: i16 = 611;
    pub const ENTER_INST_REQ_ITEM: i16 = 632;
    pub const NEED_PARTY: i16 = 671;
}

/// `character.SendErrorMessage(type, subType = 0, isNotify = true)`.
pub fn send_error(p: &PlayerRef, error: i16, kind: u32) {
    p.send(SCErrorMsgPacket::new(error, kind, true));
}

/// `WorldManager.CreateWorldInstance(template, channelId, overrideInstanceId, fixedInstanceId,
/// notifyPlayer)`: registers the instance, tells `notify` (`SCProcessingInstancePacket`) and
/// creates its `SpawnManager` (the spawn files are read by [`load_spawns`]). Templates that
/// cannot be instanced return their existing instance. `Err` = the C# exceptions (fixed id too
/// large, duplicate id, no free id).
pub fn create_world_instance(
    server: &GameServer,
    template: &WorldTemplate,
    channel_id: u32,
    override_instance_id: bool,
    fixed_instance_id: u32,
    notify: Option<&PlayerRef>,
) -> Result<Arc<WorldInstance>, String> {
    let _creation = server
        .systems
        .instances
        .worlds
        .creation
        .lock()
        .expect("world creation poisoned");
    // If it's marked as an instance or if it only has 1 zone defined, then it's a "dungeon"
    let can_be_instanced =
        template.xml_world.is_instance > 0 || template.xml_world.zones.len() <= 1;
    if !can_be_instanced {
        let mut worlds = server.world.worlds();
        worlds.sort_by_key(|w| w.id);
        if let Some(previous) = worlds.into_iter().find(|w| w.template.id == template.id) {
            tracing::warn!(
                "Tried to create a new instance of {} which does not allow multiple instances. Using InstanceId {} instead!",
                template.name,
                previous.id
            );
            return Ok(previous);
        }
    }
    if fixed_instance_id > 100 {
        return Err("Fixed Instance ID is too large".into());
    }
    if server.world.worlds().len() as u64 >= u64::from(server.config.world.max_instances) {
        return Err("Maximum number of world instances reached".into());
    }
    let id = if override_instance_id {
        fixed_instance_id
    } else {
        server.ids.world.get_next_id().map_err(|e| e.to_string())?
    };
    if server.world.has_world(id) {
        return Err(format!("World instance with id {id} already exists"));
    }
    let world = server
        .world
        .create_world_instance(Arc::new(template.clone()), id);
    server.systems.instances.worlds.info.insert(
        id,
        WorldInfo {
            channel_id,
            fixed_id: override_instance_id,
        },
    );
    if let Some(p) = notify {
        p.send(SCProcessingInstancePacket {
            zone_id: template.zone_keys.first().copied().unwrap_or(0) as i32,
        });
    }
    // world.SpawnManager = new SpawnManager(world) (Load: `load_spawns`).
    server.systems.npcs.spawns.get_or_create(&world);
    Ok(world)
}

/// `world.SpawnManager.Load()` (spawn files of `Data/Worlds/<template name>`). The load future
/// is not `Send` (its template filters), so it runs on a blocking thread.
pub async fn load_spawns(server: &Arc<GameServer>, world: &Arc<WorldInstance>) {
    let (s, w) = (server.clone(), world.clone());
    let res = tokio::task::spawn_blocking(move || {
        let manager = s.systems.npcs.spawns.get_or_create(&w);
        tokio::runtime::Handle::current().block_on(manager.load(&s))
    })
    .await;
    match res {
        Ok(Ok(_)) => {}
        Ok(Err(e)) => tracing::error!("SpawnManager.Load for {}: {e:#}", world_name(world)),
        Err(e) => tracing::error!("SpawnManager.Load for {}: {e}", world_name(world)),
    }
}

/// `WorldInstance.ToString()` (`{Id}-{Template.Name}({Template.Id})`).
pub fn world_name(world: &WorldInstance) -> String {
    format!(
        "{}-{}({})",
        world.id, world.template.name, world.template.id
    )
}

/// `WorldManager.RemoveWorld(worldInstanceId)`.
pub fn remove_world(server: &GameServer, world_id: u32) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    super::tower_def::cleanup_world(server, world_id);
    if !server.world.remove_world(world_id) {
        tracing::info!("[Dungeon] couldn't remove the dungeon id={world_id}!");
    }
    server.systems.instances.worlds.info.remove(&world_id);
}

/// `WorldIdManager.ReleaseId(id)` (ids below the first id log an error, as in C#).
pub fn release_world_id(server: &GameServer, world_id: u32) {
    server.ids.world.release_id(world_id);
}

/// `WorldInstance.CleanupInstance()`: stops the respawn loop, removes the spawners and what they
/// spawned, then despawns whatever is left; the instance's `SpawnManager` is dropped.
pub fn cleanup_instance(server: &GameServer, world_id: u32) {
    let _serial = server.systems.npcs.spawns.spawn_lock();
    super::tower_def::cleanup_world(server, world_id);
    let spawns = &server.systems.npcs.spawns;
    let Some(manager) = spawns.get(world_id) else {
        return;
    };
    manager.stop();
    manager.delete_all_spawners(server);
    let n = manager.despawn_all(server);
    spawns.remove(world_id);
    tracing::debug!("Removed objects from WorldInstance {world_id} ({n} despawned)");
}

/// `WorldInstance.HasCharacter(playerId)`.
pub fn has_character(world: &WorldInstance, character_id: u32) -> bool {
    world
        .all_characters()
        .iter()
        .any(|c| c.character_id == character_id)
}

/// `WorldInstance.GetCharacterCount()`.
pub fn character_count(world: &WorldInstance) -> usize {
    world.player_count()
}

/// `WorldInstance.ListPlayerNames(max)`.
pub fn list_player_names(world: &WorldInstance, max: usize) -> String {
    let players = world.all_characters();
    if players.len() > max {
        return players.len().to_string();
    }
    if players.is_empty() {
        return "[none]".into();
    }
    players
        .iter()
        .map(|p| p.data().unit.name.clone())
        .collect::<Vec<_>>()
        .join(", ")
}

/// The world template id of an instance (`Transform.WorldId` of objects in it).
fn template_id_of(server: &GameServer, instance_id: u32) -> u32 {
    server
        .world
        .worlds()
        .into_iter()
        .find(|w| w.id == instance_id)
        .map_or(0, |w| w.template.id)
}

/// The spawn point `Dungeon.MoveCharacterTo*` uses: the `XmlWorldZones` entry with the zone key
/// (its `world_spawns.json` position, `None` when the zone has none), else the template's own.
pub fn zone_spawn_position(
    server: &GameServer,
    template: &WorldTemplate,
    zone_key: u32,
) -> Option<WorldSpawnPosition> {
    let lookups = server.game_data.worlds.world_spawn_lookups();
    let lookup = |name: &str| {
        lookups
            .iter()
            .find(|l| l.name.as_deref() == Some(name))
            .and_then(|l| l.spawn_position)
    };
    for wz in template.xml_world_zones().values() {
        if wz.zone_key == zone_key {
            return lookup(&wz.name).map(|_| wz.spawn_position);
        }
    }
    lookup(&template.name).map(|_| template.spawn_position)
}

/// `Transform.CloneDetached(character)`: the current world position as a detached transform.
/// Note (C#): the clone's `ZoneId` setter runs `character.OnZoneChange(0, zoneId)`; mirrored.
pub fn clone_detached(server: &GameServer, p: &PlayerRef) -> Transform {
    let obj = WorldObject::from(p);
    let pr = match server.world.world_of(&obj) {
        Some(w) => w.world_pos_rot(&obj),
        None => p.data().unit.transform.local(),
    };
    let t = p.data().unit.transform;
    let clone = Transform {
        instance_id: t.instance_id,
        world_id: template_id_of(server, t.instance_id),
        zone_id: t.zone_id,
        x: pr.x,
        y: pr.y,
        z: pr.z,
        rot_x: pr.rot_x,
        rot_y: pr.rot_y,
        rot_z: pr.rot_z,
        parent_obj_id: 0,
    };
    if clone.zone_id != 0 {
        session::on_zone_change(server, p, 0, clone.zone_id);
    }
    clone
}

/// Detaches a player from its parent (`Transform.DetachAll` for the parent part).
fn detach(server: &GameServer, p: &PlayerRef) {
    let parent = p.data().unit.transform.parent_obj_id;
    if parent == 0 {
        return;
    }
    let obj = WorldObject::from(p);
    match server.world.world_of(&obj) {
        Some(w) => w.detach_child(&obj),
        None => p.data().unit.transform.parent_obj_id = 0,
    }
}

/// `character.Transform.ApplyWorldSpawnPosition(wsp, newInstanceId)`. Note (C#): the rotation
/// is stored as given (degrees from `world_spawns.json`) and the `ZoneId` setter fires
/// `OnZoneChange(old, new)`; both mirrored.
pub fn apply_world_spawn_position(
    server: &GameServer,
    p: &PlayerRef,
    wsp: &WorldSpawnPosition,
    new_instance_id: u32,
) {
    detach(server, p);
    let world_id = (new_instance_id != 0).then(|| template_id_of(server, new_instance_id));
    let old_zone = {
        let mut c = p.data();
        let t = &mut c.unit.transform;
        if new_instance_id != 0 {
            t.instance_id = new_instance_id;
            t.world_id = world_id.unwrap_or(0);
        }
        let old = t.zone_id;
        t.zone_id = wsp.zone_id;
        t.set_position(wsp.x, wsp.y, wsp.z, wsp.roll, wsp.pitch, wsp.yaw);
        old
    };
    if old_zone != wsp.zone_id {
        session::on_zone_change(server, p, old_zone, wsp.zone_id);
    }
}

/// `character.Transform = character.MainWorldPosition.Clone(); Transform.InstanceId =
/// DefaultInstanceId` + `SCLoadInstancePacket(MainWorldPosition.WorldId, ZoneId, World.Position,
/// World.Rotation.DegToRad())`. `None` when there is no `MainWorldPosition` (nothing changes).
///
/// Mirrored C# quirks: the rotation (already in radians) goes through `DegToRad` again, and the
/// clone's `ZoneId` setter fires `OnZoneChange(0, zoneId)` (the instance's zone chat channel is
/// not left).
pub fn return_to_main_world(server: &GameServer, p: &PlayerRef) -> Option<Transform> {
    let mwp = p.data().main_world_position?;
    detach(server, p);
    {
        let mut c = p.data();
        c.disabled_set_position = true;
        c.unit.transform = Transform {
            instance_id: DEFAULT_INSTANCE_ID,
            world_id: mwp.world_id,
            zone_id: mwp.zone_id,
            x: mwp.x,
            y: mwp.y,
            z: mwp.z,
            rot_x: mwp.rot_x,
            rot_y: mwp.rot_y,
            rot_z: mwp.rot_z,
            parent_obj_id: 0,
        };
    }
    if mwp.zone_id != 0 {
        session::on_zone_change(server, p, 0, mwp.zone_id);
    }
    p.send(SCLoadInstancePacket {
        instance_id: mwp.world_id,
        zone_id: mwp.zone_id,
        x: mwp.x,
        y: mwp.y,
        z: mwp.z,
        ang_x: deg_to_rad(mwp.rot_x),
        ang_y: deg_to_rad(mwp.rot_y),
        ang_z: deg_to_rad(mwp.rot_z),
    });
    Some(mwp)
}

/// `CSInstanceLoadedPacket.Read`: `SCUnitStatePacket(ActiveChar)`, `SCDetailedTimeOfDayPacket(12)`
/// and `DisabledSetPosition = false`.
pub fn instance_loaded(server: &GameServer, p: &PlayerRef) {
    let packet = {
        let mut c = p.data();
        crate::world::unit_state::unit_state(&mut c, &server.game_data, crate::world::now_ms())
    };
    // Writing the unit state can add the forced buffs (their start runs in the skills hooks).
    crate::systems::dispatch_buff_events(server, &WorldObject::from(p));
    p.send(packet);
    p.send(SCDetailedTimeOfDayPacket { time: 12.0 });
    p.data().disabled_set_position = false;
    tracing::debug!("InstanceLoaded.");
}
