//! `Physics/**` + `Core/Managers/World/PhysicsManager`: the ship physics.
//!
//! The C# runs a Jitter2 world per world instance; only ship hulls (slaves with a
//! `ship_models` row) are simulated, and the ship code overrides most of the engine every tick
//! (helm-driven velocities, manual depenetration against hulls / doodads / cliffs / coastal
//! barriers). No Rust engine reproduces Jitter2's behaviour, so the parts it uses are ported in
//! [`jitter`]; the rest is a straight port of the ship modules.
//!
//! | module | C# |
//! |---|---|
//! | [`jitter`] | the Jitter2 subset: `RigidBody`, `World.Step`, `HeightMaps/*` terrain collider |
//! | [`ship`] | `ShipController`, the physics members of `Slave`, `ReplicationSmoothing`, `BoatPhysicsTick` |
//! | [`shore`] | `ShipShoreInteraction`, `Slave.Tick*HullDamage` |
//! | [`contacts`] | `ShipShipInteraction`, `ShipStaticObstacleContact`, `ShipDoodadInteraction`, `ShipCliffInteraction` |
//! | [`barriers`] | `ShipStaticBarrier*`, `ShipStaticBarrierBaiIngestor` (`GeoDataMode`) |
//! | [`harpoon`] | `ShipHarpoonTowPhysics` |
//! | [`forces`] | `Buoyancy`, `WhirlpoolShipPull`, `StormShipLogic`, `OneShotVelocityKick` |
//! | [`replication`] | `PhysicsManager.SendUpdatedMovementData` + the visual bank / pitch |
//! | [`water`], [`objects_file`] | `WaterBodies`, `WaterBodyArea`, `Models/CryEngine/Objects` (`object.dat` water volumes) |
//! | [`manager`] | `PhysicsManager` (per-instance thread and loop) |
//!
//! Integration with the slaves subsystem: it calls [`add_ship`] / [`remove_ship`] (C#
//! `world.Physics.AddShip` / `RemoveShip`), [`set_ship_request`] (helm `ShipRequestMoveType`),
//! [`set_ship_driver`] (driver seat) and reads [`ship_state`] (`Throttle`, `Steering`, `Speed`
//! for `ShipMoveType.UseSlaveBase`). It may install [`ShipPhysicsHooks`]
//! (`server.systems.hooks.ship_physics`) for the harpoon rope state and to be told about moves.
//! `ShipTuningDebug` (hard-wired off) and `HarpoonMechanicsDebug` logging are not ported.

pub mod barriers;
pub mod contacts;
pub mod forces;
pub mod harpoon;
pub mod jitter;
pub mod manager;
pub mod objects_file;
pub mod replication;
pub mod ship;
pub mod shore;
pub mod water;

#[cfg(test)]
pub(crate) mod tests;

use std::sync::Arc;

use dashmap::DashMap;

use crate::server::GameServer;
use crate::systems::ai::geodata::manager::BaiRef;
use crate::systems::npcs::heightmap::{HeightmapWorlds, CELL_HMAP_RESOLUTION};
use crate::systems::skills::controllers::Vec3;
use crate::world::{WorldInstance, WorldObject};

use contacts::{DoodadObstacle, HullDamage};
use harpoon::HarpoonLine;
use jitter::TerrainSamples;
use manager::{Pending, PhysConfig, ShipSnapshot, TickWorld, WorldPhysics};
use ship::{PhysShip, ShipGameView, ShipSpec};
use water::WaterBodies;

/// `SlaveKind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub struct SlaveKind(pub u8);

impl SlaveKind {
    pub const BIG_SAILING_SHIP: Self = Self(1);
    pub const SMALL_SAILING_SHIP: Self = Self(2);
    pub const SPEEDBOAT: Self = Self(3);
    pub const BOAT: Self = Self(4);
    pub const TANK: Self = Self(5);
    pub const MACHINE: Self = Self(6);
    pub const SIEGE_WEAPON: Self = Self(7);
    pub const SLAVE_EQUIPMENT: Self = Self(8);
    pub const FISHBOAT: Self = Self(9);
    pub const MERCHANT_SHIP: Self = Self(10);
    pub const LEVIATHAN: Self = Self(11);

