//! `DoodadManager` runtime (`Core/Managers/UnitManagers/DoodadManager.cs`: `Create`,
//! `CreatePlayerDoodad`, `OpenCofferDoodad`, `CloseCofferDoodad`, `CloseCoffersOpenedBy`,
//! `ChangeDoodadData`) and the public entry points of the doodad phase machine (`Doodad.Spawn`,
//! `Delete`, `Use`, `DoChangePhase`, `OnSkillHit`, `InitDoodad`, `Save`, the `FuncGroupId`
//! setter), plus `TimeManager.OnTimeOfDayChange` (doodad ToD triggers).
//!
//! The read-only getters of `DoodadManager` live in [`super::data::DoodadData`].
//!
//! Deviations (see PORTING.md):
//! * `Create(..., skipPhaseInitialization: false)` runs `InitDoodad` on a spawned tokio task
//!   (`Task.Run`); without a shared server handle / runtime it runs synchronously.
//! * `Delete()` also cancels the doodad's `FuncTask` (C# only `DoodadSpawner.Despawn` does), so
//!   no timer keeps firing for a deleted doodad.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, RwLock};

use super::packets::{
    error_message, error_msg, CSCreateDoodadPacket, SCDoodadChangedPacket, SCUnbondDoodadPacket,
};
use super::phase;
use super::runtime::DoodadCtx;
use super::tasks::{self, TaskScheduler};
use crate::models::doodad::{doodad_owner_type, Doodad};
use crate::server::GameServer;
use crate::systems::npcs::{area_trigger, public_farm};
use crate::world::{now_ms, DoodadRef, Entity, PlayerRef, WorldInstance, WorldObject};

#[cfg(test)]
#[path = "placement_tests.rs"]
mod placement_tests;

/// `HousingPermission` values `ChangeDoodadData` checks.
mod housing_permission {
    pub const PRIVATE: i32 = 0;
    pub const GUILD: i32 = 1;
    pub const PUBLIC: i32 = 2;
    pub const FAMILY: i32 = 3;
}

/// `DoodadManager` runtime state (`server.systems.npcs.doodads`).
#[derive(Debug)]
pub struct DoodadManager {
    /// Doodad func timers (`TaskManager` for `DoodadFunc*Task`).
    pub tasks: TaskScheduler,
    /// Ordered on-change doodad writes, drained by the save lifecycle.
    pub persistence: super::db::Persistence,
    /// `AppConfiguration.World.GrowthRate` (runtime-changeable, `Unit.SetGrowthRate`).
    growth_rate: Mutex<f64>,
    /// `ZoneManager._climateElem`: zone climate id -> climates.
    climates: RwLock<HashMap<u32, Vec<i32>>>,
    /// Test override of the `PhaseRatio` roll.
    forced_phase_ratio: Mutex<Option<i32>>,
}

impl Default for DoodadManager {
    fn default() -> Self {
        Self {
            tasks: TaskScheduler::default(),
            persistence: super::db::Persistence::default(),
            growth_rate: Mutex::new(1.0),
            climates: RwLock::new(HashMap::new()),
            forced_phase_ratio: Mutex::new(None),
        }
    }
}

impl DoodadManager {
    /// `World.GrowthRate`.
    pub fn growth_rate(&self) -> f64 {
        *self.growth_rate.lock().expect("doodads poisoned")
    }

    /// `Unit.SetGrowthRate(value)` (`/world set growthrate`).
    pub fn set_growth_rate(&self, value: f64) {
        *self.growth_rate.lock().expect("doodads poisoned") = value;
    }

    /// `ZoneManager.GetClimatesByZone` for a zone climate id.
    pub fn climates_of(&self, zone_climate_id: u32) -> Vec<i32> {
        self.climates
            .read()
            .expect("doodads poisoned")
            .get(&zone_climate_id)
            .cloned()
            .unwrap_or_default()
    }

    /// Installs `zone_climate_elems` (zone climate id, climate id) rows.
    pub fn set_climate_elems(&self, rows: &[(u32, i32)]) {
        let mut m = self.climates.write().expect("doodads poisoned");
        m.clear();
        for &(zone_climate, climate) in rows {
            m.entry(zone_climate).or_default().push(climate);
        }
    }

