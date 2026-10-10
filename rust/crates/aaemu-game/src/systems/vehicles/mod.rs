//! Phase-4 subsystem: vehicles — mates (mounts / pets), slaves (vehicles, ships, siege),
//! shipyards and transfers (carriages / airships on routes).
//!
//! C# scope: `Core/Managers/MateManager`, `SlaveManager`, `ShipyardManager`,
//! `TransferManager`, `GameData/{Mate,Slave,Transfer}GameData`, `Models/Game/{Mate,Slaves,
//! Shipyard,Transfers}`, `Models/Game/Units/{Mate,Slave,Transfer}` behaviour,
//! `Models/Game/Char/CharacterMates`, `ShipHarpoonRopeController`, the vehicle packets and the
//! vehicle branches of `CSMoveUnitPacket`.
//!
//! | module | C# |
//! |---|---|
//! | [`data`] | `MateGameData`, `SlaveGameData`, `TransferGameData`, shipyard templates, taxations, vehicle / ship models |
//! | [`mates`] | `MateManager`, `CharacterMates`, `Mate` (exp, xp task, regen, fall damage) |
//! | [`slaves`] | `SlaveManager`, `SlaveSpawner`, `Slave` (death, repair points, save) |
//! | [`transfers`] | `TransferManager`, `TransferSpawner`, `Transfer` route following |
//! | [`shipyards`] | `ShipyardManager`, `Shipyard` build steps |
//! | [`harpoon`] | `ShipHarpoonRopeController`, `CSUnhangPacket` |
//! | [`movement`] | the vehicle branches of `CSMoveUnitPacket` |
//! | [`effects`] | `RepairSlaveEffect`, the shipyard part of `CraftEffect`, backpack drop |
//! | [`stats`] | the `Mate` / `Slave` / `Transfer` / `Shipyard` attribute getters |
//! | [`hooks`] | physics / economy hooks, the skills / social hook wrappers |
//! | [`packets`] | every vehicle C->S / S->C packet |

pub mod data;
pub mod effects;
pub mod harpoon;
pub mod hooks;
pub mod mates;
pub mod movement;
pub mod packets;
pub mod ship_physics;
pub mod shipyards;
pub mod slaves;
pub mod stats;
pub mod transfers;

#[cfg(test)]
mod flow_tests;

use std::sync::Arc;
use std::time::Duration;

use crate::net::registry::PacketRegistry;
use crate::server::GameServer;
use crate::systems::SaveStage;
use crate::world::{PlayerRef, WorldInstance, WorldObject};

/// Game-data tables of this subsystem (`server.game_data.systems.vehicles`).
pub type Data = data::VehiclesData;

/// Managers of this subsystem (`server.systems.vehicles`).
#[derive(Debug, Default)]
pub struct System {
    /// `world.MateManager` of every instance + the characters' `CharacterMates`.
    pub mates: mates::MateManagers,
    /// The loaded `SlaveSpawner`s.
    pub slave_spawners: slaves::SlaveSpawners,
    /// `world.TransferManager` of every instance + the transfer spawners.
    pub transfers: transfers::TransferManagers,
    /// `ShipyardManager`.
    pub shipyards: shipyards::ShipyardManager,
    /// Hooks other owners install (physics, economy).
    pub hooks: hooks::VehicleHooks,
    /// `SlaveGameData._attachPoints` after a `/scripts reloadslavepoints` (`None` = the loaded
    /// game data).
    reloaded_attach_points: std::sync::RwLock<Option<AttachPoints>>,
}

/// `SlaveGameData._attachPoints` (model id -> attach point -> location).
type AttachPoints = std::collections::HashMap<u32, Vec<(u8, data::AttachPointLocation)>>;