    /// `SlaveTemplate.IsABoat()`.
    pub fn is_boat(self) -> bool {
        matches!(
            self,
            Self::BOAT
                | Self::FISHBOAT
                | Self::SPEEDBOAT
                | Self::MERCHANT_SHIP
                | Self::BIG_SAILING_SHIP
                | Self::SMALL_SAILING_SHIP
        )
    }
}

/// `WindModelType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum WindModel {
    #[default]
    Official,
    Realistic,
}

/// Heights and water as the ship code reads them (`WorldInstance.GetHeight`,
/// `WorldInstance.Water`).
pub trait PhysEnv {
    /// `ParentWorld.GetHeight(x, y)` (game axes).
    fn height(&self, x: f32, y: f32) -> f32;
    /// `Water.GetWaterSurface(point, out flow)`.
    fn water_surface(&self, p: Vec3) -> (f32, Vec3);
    /// `IsWater(point, out flow)`: `Some(flow)` in water.
    fn is_water(&self, p: Vec3) -> Option<Vec3>;
    /// `Template.OceanLevel`.
    fn ocean_level(&self) -> f32;
}

/// The slaves subsystem's side of the ship physics (`server.systems.hooks.ship_physics`).
pub trait ShipPhysicsHooks: Send + Sync {
    /// The harpoon lines of the hull's attached slaves (`HarpoonRope` of each cannon,
    /// depth-first over `AttachedSlaves`, `direct` for the hull's own children).
    fn harpoon_lines(&self, _server: &GameServer, _hull_obj_id: u32) -> Vec<HarpoonLine> {
        Vec::new()
    }
    /// `ShipHarpoonRopeController.TickTensionTearAndGetHullRecoilDeltaV(slave, dt)` (game X, Y).
    fn harpoon_recoil(&self, _server: &GameServer, _hull_obj_id: u32, _dt: f32) -> (f32, f32) {
        (0.0, 0.0)
    }
    /// `ShipHarpoonRopeController.TickHarpoonRopeControllerLifetime(slave)`.
    fn harpoon_lifetime_tick(&self, _server: &GameServer, _hull_obj_id: u32) {}
    /// Called after the physics moved a ship (transform written, movement broadcast).
    fn ship_moved(&self, _server: &GameServer, _obj_id: u32) {}
}

/// `server.systems.ai.physics`.
#[derive(Debug, Default)]
pub struct PhysicsSystem {
    /// `WorldInstance.Physics` (by instance id; created with the first ship).
    worlds: DashMap<u32, Arc<WorldPhysics>>,
    /// `WorldInstance.Water` (by template id).
    water: DashMap<u32, Arc<WaterBodies>>,
    /// Tests: no physics threads (ticks are driven by [`run_world_tick`]).
    manual_ticks: std::sync::atomic::AtomicBool,
}

impl PhysicsSystem {
    /// The physics of a world instance.
    pub fn world(&self, instance_id: u32) -> Option<Arc<WorldPhysics>> {
        self.worlds.get(&instance_id).map(|w| w.clone())
    }

    /// Tests: do not start physics threads.
    pub fn set_manual_ticks(&self, v: bool) {
        self.manual_ticks
            .store(v, std::sync::atomic::Ordering::SeqCst);
    }

    fn world_of_ship(&self, obj_id: u32) -> Option<Arc<WorldPhysics>> {
        self.worlds
            .iter()
            .find(|w| w.has_ship(obj_id))
            .map(|w| w.clone())
    }
}