    /// The forced `PhaseRatio` roll (tests), `None` = `Random.Shared.Next(0, 10000)`.
    pub fn forced_phase_ratio(&self) -> Option<i32> {
        *self.forced_phase_ratio.lock().expect("doodads poisoned")
    }

    pub fn set_forced_phase_ratio(&self, v: Option<i32>) {
        *self.forced_phase_ratio.lock().expect("doodads poisoned") = v;
    }
}

/// Loads `zone_climate_elems` from `Data/compact.sqlite3` (`ZoneManager.Load` climate part).
pub fn load_climate_elems(server: &GameServer) {
    let path = server.config.app_path.join("Data").join("compact.sqlite3");
    let Ok(conn) =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
    else {
        tracing::warn!("zone_climate_elems: cannot open {}", path.display());
        return;
    };
    let mut rows = Vec::new();
    let res =
        crate::data::sqlite::for_each_row(&conn, "SELECT * FROM zone_climate_elems", [], |r| {
            rows.push((r.u32("zone_climate_id")?, r.u32("climate_id")? as i32));
            Ok(())
        });
    if let Err(e) = res {
        tracing::warn!("zone_climate_elems: {e}");
    }
    tracing::info!("Loaded {} climate elems", rows.len());
    server.systems.npcs.doodads.set_climate_elems(&rows);
}

/// `DoodadManager.Create(parentWorld, bcId, templateId, ownerObject, skipPhaseInitialization)`:
/// a new doodad (not yet shown). `obj_id == 0` allocates a new object id. `None` when the
/// template does not exist.
pub fn create(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    obj_id: u32,
    template_id: u32,
    owner: Option<&WorldObject>,
    skip_phase_init: bool,
) -> Option<DoodadRef> {
    let data = &server.game_data.systems.npcs.doodad;
    let template = data.template(template_id)?;
    let obj_id = if obj_id > 0 {
        obj_id
    } else {
        server.ids.object.get_next_id().ok()?
    };
    let mut d = Doodad::new(template.id);
    // Check if template is a Coffer
    d.coffer = super::coffer::new_coffer_state(server, template.id);
    d.transform.instance_id = world.id;
    d.owner_obj_id = owner.map_or(0, WorldObject::obj_id);
    d.plant_time = chrono::Utc::now().timestamp();
    d.owner_type = doodad_owner_type::SYSTEM;
    // FuncGroupId = GetFuncGroupId() (setter; not persistent yet -> no Save)
    let start = phase::get_func_group_id(data, template.id);
    if start != d.func_group_id {
        d.func_group_id = start;
        d.phase_time_ms = now_ms();
        phase::refresh_phase_caches(data, &mut d);
    }
    match owner {
        Some(WorldObject::Player(p)) => {
            d.owner_id = p.data().id;
            d.owner_type = doodad_owner_type::CHARACTER;
        }
        Some(WorldObject::House(h)) => {
            let (house_id, house_owner) = {
                let h = h.data();
                (h.id, h.unit.owner_id)
            };
            d.owner_obj_id = 0;
            d.parent_obj_id = h.obj_id;
            d.owner_id = house_owner;
            d.owner_type = doodad_owner_type::HOUSING;
            d.owner_db_id = house_id;
        }
        Some(WorldObject::Transfer(t)) => {
            d.owner_id = 0;
            d.parent_obj_id = t.obj_id;
            d.owner_type = doodad_owner_type::SYSTEM;
        }
        _ => {}
    }
    let doodad = Entity::new(obj_id, d);
    if !skip_phase_init {
        // Task.Run(() => doodad.InitDoodad())
        match (server.self_arc(), tokio::runtime::Handle::try_current()) {
            (Some(server), Ok(rt)) => {
                let (world, doodad) = (world.clone(), doodad.clone());
                rt.spawn(async move {
                    phase::init_doodad(&DoodadCtx::new(&server, world, doodad));
                });
            }
            _ => phase::init_doodad(&DoodadCtx::new(server, world.clone(), doodad.clone())),
        }
    }
    Some(doodad)
}