impl System {
    /// `SlaveGameData.LoadSlaveAttachmentPointLocations()` at runtime (GM `scripts
    /// reloadslavepoints`): re-reads `Data/slave_attach_points*.json`.
    pub fn reload_slave_attachment_point_locations(&self, server: &GameServer) {
        let mut d = data::VehiclesData::default();
        d.load_slave_attachment_point_locations(&server.config.app_path.join("Data"));
        *self
            .reloaded_attach_points
            .write()
            .expect("attach points poisoned") = Some(d.attach_points);
    }

    /// `SlaveGameData.GetAttachPointsForSlave(modelId)` (the reloaded table when there is one).
    pub fn attach_points_for_slave(
        &self,
        server: &GameServer,
        model_id: u32,
    ) -> Option<Vec<(u8, data::AttachPointLocation)>> {
        if let Some(points) = self
            .reloaded_attach_points
            .read()
            .expect("attach points poisoned")
            .as_ref()
        {
            return points.get(&model_id).cloned();
        }
        server
            .game_data
            .systems
            .vehicles
            .attach_points_for_slave(model_id)
            .map(<[_]>::to_vec)
    }

    /// Startup: hook installation (skills `NpcSkillHooks`, social `SocialHooks`), the transfer
    /// tick (100 ms), `SendMySlaveTask` (5 s), `ShipyardTickTask` (1 min) and the mate / slave
    /// regen tick (`OnActiveRegionTick`, 1 s).
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        install_hooks(server);
        tracing::info!("TransferTickTask: Started");
        tracing::info!("Initialising Shipyard Manager...");
        tracing::warn!("ShipyardUpdateInfoTick: Started");
        spawn_ticker(
            server,
            Duration::from_millis(transfers::TICK_MS),
            Duration::ZERO,
            transfers::transfer_tick,
        );
        spawn_ticker(server, Duration::from_secs(5), Duration::ZERO, |s| {
            slaves::send_my_slave_packet_to_all_owners(s)
        });
        spawn_ticker(
            server,
            Duration::from_secs(60),
            Duration::from_secs(60),
            shipyards::shipyard_tick,
        );
        spawn_ticker(server, Duration::from_secs(1), Duration::ZERO, |s| {
            active_region_tick(s)
        });
        Ok(())
    }

    /// `SaveManager.DoSave` part (`AfterCharacters`): the online characters' mates
    /// (`Character.Save` -> `Mates.Save`) and every slave of every world. Returns the number of
    /// slaves saved (the C# commit counter).
    pub async fn save(
        &self,
        server: &GameServer,
        tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        stage: SaveStage,
    ) -> crate::Result<usize> {
        if stage != SaveStage::AfterCharacters {
            return Ok(0);
        }
        for p in server.world.all_characters() {
            let id = p.data().id;
            if let Err(e) = mates::save_character_mates(server, tx, id).await {
                tracing::error!("{e}");
            }
        }
        slaves::save_all(server, tx).await
    }
}

/// Hook installation of this subsystem: the ship physics hooks
/// ([`ship_physics::VehicleShipPhysics`]) and the services radar ship lists ([`RadarShips`]). The npcs `NpcSkillHooks` and social `SocialHooks` call
/// `systems::vehicles` directly for the mate / slave / dismount parts.
pub fn install_hooks(server: &GameServer) {
    server
        .systems
        .hooks
        .ship_physics
        .install(Arc::new(ship_physics::VehicleShipPhysics));
    server
        .systems
        .services
        .radar
        .ships
        .install(Arc::new(RadarShips));
}

/// `RadarShipHooks`: `SlaveManager.GetActiveSlavesByKinds(Boat, Fishboat, Speedboat,
/// MerchantShip, BigSailingShip, SmallSailingShip)` of one world instance.
pub struct RadarShips;

impl crate::systems::services::radar::RadarShipHooks for RadarShips {
    fn active_ships(
        &self,
        server: &GameServer,
        world: &Arc<WorldInstance>,
    ) -> Vec<crate::world::SlaveRef> {
        use data::slave_kind::*;
        slaves::get_active_slaves_by_kinds(
            server,
            world,
            &[
                BOAT,
                FISHBOAT,
                SPEEDBOAT,
                MERCHANT_SHIP,
                BIG_SAILING_SHIP,
                SMALL_SAILING_SHIP,
            ],
            u32::MAX,
        )
    }
}