/// The water of a world template (created with the template's ocean level).
pub fn water_of(server: &GameServer, template_id: u32, ocean_level: f32) -> Arc<WaterBodies> {
    server
        .systems
        .ai
        .physics
        .water
        .entry(template_id)
        .or_insert_with(|| Arc::new(WaterBodies::new(ocean_level)))
        .clone()
}

/// Ingests the water volumes of the cells loaded so far and returns the world's water.
fn synced_water(server: &GameServer, world: &WorldInstance) -> Arc<WaterBodies> {
    let w = water_of(server, world.template.id, world.template.ocean_level);
    if let Some(hm) = server.systems.npcs.heightmaps.get() {
        if let Some(t) = hm.template(world.template.id) {
            w.sync_loaded_cells(
                t,
                hm.files(),
                &world.template.name,
                world.template.ocean_level,
            );
        }
    }
    w
}

/// `WorldInstance.IsWater(point, out flow)`.
pub fn is_water(server: &GameServer, instance_id: u32, p: Vec3) -> Option<Vec3> {
    let world = server.world.world(instance_id)?;
    synced_water(server, &world).is_water(p)
}

/// `WorldInstance.Water.GetWaterSurface(point, out flow)`.
pub fn water_surface(server: &GameServer, instance_id: u32, p: Vec3) -> Option<(f32, Vec3)> {
    let world = server.world.world(instance_id)?;
    Some(synced_water(server, &world).get_water_surface(p))
}

fn config(server: &GameServer) -> PhysConfig {
    let w = &server.config.world;
    PhysConfig {
        wind: if w.wind_model == crate::config::WindModelType::Realistic {
            WindModel::Realistic
        } else {
            WindModel::Official
        },
        realistic_sea_weather: w.sea_weather_model == crate::config::SeaWeatherModelType::Realistic,
        geo_data_mode: w.geo_data_mode,
        game_time: server.time.get_time(),
    }
}

/// `PhysicsManager.AddShip(slave)`: `false` (and the C# error log) when the model has no ship
/// mass. Starts the instance's physics thread on first use.
pub fn add_ship(server: &Arc<GameServer>, instance_id: u32, spec: ShipSpec) -> bool {
    let Some(model) = server
        .game_data
        .systems
        .ai
        .ai
        .ship_model(spec.model_id)
        .copied()
    else {
        tracing::error!("Invalid ship model for slave {}", spec.obj_id);
        return false;
    };
    if model.mass <= 0.0 {
        tracing::error!("Invalid ship model for slave {}", spec.obj_id);
        return false;
    }
    let wp = server
        .systems
        .ai
        .physics
        .worlds
        .entry(instance_id)
        .or_insert_with(|| Arc::new(WorldPhysics::new(instance_id)))
        .clone();
    let ship = PhysShip::new(spec, model, ShipGameView::default());
    // the initial transform sync (rotation becomes yaw-only)
    if let Some(world) = server.world.world(instance_id) {
        if let Some(obj) = world.slave(ship.spec.obj_id).map(WorldObject::from) {
            obj.with_transform(|t| {
                t.set_position(
                    ship.pos.x, ship.pos.y, ship.pos.z, ship.rot.0, ship.rot.1, ship.rot.2,
                )
            });
        }
    }
    tracing::debug!("AddShip {} -> instance {instance_id}", ship.spec.obj_id);
    wp.enqueue(Pending::Add(Box::new(ship)));
    if !server
        .systems
        .ai
        .physics
        .manual_ticks
        .load(std::sync::atomic::Ordering::SeqCst)
    {
        wp.start_thread(
            Arc::downgrade(server),
            server.config.world.target_physics_tps,
        );
    }
    true
}

/// `PhysicsManager.RemoveShip(slave)` (processed by the physics thread).
pub fn remove_ship(server: &GameServer, obj_id: u32) {
    if let Some(wp) = server.systems.ai.physics.world_of_ship(obj_id) {
        tracing::debug!("RemoveShip {obj_id} <- instance {}", wp.instance_id);
        wp.enqueue(Pending::Remove(obj_id));
    }
}