/// `Doodad.Spawn()` (`GameObject.Spawn`: AddObject + Show, then the zone change dispatch).
pub fn spawn(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
    let changes = world.spawn(doodad, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
}

/// `Doodad.Delete()`: hide + remove, attached area triggers, the expired source item, and the
/// DB row of a persistent doodad (`SpawnManager.RemovePlayerDoodad`).
pub fn delete(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
    if world.object(doodad.obj_id).is_some_and(
        |current| !matches!(current, WorldObject::Doodad(ref d) if Arc::ptr_eq(d, doodad)),
    ) {
        return;
    }
    {
        let mut d = doodad.data();
        if d.phase.deleted {
            return;
        }
        // Mark as deleted early to avoid re-entry
        d.phase.deleted = true;
        d.phase.func_task = 0; // deviation: cancel the FuncTask (see the module docs)
    }
    super::logic::clear_links(&DoodadCtx::new(server, world.clone(), doodad.clone()));
    world.delete(doodad);
    area_trigger::remove_doodad_area_triggers(server, doodad.obj_id);

    let (item_id, persistent, db_id) = {
        let d = doodad.data();
        (d.item_id, d.is_persistent, d.db_id)
    };
    // Delete associated item if expired
    if item_id > 0 {
        if let Some(h) = server.systems.npcs.hooks.economy.get() {
            h.remove_expired_doodad_item(server, item_id);
        }
    }
    if let Some(spawns) = server.systems.npcs.spawns.get(world.id) {
        spawns.remove_player_doodad(doodad);
    }
    if !persistent {
        return;
    }
    super::db::delete_row(server, db_id);
    doodad.data().is_persistent = false;
}

/// `Doodad.Use(caster, skillId, funcGroupId)` (the skills `InteractionEffect`, interactions and
/// funcs call this).
pub fn use_doodad(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    doodad: &DoodadRef,
    caster: Option<&WorldObject>,
    skill_id: u32,
    func_group_id: i32,
) {
    let ctx = DoodadCtx::new(server, world.clone(), doodad.clone());
    phase::use_doodad(&ctx, caster, skill_id, func_group_id);
}

/// `Doodad.DoChangePhase(caster, nextPhase)` (true = aborted).
pub fn do_change_phase(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    doodad: &DoodadRef,
    caster: Option<&WorldObject>,
    next_phase: i32,
) -> bool {
    let ctx = DoodadCtx::new(server, world.clone(), doodad.clone());
    phase::do_change_phase(&ctx, caster, next_phase)
}

/// `Doodad.OnSkillHit(caster, skillId)` (`Skill` hitting a doodad).
pub fn on_skill_hit(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    doodad: &DoodadRef,
    caster: Option<&WorldObject>,
    skill_id: u32,
) {
    let ctx = DoodadCtx::new(server, world.clone(), doodad.clone());
    phase::on_skill_hit(&ctx, caster, skill_id);
}

/// `Doodad.InitDoodad()` (synchronous).
pub fn init_doodad(server: &GameServer, world: &Arc<WorldInstance>, doodad: &DoodadRef) {
    phase::init_doodad(&DoodadCtx::new(server, world.clone(), doodad.clone()));
}

/// The `FuncGroupId` setter.
pub fn set_func_group_id(server: &GameServer, doodad: &DoodadRef, value: u32) {
    phase::set_func_group_id(server, doodad, value);
}

/// `Doodad.Save()` (awaited; [`super::db::save`] is the fire-and-forget form).
pub async fn save_doodad(server: &GameServer, doodad: &DoodadRef) -> sqlx::Result<()> {
    super::db::save_now(server, doodad).await
}

/// `Doodad.DoDespawn()` (`Spawner.DoDespawn(this)`).
pub fn do_despawn(server: &GameServer, doodad: &DoodadRef) {
    match server.systems.npcs.spawns.doodad_spawner_of(doodad.obj_id) {
        Some(s) => s.do_despawn(server, doodad),
        None => tracing::error!("DoDespawn: doodad {} has no spawner", doodad.obj_id),
    }
}

/// `CreatePlayerDoodad(character, id, x, y, z, zRot, scale, itemId, farmGroupKind,
/// itemTemplateId, customData, ignoreHouses)`: creates, initialises, spawns and saves a player
/// doodad.
#[allow(clippy::too_many_arguments)]
pub fn create_player_doodad(
    server: &GameServer,
    player: &PlayerRef,
    id: u32,
    pos: (f32, f32, f32),
    z_rot: f32,
    scale: f32,
    item_id: u64,
    farm_type: u8,
    item_template_id: u32,
    custom_data: i32,
    ignore_houses: bool,
) -> Option<DoodadRef> {
    create_player_doodad_with_labor(
        server,
        player,
        id,
        pos,
        z_rot,
        scale,
        item_id,
        farm_type,
        item_template_id,
        custom_data,
        ignore_houses,
        0,
    )
}

#[allow(clippy::too_many_arguments)]
fn create_player_doodad_with_labor(
    server: &GameServer,
    player: &PlayerRef,
    id: u32,
    pos: (f32, f32, f32),
    z_rot: f32,
    scale: f32,
    item_id: u64,
    farm_type: u8,
    item_template_id: u32,
    custom_data: i32,
    ignore_houses: bool,
    labor_cost: i16,
) -> Option<DoodadRef> {
    let (x, y, z) = pos;
    let pobj = WorldObject::from(player);
    let (char_id, char_name, zone_id) = {
        let c = player.data();
        (c.id, c.unit.name.clone(), c.unit.transform.zone_id)
    };
    tracing::warn!("{char_name} is placing a doodad {id} at position {x} {y} {z}");
    let world = server.world.world_of(&pobj)?;
    let economy = server.systems.npcs.hooks.economy.get();

    let target_house = if ignore_houses {
        None
    } else {
        crate::systems::housing::house_at_location_in_world(server, world.id, x, y)
    };
    if target_house
        .as_ref()
        .is_some_and(|house| !crate::systems::housing::allowed_to_interact(server, house, player))
    {
        player.send(error_msg(error_message::NO_PERM));
        return None;
    }

    // C#: Create returns null for an unknown template (NullReferenceException).
    let doodad = create(server, &world, 0, id, Some(&pobj), true)?;
    {
        let mut d = doodad.data();
        // Persist only after the placement item has been successfully consumed.
        // Transform = character.Transform.CloneDetached(doodad)
        d.transform.instance_id = world.id;
        d.transform.zone_id = zone_id;
        d.transform.set_position(x, y, z, 0.0, 0.0, z_rot);
        d.item_id = item_id;
        d.plant_time = chrono::Utc::now().timestamp();
        d.farm_type = farm_type;
        d.item_template_id = item_template_id;
    }
    doodad.data().data = custom_data;
    match &target_house {
        Some(house) => {
            let house_id = house.data().id;
            let house_object = WorldObject::from(house);
            let parent = world.world_pos_rot(&house_object);
            let scale = house.data().unit.scale;
            let q = parent.to_quaternion();
            let inverse = crate::models::transform::Quaternion {
                x: -q.x,
                y: -q.y,
                z: -q.z,
                w: q.w,
            };
            let local = inverse.rotate([x - parent.x, y - parent.y, z - parent.z]);
            let rotation =
                crate::models::transform::PositionAndRotation::new(0.0, 0.0, 0.0, 0.0, 0.0, z_rot);
            let (roll, pitch, yaw) =
                crate::models::transform::PositionAndRotation::rotation_from_quaternion(
                    inverse * rotation.to_quaternion(),
                );
            {
                let mut d = doodad.data();
                // Placement packets supply world coordinates; attach_child keeps local
                // coordinates, so inverse-compose before parenting (also saved for restart).
                d.transform.set_position(
                    local[0] / scale,
                    local[1] / scale,
                    local[2] / scale,
                    roll,
                    pitch,
                    yaw,
                );
                d.owner_db_id = house_id;
                d.attach_point = crate::models::unit::attach_point::NONE;
                d.owner_type = doodad_owner_type::HOUSING;
                d.parent_obj_id = house.obj_id;
            }
            world.attach_child(&house_object, &WorldObject::from(&doodad));
        }
        None => doodad.data().owner_db_id = 0,
    }
    if scale > 0.0 {
        doodad.data().scale = scale;
    }

    let items = economy
        .as_ref()
        .map(|h| h.item_ids_from_doodad(server, id))
        .unwrap_or_default();
    let preferred = if item_id > 0 {
        economy
            .as_ref()
            .and_then(|h| h.doodad_source_item(server, player, item_id, true))
    } else {
        None
    };
    if item_id > 0 {
        let Some(preferred) = preferred else {
            tracing::error!(
                "Unable to create doodad because source item (Id: {item_id}) does not exist in {char_name}'s bag inventory."
            );
            delete(server, &world, &doodad);
            server.ids.object.release_id(doodad.obj_id);
            return None;
        };
        if !items.contains(&preferred.template_id) {
            delete(server, &world, &doodad);
            server.ids.object.release_id(doodad.obj_id);
            return None;
        }
        let ucc = economy
            .as_ref()
            .and_then(|h| h.item_template_and_ucc(server, item_id))
            .map_or(0, |(_, ucc)| ucc);
        let mut d = doodad.data();
        d.item_template_id = preferred.template_id;
        d.ucc_id = ucc;
        if preferred.max_count > 1 {
            d.item_id = 0; // stackable: only keep the template id
        }
    }
    let is_coffer = doodad.data().coffer.is_some();
    if let Some(h) = &economy {
        if item_id > 0 && !h.try_consume_doodad_source_item(server, player, id, item_id, labor_cost)
        {
            delete(server, &world, &doodad);
            server.ids.object.release_id(doodad.obj_id);
            return None;
        }
        if is_coffer {
            h.initialize_coffer(server, &doodad, char_id);
        }
    }

    doodad.data().is_persistent = true;
    spawn(server, &world, &doodad);
    init_doodad(server, &world, &doodad);
    super::db::save(server, &doodad);
    if !doodad.data().phase.deleted {
        if let Some(spawns) = server.systems.npcs.spawns.get(world.id) {
            spawns.add_player_doodad(doodad.clone());
        }
    }
    Some(doodad)
}

/// `CSCreateDoodadPacket.Read` body.
pub fn handle_create_doodad(server: &GameServer, player: &PlayerRef, p: &CSCreateDoodadPacket) {
    tracing::warn!(
        "CreateDoodad, Id: {}, X: {}, Y: {}, Z: {}, zRot: {}  ItemId: {}",
        p.id,
        p.x,
        p.y,
        p.z,
        p.z_rot,
        p.item_id
    );
    let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
        return;
    };
    let pos = (p.x, p.y, p.z);
    let economy = server.systems.npcs.hooks.economy.get();

    if ![p.x, p.y, p.z, p.z_rot, p.scale]
        .iter()
        .all(|v| v.is_finite())
        || !(0.0..=10.0).contains(&p.scale)
    {
        return;
    }

    // Verify actual item
    let Some(item) = economy
        .as_ref()
        .and_then(|h| h.doodad_source_item(server, player, p.item_id, true))
    else {
        tracing::warn!("CreateDoodad, provided an invalid ItemId: {}", p.item_id);
        return;
    };
    if !economy.as_ref().is_some_and(|h| {
        h.item_ids_from_doodad(server, p.id)
            .contains(&item.template_id)
    }) {
        return;
    }
    let Some(template) = server.game_data.systems.npcs.doodad.template(p.id) else {
        return;
    };
    let player_pos = world.world_pos_rot(&WorldObject::from(player));
    let range = server
        .game_data
        .systems
        .skills
        .skill(item.use_skill_id)
        .map_or(10.0, |skill| skill.max_range.max(10) as f32);
    let distance_squared =
        (p.x - player_pos.x).powi(2) + (p.y - player_pos.y).powi(2) + (p.z - player_pos.z).powi(2);
    if distance_squared > range * range {
        return;
    }
    let target_zone = world.template.zone_id_at(p.x, p.y);
    if template.restrict_zone_id != 0 && template.restrict_zone_id != target_zone {
        return;
    }
    // Get cost from related skill
    let mut labor_cost = server.systems.npcs.hooks.skills.get().map_or(0, |h| {
        h.skill_consume_labor_power(server, item.use_skill_id)
    });

    let in_public_farm = public_farm::in_public_farm(server, &world.template, pos);
    let farm_type = if in_public_farm {
        public_farm::get_farm_type(server, &world, pos)
    } else {
        public_farm::farm_group_kind::INVALID
    };
    // Validate public farm
    if farm_type != public_farm::farm_group_kind::INVALID {
        if !public_farm::can_place(server, player, farm_type, p.id) {
            tracing::warn!(
                "CreateDoodad, ItemId: {}, Invalid FarmGroupKind: {farm_type}",
                p.item_id
            );
            return;
        }
        labor_cost = 0;
    }

    // Labor cost can be skipped by planting on "owned" land (also blocks if not allowed)
    if let Some(house) =
        crate::systems::housing::house_at_location_in_world(server, world.id, p.x, p.y)
    {
        if !crate::systems::housing::allowed_to_interact(server, &house, player) {
            player.send(error_msg(error_message::NO_PERM));
            return;
        }
        labor_cost = 0;
    }

    // Check labor cost
    if i32::from(player.data().labor_power) < labor_cost {
        player.send(error_msg(error_message::LABOR_POWER_NEEDED));
        return;
    }
    let Ok(labor_cost) = i16::try_from(labor_cost) else {
        return;
    };
    create_player_doodad_with_labor(
        server, player, p.id, pos, p.z_rot, p.scale, p.item_id, farm_type, 0, 0, false, labor_cost,
    );
}

