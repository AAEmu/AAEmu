//! The slave side of the ship physics (`systems::ai::physics`): `world.Physics.AddShip` /
//! `RemoveShip`, the helm request and driver seat, `world.Water`, and the
//! [`ShipPhysicsHooks`] the physics thread calls (harpoon lines / tension / lifetime, the
//! `Throttle` / `Steering` written back after a move).

use crate::server::GameServer;
use crate::systems::ai::physics::harpoon::HarpoonLine;
use crate::systems::ai::physics::ship::ShipSpec;
use crate::systems::ai::physics::{self, ShipPhysicsHooks, SlaveKind};
use crate::systems::skills::controllers::Vec3;
use crate::world::{SlaveRef, WorldInstance, WorldObject};

use super::harpoon;

/// `world.Physics.AddShip(slave)`.
pub fn add_ship(server: &GameServer, slave: &SlaveRef) {
    let Some(srv) = server.self_arc() else {
        tracing::debug!("AddShip {}: no bound server", slave.obj_id);
        return;
    };
    let obj = WorldObject::from(slave);
    let Some(world) = super::world_of(server, &obj) else {
        return;
    };
    let (slave_id, template_id, scale, spawn_time_ms) = {
        let d = slave.data();
        (d.id, d.unit.template_id, d.unit.scale, d.spawn_time_ms)
    };
    let Some(template) = server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)
    else {
        return;
    };
    let p = world.world_pos_rot(&obj);
    let spec = ShipSpec {
        obj_id: slave.obj_id,
        slave_id,
        model_id: template.model_id,
        kind: SlaveKind(template.slave_kind as u8),
        scale,
        spawn_time_ms,
        portal_time: template.portal_time,
        position: Vec3::new(p.x, p.y, p.z),
        yaw: p.rot_z,
    };
    physics::add_ship(&srv, world.id, spec);
}

/// `world.Physics.RemoveShip(slave)`.
pub fn remove_ship(server: &GameServer, slave: &SlaveRef) {
    physics::remove_ship(server, slave.obj_id);
}

/// `ShipRequestMoveType`: `ThrottleRequest` / `SteeringRequest` handed to the physics.
pub fn set_request(server: &GameServer, slave: &SlaveRef, throttle: i8, steering: i8) {
    physics::set_ship_request(server, slave.obj_id, throttle, steering);
}

/// `AttachedCharacters.ContainsKey(AttachPointKind.Driver)` changed.
pub fn set_driver(server: &GameServer, slave: &SlaveRef, has_driver: bool) {
    physics::set_ship_driver(server, slave.obj_id, has_driver);
}

/// `world.Water.GetWaterSurface(point, out _)`.
pub fn water_surface(server: &GameServer, world: &WorldInstance, pos: [f32; 3]) -> f32 {
    physics::water_surface(server, world.id, Vec3::new(pos[0], pos[1], pos[2]))
        .map_or(world.template.ocean_level, |(h, _)| h)
}

/// `world.IsWater(point)`.
pub fn is_water(server: &GameServer, world: &WorldInstance, pos: [f32; 3]) -> bool {
    physics::is_water(server, world.id, Vec3::new(pos[0], pos[1], pos[2])).is_some()
}

/// The vehicles subsystem's [`ShipPhysicsHooks`] (`server.systems.hooks.ship_physics`).
pub struct VehicleShipPhysics;

fn hull(server: &GameServer, obj_id: u32) -> Option<(std::sync::Arc<WorldInstance>, SlaveRef)> {
    let obj = server.world.object(obj_id)?;
    let slave = obj.as_slave()?.clone();
    let world = super::world_of(server, &obj)?;
    Some((world, slave))
}

impl ShipPhysicsHooks for VehicleShipPhysics {
    fn harpoon_lines(&self, server: &GameServer, hull_obj_id: u32) -> Vec<HarpoonLine> {
        let Some((world, hull)) = hull(server, hull_obj_id) else {
            return Vec::new();
        };
        harpoon::harpoon_lines(&world, &hull)
    }

    fn harpoon_recoil(&self, server: &GameServer, hull_obj_id: u32, dt: f32) -> (f32, f32) {
        let Some((_, hull)) = hull(server, hull_obj_id) else {
            return (0.0, 0.0);
        };
        let [x, y] = harpoon::tick_tension_tear_and_get_hull_recoil_delta_v(server, &hull, dt);
        (x, y)
    }

    fn harpoon_lifetime_tick(&self, server: &GameServer, hull_obj_id: u32) {
        if let Some((_, hull)) = hull(server, hull_obj_id) {
            harpoon::tick_harpoon_rope_controller_lifetime(server, &hull);
        }
    }

    fn ship_moved(&self, server: &GameServer, obj_id: u32) {
        // `Slave.Throttle` / `Steering` (the smoothed physics values `UseSlaveBase` reads).
        let Some(state) = physics::ship_state(server, obj_id) else {
            return;
        };
        if let Some(slave) = server
            .world
            .object(obj_id)
            .and_then(|o| o.as_slave().cloned())
        {
            let mut d = slave.data();
            d.throttle = state.throttle;
            d.steering = state.steering;
        }
    }
}

/// `StormShipLogic.RegularCannonModelId` (`SlaveTemplate.ModelId` of a regular ship cannon).
pub const REGULAR_CANNON_MODEL_ID: u32 = 117;

/// `StormShipLogic.TryBlockMountOrSlaveSkill(world, skillId, casterSlave)`: with the realistic
/// sea weather model, a regular cannon (not a harpoon skill) on a ship inside a storm cannot
/// fire. `true` = blocked (`SkillResult.NoPerm`).
pub fn storm_blocks_slave_skill(server: &GameServer, slave: &SlaveRef, skill_id: u32) -> bool {
    use crate::systems::ai::physics::forces::STORM_BUFF_ID;
    if server.config.world.sea_weather_model != crate::config::SeaWeatherModelType::Realistic {
        return false;
    }
    // Harpoon is also a ship-mounted weapon/slot; storm rules shouldn't block it.
    if skill_id == harpoon::SHIP_LAUNCH_HARPOON_SKILL_ID
        || skill_id == harpoon::SHIP_CUT_HARPOON_ROPE_SKILL_ID
    {
        return false;
    }
    let (template_id, owner_type, owner_obj_id) = {
        let d = slave.data();
        (d.unit.template_id, d.owner_type, d.owner_obj_id)
    };
    let is_cannon = server
        .game_data
        .systems
        .vehicles
        .slave_template(template_id)
        .is_some_and(|t| t.model_id == REGULAR_CANNON_MODEL_ID);
    if !is_cannon {
        return false;
    }
    // Cannon skills are cast by the cannon slave; its parent ship is stored as owner obj id.
    if owner_type != crate::models::unit::BaseUnitType::Slave || owner_obj_id == 0 {
        return false;
    }
    let Some(world) = super::world_of(server, &WorldObject::from(slave)) else {
        return false;
    };
    let Some(parent) = world.slave(owner_obj_id) else {
        return false;
    };
    let in_storm = parent.data().unit.buffs.check_buff(STORM_BUFF_ID);
    in_storm
}