/// The helm input (`ThrottleRequest` / `SteeringRequest`, `ShipRequestMoveType`).
pub fn set_ship_request(server: &GameServer, obj_id: u32, throttle: i8, steering: i8) {
    if let Some(wp) = server.systems.ai.physics.world_of_ship(obj_id) {
        wp.enqueue(Pending::Request {
            obj_id,
            throttle,
            steering,
        });
    }
}

/// The driver seat (`AttachedCharacters.ContainsKey(AttachPointKind.Driver)`).
pub fn set_ship_driver(server: &GameServer, obj_id: u32, has_driver: bool) {
    if let Some(wp) = server.systems.ai.physics.world_of_ship(obj_id) {
        wp.enqueue(Pending::Driver { obj_id, has_driver });
    }
}

/// The ship's physics state after the last tick.
pub fn ship_state(server: &GameServer, obj_id: u32) -> Option<ShipSnapshot> {
    server
        .systems
        .ai
        .physics
        .worlds
        .iter()
        .find_map(|w| w.snapshot(obj_id))
}

/// `ShipSiegeAoEHit.PivotQuerySlackMeters`.
pub const SIEGE_PIVOT_QUERY_SLACK_METERS: f32 = 100.0;

/// `ShipSiegeAoEHit.TrySiegePointHitsShipMassBoxXz(x, y, radius, ship)`: the siege circle
/// touches the hull's mass box (XZ) — `false` for a slave without ship physics or not a boat.
pub fn siege_point_hits_ship(
    server: &GameServer,
    obj_id: u32,
    x: f32,
    y: f32,
    radius: f32,
) -> bool {
    let Some(s) = ship_state(server, obj_id) else {
        return false;
    };
    siege_point_hits_mass_box(&s, x, y, radius)
}

/// The geometry of [`siege_point_hits_ship`].
pub fn siege_point_hits_mass_box(s: &ShipSnapshot, x: f32, y: f32, radius: f32) -> bool {
    if !s.is_boat {
        return false;
    }
    let (cx, cz) = s.mass_box_center;
    let bow = s.bow;
    let (bx, bz) = (bow.cos(), bow.sin());
    let (sx, sz) = (-bow.sin(), bow.cos());
    let (dx, dz) = (x - cx, y - cz);
    let u = (dx * bx + dz * bz).clamp(-s.half_length, s.half_length);
    let v = (dx * sx + dz * sz).clamp(-s.half_beam, s.half_beam);
    let qx = cx + u * bx + v * sx;
    let qz = cz + u * bz + v * sz;
    let (ddx, ddz) = (x - qx, y - qz);
    ddx * ddx + ddz * ddz <= radius * radius
}

/// The heightmap samples of a world template (`Heightmap.RawHeights` built from the loaded cells).
struct HeightmapTerrain<'a> {
    hm: &'a crate::systems::npcs::heightmap::WorldHeightmap,
}

impl TerrainSamples for HeightmapTerrain<'_> {
    fn size(&self) -> (i32, i32) {
        (
            self.hm.cell_x * CELL_HMAP_RESOLUTION,
            self.hm.cell_y * CELL_HMAP_RESOLUTION,
        )
    }

    fn sample(&self, sx: i32, sz: i32) -> f32 {
        if sx < 0 || sz < 0 {
            return 0.0;
        }
        let (cx, cy) = (sx / CELL_HMAP_RESOLUTION, sz / CELL_HMAP_RESOLUTION);
        self.hm
            .get_height_map_data_in_cell(
                cx,
                cy,
                sx % CELL_HMAP_RESOLUTION,
                sz % CELL_HMAP_RESOLUTION,
            )
            .unwrap_or(0.0)
    }
}