/// `DoodadManager.ChangeDoodadData(player, doodad, data)`.
pub fn change_doodad_data(
    server: &GameServer,
    world: &WorldInstance,
    player: &PlayerRef,
    doodad: &DoodadRef,
    data: i32,
) -> bool {
    let (char_id, family, expedition) = {
        let c = player.data();
        (c.id, c.family, c.unit.expedition_id)
    };
    let (owner_id, is_coffer) = {
        let d = doodad.data();
        (d.owner_id, d.coffer.is_some())
    };
    // TODO (C#): Can non-coffer doodads that use this packet only be changed by their owner ?
    if owner_id != char_id {
        return false;
    }
    if is_coffer {
        if !matches!(
            data,
            housing_permission::PRIVATE
                | housing_permission::FAMILY
                | housing_permission::GUILD
                | housing_permission::PUBLIC
        ) {
            return false;
        }
        if data == housing_permission::FAMILY && family == 0 {
            player.send(error_msg(error_message::FAMILY_NOT_EXIST));
            return false;
        }
        if data == housing_permission::GUILD && expedition == 0 {
            player.send(error_msg(error_message::ONLY_EXPEDITION_MEMBER));
            return false;
        }
    }
    phase::set_data(server, doodad, data);
    let data = doodad.data().data;
    crate::world::broadcast(
        world,
        doodad,
        &SCDoodadChangedPacket {
            obj_id: doodad.obj_id,
            data,
        },
        false,
    );
    true
}