fn spawn_ticker(
    server: &Arc<GameServer>,
    period: Duration,
    first_delay: Duration,
    f: impl Fn(&GameServer) + Send + Sync + 'static,
) {
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        if !first_delay.is_zero() {
            tokio::time::sleep(first_delay).await;
        }
        let mut iv = tokio::time::interval(period);
        loop {
            iv.tick().await;
            let Some(s) = weak.upgrade() else { break };
            f(&s);
        }
    });
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    packets::register(reg);
}

// ---------------------------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------------------------

/// `DateTime.UtcNow` in unix ms.
pub fn now_ms() -> i64 {
    crate::world::now_ms()
}

/// The world instance of an object (its region's instance, else its transform's).
pub fn world_of(server: &GameServer, obj: &WorldObject) -> Option<Arc<WorldInstance>> {
    server.world.world_of(obj)
}

/// `GameObject.BroadcastPacket(packet, self)`.
pub fn broadcast(
    server: &GameServer,
    obj: &WorldObject,
    packet: &dyn crate::net::packet::ServerPacket,
    include_self: bool,
) {
    crate::systems::skills::broadcast(server, obj, packet, include_self);
}

/// `ErrorMessageType` values this subsystem sends.
pub mod error {
    pub const NO_PERMISSION_TO_LOOT: i16 = 6;
    pub const ITEM_FAILED_REPAIR: i16 = 23;
    pub const NOT_ENOUGH_MONEY: i16 = 26;
    pub const NOT_ENOUGH_ITEM: i16 = 27;
    pub const SLAVE_ALREADY_HAS_MASTER: i16 = 97;
    pub const SLAVE_SPAWN_ITEM_LOCKED: i16 = 101;
    pub const NOT_READY: i16 = 206;
    pub const SLAVE_SPAWN_ERROR_NEED_REPAIR_TIME: i16 = 540;
    pub const SLAVE_EQUIPMENT_LOADED_ITEM: i16 = 801;
}

/// `Character.SendErrorMessage(type)`.
pub fn error(p: &PlayerRef, error: i16) {
    p.send(crate::packets::world::SCErrorMsgPacket::new(error, 0, true));
}