/// [`TickWorld`] on the live server.
struct ServerTickWorld<'a> {
    server: &'a GameServer,
    world: Arc<WorldInstance>,
    heightmaps: Option<Arc<HeightmapWorlds>>,
    water: Arc<WaterBodies>,
    hooks: Option<Arc<dyn ShipPhysicsHooks>>,
    terrain: Option<HeightmapTerrain<'a>>,
}

impl PhysEnv for ServerTickWorld<'_> {
    fn height(&self, x: f32, y: f32) -> f32 {
        match &self.heightmaps {
            Some(h) => h
                .world_height(self.world.template.id, x, y)
                .unwrap_or_else(|e| {
                    tracing::error!("physics GetHeight({x}, {y}): {e}");
                    0.0
                }),
            None => 0.0,
        }
    }

    fn water_surface(&self, p: Vec3) -> (f32, Vec3) {
        self.water.get_water_surface(p)
    }

    fn is_water(&self, p: Vec3) -> Option<Vec3> {
        self.water.is_water(p)
    }

    fn ocean_level(&self) -> f32 {
        self.world.template.ocean_level
    }
}

fn unit_attr(obj: &WorldObject, base: f64, attr: crate::models::unit::UnitAttribute) -> f32 {
    let now = crate::world::now_ms();
    obj.with_unit(|u| u.unit().calculate_with_bonuses(base, attr, now) as f32)
        .unwrap_or(base as f32)
}

impl TickWorld for ServerTickWorld<'_> {
    fn ship_view(&self, obj_id: u32) -> Option<ShipGameView> {
        use crate::models::unit::UnitAttribute as A;
        let obj = WorldObject::from(self.world.slave(obj_id)?);
        let t = obj.transform();
        let (whirlpool_buff, storm_buff) = obj.with_base_unit(|b| {
            (
                b.buffs().check_buff(forces::whirlpool::BUFF_ID),
                b.buffs().check_buff(forces::STORM_BUFF_ID),
            )
        });
        Some(ShipGameView {
            world_id: t.world_id,
            instance_id: t.instance_id,
            zone_id: t.zone_id,
            in_region: obj.region().is_some(),
            move_speed_mul: unit_attr(&obj, 1000.0, A::MoveSpeedMul) / 1000.0,
            turn_speed: unit_attr(&obj, 100.0, A::TurnSpeed) / 100.0,
            whirlpool_buff,
            storm_buff,
        })
    }

    fn harpoon_lines(&self, obj_id: u32) -> Vec<HarpoonLine> {
        match &self.hooks {
            Some(h) => h.harpoon_lines(self.server, obj_id),
            None => Vec::new(),
        }
    }

    fn harpoon_recoil(&self, obj_id: u32, dt: f32) -> (f32, f32) {
        match &self.hooks {
            Some(h) => h.harpoon_recoil(self.server, obj_id, dt),
            None => (0.0, 0.0),
        }
    }

    fn harpoon_lifetime(&self, obj_id: u32) {
        if let Some(h) = &self.hooks {
            h.harpoon_lifetime_tick(self.server, obj_id);
        }
    }

    fn doodads_around(&self, obj_id: u32, radius: f32) -> Vec<DoodadObstacle> {
        let Some(slave) = self.world.slave(obj_id) else {
            return Vec::new();
        };
        let obj = WorldObject::from(slave);
        let data = &self.server.game_data.systems.npcs.doodad;
        crate::systems::ai::npc::around(self.server, &obj, radius, false)
            .into_iter()
            .filter_map(|o| {
                let d = o.as_doodad()?.clone();
                let pos = self.world.world_pos_rot(&o);
                let (template_id, scale, instance) = {
                    let dd = d.data();
                    (dd.template_id, dd.scale, dd.transform.instance_id)
                };
                let t = data.template(template_id);
                Some(DoodadObstacle {
                    pos: Vec3::new(pos.x, pos.y, pos.z),
                    rot_z: pos.rot_z,
                    scale,
                    sim_radius: t.map_or(0, |t| t.sim_radius),
                    collide_ship: t.is_some_and(|t| t.collide_ship),
                    no_collision: t.is_some_and(|t| t.no_collision),
                    has_template: t.is_some(),
                    same_world: instance == self.world.id,
                })
            })
            .collect()
    }

    fn whirlpools(&self) -> Vec<(f32, f32)> {
        self.world
            .all_doodads()
            .into_iter()
            .filter(|d| {
                d.is_visible() && d.data().template_id == forces::whirlpool::DOODAD_TEMPLATE_ID
            })
            .map(|d| {
                let p = self.world.world_pos_rot(&WorldObject::from(d));
                (p.x, p.y)
            })
            .collect()
    }

    fn any_whirlpool_buff(&self) -> bool {
        self.world
            .all_of_kind(crate::world::ObjectKind::Slave)
            .iter()
            .any(|o| o.with_base_unit(|b| b.buffs().check_buff(forces::whirlpool::BUFF_ID)))
    }

    fn terrain(&self) -> Option<&dyn TerrainSamples> {
        self.terrain.as_ref().map(|t| t as &dyn TerrainSamples)
    }

    fn bai_cell(&self, cell_x: i32, cell_y: i32) -> Option<[[Option<BaiRef>; 4]; 4]> {
        let t = &self.world.template;
        if cell_x < 0 || cell_y < 0 || cell_x >= t.cell_x || cell_y >= t.cell_y {
            return None;
        }
        // cell.VerifyCellLoaded()
        if let Some(h) = &self.heightmaps {
            if let Some(hm) = h.template(t.id) {
                let _ = hm.get_raw_height_map_height(h.files(), cell_x * 1024, cell_y * 1024);
            }
        }
        let geo = self.server.systems.ai.geodata.get(t.id)?;
        geo.cell_bai(cell_x, cell_y).cloned()
    }
}