/// `CSUnbondDoodadPacket.Read` body.
pub fn unbond_doodad(
    server: &GameServer,
    player: &PlayerRef,
    character_obj_id: u32,
    doodad_obj_id: u32,
) {
    let (char_id, bonding) = {
        let c = player.data();
        (c.id, c.bonding)
    };
    if player.obj_id != character_obj_id || bonding.is_none_or(|b| b.owner_obj_id != doodad_obj_id)
    {
        return;
    }
    let pobj = WorldObject::from(player);
    let world = server.world.world_of(&pobj);
    if let Some(doodad) = world.as_ref().and_then(|w| w.doodad(doodad_obj_id)) {
        // free up the place where we were sitting
        doodad.data().seat.unload_passenger(char_id, doodad_obj_id);
    }
    let had_parent = {
        let mut c = player.data();
        c.bonding = None;
        c.unit.transform.parent_obj_id != 0
    };
    if let Some(world) = &world {
        // UnLoadPassenger: StickyParent = null (which also clears the parent).
        world.set_sticky_parent(&pobj, None);
        if had_parent {
            world.detach_child(&pobj);
        }
        crate::world::broadcast(
            world,
            &pobj,
            &SCUnbondDoodadPacket {
                character_obj_id: player.obj_id,
                character_id: char_id,
                doodad_obj_id,
            },
            true,
        );
    }
}