/// `Unit.SetPosition(x, y, z, roll, pitch, yaw)` of a world object (+ the region update of
/// `WorldManager.AddVisibleObject` and, for characters, the skills tail of
/// `Character.SetPosition`).
#[allow(clippy::too_many_arguments)]
pub fn set_unit_position(
    server: &GameServer,
    obj: &WorldObject,
    x: f32,
    y: f32,
    z: f32,
    rx: f32,
    ry: f32,
    rz: f32,
) {
    let old = {
        let t = obj.transform();
        (t.x, t.y, t.z)
    };
    obj.with_transform(|t| t.set_position(x, y, z, rx, ry, rz));
    if let Some(world) = world_of(server, obj) {
        let changes = world.add_visible_object(obj, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
    }
    if let Some(p) = obj.as_player() {
        crate::systems::skills::on_moved(server, p, old, 0);
    }
}

// ---------------------------------------------------------------------------------------------
// Entry points for shared code
// ---------------------------------------------------------------------------------------------

/// `Character.ForceDismount(reason)`: off any mate seat, off any slave, out of a doodad seat,
/// `Transform.DetachAll()`. `true` when a mate / slave dismount happened.
pub fn force_dismount(server: &GameServer, player: &PlayerRef, reason: u8) -> bool {
    let mut res = false;
    let instance = player.data().unit.transform.instance_id;
    if let Some((mate, point)) = server
        .systems
        .vehicles
        .mates
        .get(instance)
        .get_is_mounted(player.obj_id)
    {
        let tl = u32::from(mate.data().unit.tl_id);
        mates::unmount_mate(server, player, tl, point, reason);
        res = true;
    }
    if let Some(world) = server.world.world(instance) {
        if let Some((slave, _)) = slaves::get_is_mounted(&world, player.obj_id) {
            let tl = u32::from(slave.data().unit.tl_id);
            slaves::unbind_slave(server, player, tl, reason);
            res = true;
        }
    }
    // If they are still sitting down, detach them first
    let bonding = player.data().bonding;
    if let Some(b) = bonding {
        crate::systems::npcs::doodad::manager::unbond_doodad(
            server,
            player,
            player.obj_id,
            b.owner_obj_id,
        );
    }
    let me = WorldObject::from(player);
    if let Some(world) = world_of(server, &me) {
        world.detach_child(&me);
        for child in me.core().take_children() {
            child.with_transform(|t| t.parent_obj_id = 0);
        }
    }
    res
}

/// `Unit.DoDie` overrides of the vehicle kinds: `true` when the death was handled here
/// (`Slave.DoDie` replaces the base implementation).
pub fn on_do_die(
    server: &GameServer,
    victim: &WorldObject,
    killer: Option<&WorldObject>,
    kill_reason: crate::systems::skills::types::KillReason,
) -> bool {
    match victim {
        WorldObject::Slave(s) => {
            slaves::do_die(server, s, killer, kill_reason);
            true
        }
        _ => false,
    }
}

/// `Events.OnDeath` subscribers of the vehicle kinds (`Shipyard.OnDeath`), raised by the base
/// `Unit.DoDie` right after the buff death triggers.
pub fn on_death_event(server: &GameServer, victim: &WorldObject) {
    if let WorldObject::Shipyard(s) = victim {
        shipyards::on_death(server, s);
    }
}

/// `DoHpChangeTrigger(triggerValue, ...)` overrides (`Slave`: repair points).
pub fn on_hp_change_trigger(server: &GameServer, unit: &WorldObject, trigger_value: i32) {
    if let WorldObject::Slave(s) = unit {
        slaves::do_hp_change_trigger(server, s, trigger_value);
    }
}

/// `Unit.OnZoneChange` overrides (`Mate` / `Slave`: the passengers' zone change).
pub fn on_unit_zone_change(server: &GameServer, unit: &WorldObject, last_zone: u32, new_zone: u32) {
    match unit {
        WorldObject::Mate(m) => mates::on_zone_change(server, m, last_zone, new_zone),
        WorldObject::Slave(s) => slaves::on_zone_change(server, s, last_zone, new_zone),
        _ => {}
    }
}

/// `Unit.DoFallDamage` with the `Mate` override (riders take the fall too).
pub fn do_fall_damage(server: &GameServer, unit: &WorldObject, fall_vel: u16) -> i32 {
    match unit {
        WorldObject::Mate(m) => mates::do_fall_damage(server, m, fall_vel),
        other => crate::systems::skills::combat::do_fall_damage(server, other, fall_vel),
    }
}

/// `Unit.OnActiveRegionTick` regen part of the vehicle kinds (`Mate.RegenTick`,
/// `Slave.RegenTick`) for the units around players.
pub fn active_region_tick(server: &GameServer) {
    let mut seen = std::collections::HashSet::new();
    for world in server.world.worlds() {
        for p in world.all_characters() {
            let me = WorldObject::from(&p);
            for o in world.objects_around(&me) {
                if !seen.insert(o.obj_id()) {
                    continue;
                }
                match &o {
                    WorldObject::Mate(m) => mates::regen_tick(server, m),
                    WorldObject::Slave(s) => slaves::regen_tick(server, s),
                    _ => {}
                }
            }
        }
    }
}

/// `CSSelectCharacterPacket` part: every instance's mates of the character are despawned and
/// a pending vehicle despawn task is cancelled.
pub fn on_character_selected(server: &GameServer, player: &PlayerRef) {
    let char_id = player.data().id;
    mates::on_character_selected(server, char_id);
    let instance = player.data().unit.transform.instance_id;
    if let Some(world) = server.world.world(instance) {
        if let Some(slave) = slaves::get_active_slave_by_owner_obj_id(&world, player.obj_id) {
            tracing::warn!(
                "{}: Abort the task of disabling vehicles",
                player.data().unit.name
            );
            slave.data().leave_task = 0;
        }
    }
}

/// `CharacterMates.Load` (called from `Character.Load` at character select).
pub async fn load_character(server: &GameServer, character_id: u32) {
    if let Err(e) = mates::load_character_mates(server, character_id).await {
        tracing::error!("CharacterMates.Load({character_id}): {e}");
    }
}

/// `Character.SaveDirectlyToDatabase` part: `Mates.Save`.
pub async fn save_character(server: &GameServer, character_id: u32) {
    let mut conn = match server.db.acquire().await {
        Ok(c) => c,
        Err(e) => {
            tracing::error!("Mates.Save: {e}");
            return;
        }
    };
    if let Err(e) = mates::save_character_mates(server, &mut conn, character_id).await {
        tracing::error!("Mates.Save: {e}");
    }
}

/// The logout extension of `EnterWorldManager.Leave` (+10 minutes with an active vehicle).
pub fn leave_extra_logout_ms(server: &GameServer, player: &PlayerRef) -> i32 {
    let instance = player.data().unit.transform.instance_id;
    match server.world.world(instance) {
        Some(w) if slaves::get_active_slave_by_owner_obj_id(&w, player.obj_id).is_some() => {
            1000 * 60 * 10
        }
        _ => 0,
    }
}

/// `EnterWorldManager.LeaveWorldTask` part: despawn the owned mates and slaves (the caller then
/// closes coffers and calls [`force_dismount`], as the C# order).
pub fn on_leave_world(server: &GameServer, player: &PlayerRef) {
    let (instance, id) = {
        let c = player.data();
        (c.unit.transform.instance_id, c.id)
    };
    mates::remove_and_despawn_all_active_owned_mates(server, instance, id);
    slaves::remove_and_despawn_all_active_owned_slaves(server, player, false);
}

/// `GameConnection.OnDisconnect` part: `RemoveAndDespawnActiveOwnedMatesSlaves()` (owned mates
/// now, the active vehicle 10 minutes later unless the character logs in again).
pub fn on_disconnect(server: &GameServer, player: &PlayerRef) {
    let (instance, id) = {
        let c = player.data();
        (c.unit.transform.instance_id, c.id)
    };
    mates::remove_and_despawn_all_active_owned_mates(server, instance, id);
    force_dismount_and_despawn(server, player, 1000 * 60 * 10);
}

static LEAVE_TASK_GEN: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);