/// One physics loop iteration of an instance (the thread body; tests call it directly):
/// the tick under the state lock, then the game-side effects.
pub fn run_world_tick(server: &GameServer, wp: &WorldPhysics, dt_ms: i64) {
    let Some(world) = server.world.world(wp.instance_id) else {
        return;
    };
    let heightmaps = server.systems.npcs.heightmaps.get();
    let water = synced_water(server, &world);
    let hooks = server.systems.hooks.ship_physics.get();
    let out = {
        let terrain = heightmaps
            .as_ref()
            .and_then(|h| h.template(world.template.id))
            .map(|hm| HeightmapTerrain { hm });
        let tw = ServerTickWorld {
            server,
            world: world.clone(),
            heightmaps: heightmaps.clone(),
            water,
            hooks: hooks.clone(),
            terrain,
        };
        let res = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            wp.tick(&tw, dt_ms, crate::world::now_ms(), config(server))
        }));
        match res {
            Ok(o) => o,
            Err(_) => {
                tracing::error!("PhysicsThread Error in instance {}", wp.instance_id);
                return;
            }
        }
    };
    apply_effects(server, &world, out, hooks.as_deref());
}

fn apply_effects(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    out: manager::TickOutput,
    hooks: Option<&dyn ShipPhysicsHooks>,
) {
    for id in out.ignite_lamps {
        ignite_ship_lamps(server, world, id);
    }
    for d in out.damage {
        apply_hull_damage(server, world, d);
    }
    for m in out.movements {
        let Some(slave) = world.slave(m.obj_id) else {
            continue;
        };
        let obj = WorldObject::from(slave);
        obj.with_transform(|t| {
            t.set_position(
                m.position.x,
                m.position.y,
                m.position.z,
                m.rotation.0,
                m.rotation.1,
                m.rotation.2,
            )
        });
        crate::systems::ai::npc::check_moved_position(server, &obj);
        crate::systems::skills::broadcast(
            server,
            &obj,
            &crate::packets::world::SCOneUnitMovementPacket {
                obj_id: m.obj_id,
                move_type: m.move_type,
            },
            false,
        );
        if let Some(h) = hooks {
            h.ship_moved(server, m.obj_id);
        }
    }
}