/// The doodad part of `CSLootOpenBagPacket.TryHandleFuncDrivenLoot` for a func-driven loot
/// doodad without `DoodadFuncRecoverItem` (the loot container / `RecoverItem` checks are the
/// caller's): `doodad.Use(character, 0)`, then a `DoodadDeleteTask` when the phase is no longer
/// loot-driven.
pub fn loot_open_bag_doodad(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    player: &PlayerRef,
    doodad: &DoodadRef,
) {
    use_doodad(
        server,
        world,
        doodad,
        Some(&WorldObject::from(player)),
        0,
        0,
    );
    if !doodad.data().has_loot_item() {
        tasks::schedule_delete(server, doodad);
    }
}

/// `TimeManager.OnTimeOfDayChange(newTime, oldTime)`, doodad part: runs the ToD triggers of
/// every doodad, including phase entry functions and replacement of the previous timer.
pub fn on_time_of_day_change(server: &GameServer, new_time: f32, old_time: f32) {
    let mut old_time = old_time;
    if old_time > new_time {
        old_time -= 24.0;
    }
    // Only check if it changed at least to the next 6 seconds
    if (new_time * 600.0).floor() as i32 == (old_time * 600.0).floor() as i32 {
        return;
    }
    for world in server.world.worlds() {
        // NPC idle postures (`SCUnitModelPostureChangedPacket` when the posture of the time of
        // day changes).
        for npc in world.all_npcs() {
            let template_id = npc.data().unit.template_id;
            let Some(t) = server.game_data.systems.npcs.npc.template(template_id) else {
                continue;
            };
            if t.npc_posture_sets.len() <= 1 {
                continue;
            }
            let pick = |tod: f32| {
                t.npc_posture_sets
                    .iter()
                    .find(|p| p.start_tod_time <= tod)
                    .map_or(0, |p| p.anim_action_id)
            };
            let (old_anim, new_anim) = (pick(old_time), pick(new_time));
            if old_anim != new_anim {
                let p = {
                    let mut n = npc.data();
                    // `AnimActionId` is a getter evaluated with the current game time.
                    n.anim_action_id = new_anim;
                    crate::systems::npcs::npc::packets::SCUnitModelPostureChangedPacket {
                        obj_id: npc.obj_id,
                        posture: if new_anim > 0 {
                            crate::models::unit::ModelPostureType::ActorModelState
                        } else {
                            crate::models::unit::ModelPostureType::None
                        },
                        has_loot_left: n.unit.has_loot_left,
                        anim_action_id: new_anim,
                        activate_animation: true,
                    }
                };
                crate::world::broadcast(&world, &npc, &p, false);
            }
        }
        for doodad in world.all_doodads() {
            let triggers = doodad.data().phase.tod_triggers.clone();
            for (tod, next_phase) in triggers {
                let crossed = if old_time < 0.0 {
                    tod > old_time + 24.0 || tod <= new_time
                } else {
                    tod > old_time && tod <= new_time
                };
                if crossed && next_phase > 0 {
                    tasks::start_func_task(
                        server,
                        &doodad,
                        Some(&WorldObject::from(&doodad)),
                        tasks::FuncTaskKind::Tod { next_phase },
                        std::time::Duration::ZERO,
                    );
                    break;
                }
            }
        }
    }
}

/// Starts the ToD poller: every 10 s (the `TimeManager` tick) the doodad part of
/// `OnTimeOfDayChange` with the previous game time.
pub fn start_tod_watch(server: &Arc<GameServer>) {
    let server = Arc::downgrade(server);
    let Ok(rt) = tokio::runtime::Handle::try_current() else {
        return;
    };
    rt.spawn(async move {
        let mut last = match server.upgrade() {
            Some(s) => s.time.get_time(),
            None => return,
        };
        let mut iv = tokio::time::interval(std::time::Duration::from_secs(10));
        loop {
            iv.tick().await;
            let Some(s) = server.upgrade() else {
                return;
            };
            let now = s.time.get_time();
            on_time_of_day_change(&s, now, last);
            last = now;
        }
    });
}