/// `Character.ForceDismountAndDespawn(reason, timeToDespawn)`.
pub fn force_dismount_and_despawn(
    server: &GameServer,
    player: &PlayerRef,
    time_to_despawn_ms: u64,
) -> bool {
    let res = force_dismount(server, player, slaves::attach_reason::PREFAB_CHANGED);
    let instance = player.data().unit.transform.instance_id;
    let Some(world) = server.world.world(instance) else {
        return res;
    };
    let Some(slave) = slaves::get_active_slave_by_owner_obj_id(&world, player.obj_id) else {
        return res;
    };
    let gen = LEAVE_TASK_GEN.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    slave.data().leave_task = gen;
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return res;
    };
    let (player, slave) = (player.clone(), slave.clone());
    rt.spawn(async move {
        tokio::time::sleep(Duration::from_millis(time_to_despawn_ms)).await;
        if slave.data().leave_task != gen {
            return;
        }
        slaves::remove_and_despawn_all_active_owned_slaves(&server, &player, false);
    });
    res
}

/// `Character.ForceDismountAndDespawn(slave, timeToDespawn)` (Mirage test vehicles): dismount
/// now, `RemoveAndDespawnTestSlave` (`Delete(owner, slave.ObjId, false)`) later.
pub fn force_dismount_and_despawn_test_slave(
    server: &GameServer,
    player: &PlayerRef,
    slave: Option<&crate::world::SlaveRef>,
    time_to_despawn_ms: u64,
) -> bool {
    let res = force_dismount(server, player, slaves::attach_reason::PREFAB_CHANGED);
    let Some(slave) = slave else {
        return res;
    };
    let gen = LEAVE_TASK_GEN.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    slave.data().leave_task = gen;
    let (Some(server), Ok(rt)) = (server.self_arc(), tokio::runtime::Handle::try_current()) else {
        return res;
    };
    let (player, slave) = (player.clone(), slave.clone());
    rt.spawn(async move {
        tokio::time::sleep(Duration::from_millis(time_to_despawn_ms)).await;
        if slave.data().leave_task != gen {
            return;
        }
        let instance = slave.data().unit.transform.instance_id;
        slaves::delete(&server, Some(&player), instance, slave.obj_id, false);
    });
    res
}