/// `Slave.ApplyShipHullCollisionDamage(attacker, percent)` /
/// `ApplyFloorCollisionDamageImmediate(percent)`: `percent` of `MaxHp`, then
/// `SCEnvDamagePacket(Collision, objId, dealt)`.
fn apply_hull_damage(server: &GameServer, world: &WorldInstance, d: HullDamage) {
    use crate::systems::skills::types::KillReason;
    if d.percent <= 0 {
        return;
    }
    let Some(target) = world.unit(d.target) else {
        return;
    };
    let Some((hp, max_hp)) = target.with_unit(|u| (u.unit().hp, u.unit().max_hp)) else {
        return;
    };
    if hp <= 0 {
        return;
    }
    let damage = max_hp * d.percent / 100;
    if damage <= 0 {
        return;
    }
    let attacker = world.unit(d.attacker).unwrap_or_else(|| target.clone());
    crate::systems::skills::combat::reduce_current_hp(
        server,
        &target,
        Some(&attacker),
        damage,
        KillReason::Damage,
    );
    let new_hp = target.with_unit(|u| u.unit().hp).unwrap_or(hp);
    let dealt = hp - new_hp;
    if dealt <= 0 {
        return;
    }
    crate::systems::skills::broadcast(
        server,
        &target,
        &crate::systems::skills::packets::SCEnvDamagePacket {
            source: crate::systems::skills::packets::env_source::COLLISION,
            target: d.target,
            amount: dealt as u32,
            gimmick_id: 0,
        },
        true,
    );
}

/// `StormShipLogic.IgniteShipLamps(ship)`: the `LampFront` / `LampRear` doodads of the ship move
/// to their "lit" normal phase.
fn ignite_ship_lamps(server: &GameServer, world: &Arc<WorldInstance>, ship_obj_id: u32) {
    const LAMP_FRONT: u8 = 28;
    const LAMP_REAR: u8 = 29;
    let Some(slave) = world.slave(ship_obj_id) else {
        return;
    };
    let doodads = slave.data().attached_doodads.clone();
    let data = &server.game_data.systems.npcs.doodad;
    for id in doodads {
        let Some(d) = world.doodad(id) else {
            continue;
        };
        let (attach, template_id, current) = {
            let dd = d.data();
            (dd.attach_point, dd.template_id, dd.func_group_id)
        };
        if attach != LAMP_FRONT && attach != LAMP_REAR {
            continue;
        }
        let Some(t) = data.template(template_id) else {
            continue;
        };
        let target = pick_storm_lit_func_group(&t.func_groups);
        if target > 0 && current != target {
            let ctx =
                crate::systems::npcs::doodad::runtime::DoodadCtx::new(server, world.clone(), d);
            crate::systems::npcs::doodad::phase::do_change_phase(&ctx, None, target as i32);
        }
    }
}

/// `PickStormLitFuncGroupId`: the highest-id normal group whose model looks "_on" (and not
/// "_off"), else the highest normal group id.
pub fn pick_storm_lit_func_group(
    groups: &[crate::systems::npcs::doodad::templates::DoodadFuncGroups],
) -> u32 {
    use crate::systems::npcs::doodad::templates::doodad_func_group_kind::NORMAL;
    let mut normal: Vec<&crate::systems::npcs::doodad::templates::DoodadFuncGroups> = groups
        .iter()
        .filter(|g| g.group_kind_id == NORMAL)
        .collect();
    if normal.is_empty() {
        return 0;
    }
    normal.sort_by_key(|g| std::cmp::Reverse(g.id));
    for g in &normal {
        let m = g.model.as_deref().unwrap_or("").to_ascii_lowercase();
        if !m.is_empty() && m.contains("_on") && !m.contains("_off") {
            return g.id;
        }
    }
    normal.iter().map(|g| g.id).max().unwrap_or(0)
}