/// `PortalManager.UsePortal` part when changing instance: refuse when the active vehicle holds
/// an item (`SlaveEquipmentLoadedItem`, `false`), else despawn the owned mates and slaves.
pub fn on_instance_change(server: &GameServer, player: &PlayerRef) -> bool {
    let instance = player.data().unit.transform.instance_id;
    if let Some(world) = server.world.world(instance) {
        if let Some(slave) = slaves::get_active_slave_by_owner_obj_id(&world, player.obj_id) {
            let loaded = slave.data().attached_doodads.iter().any(|id| {
                world.doodad(*id).is_some_and(|d| {
                    let d = d.data();
                    d.item_id != 0 || d.item_template_id != 0
                })
            });
            if loaded {
                error(player, error::SLAVE_EQUIPMENT_LOADED_ITEM);
                return false;
            }
        }
    }
    let id = player.data().id;
    mates::remove_and_despawn_all_active_owned_mates(server, instance, id);
    slaves::remove_and_despawn_all_active_owned_slaves(server, player, false);
    true
}

/// The mount / slave branch of `CSStartSkillPacket` after the mount skill was used: the
/// harpoon rope controller (slave casters) and the rider / operator skill
/// (`MateGameData.GetMountAttachedSkills`). Returns the rider skill id (0 = none).
pub fn on_mount_skill_used(
    server: &GameServer,
    player: &PlayerRef,
    caster: &WorldObject,
    skill_id: u32,
    success: bool,
    target: &crate::systems::skills::types::SkillCastTarget,
) -> u32 {
    let is_vehicle = caster.as_mate().is_some() || caster.as_slave().is_some();
    let attached = if is_vehicle {
        let ap = player.data().attached_point;
        server
            .game_data
            .systems
            .vehicles
            .mount_attached_skill(skill_id, ap)
    } else {
        0
    };
    if success {
        if let Some(slave) = caster.as_slave() {
            harpoon::on_slave_skill_used(server, slave, skill_id, target, player);
        }
    }
    attached
}

/// `transfer.Spawner.Respawn(transfer)` (`Spawner<T>.Respawn` = `Spawn(0)`) of the spawn
/// manager's respawn tick.
pub fn respawn_object(server: &GameServer, obj: &WorldObject) {
    if let WorldObject::Transfer(t) = obj {
        if let Some(s) = server.systems.vehicles.transfers.spawner_of(t.obj_id) {
            s.spawn(server, 0);
        }
    }
}

/// `GameObject.Delete()` of the vehicle kinds by the spawn manager's despawn tick
/// (`transfer.Spawner.Despawn(transfer)`, `slave.Delete()`): `true` when handled.
pub fn despawn_object(server: &GameServer, obj: &WorldObject) -> bool {
    match obj {
        WorldObject::Transfer(t) => match server.systems.vehicles.transfers.spawner_of(t.obj_id) {
            Some(s) => {
                s.despawn(server, t);
                true
            }
            None => false,
        },
        _ => false,
    }
}
