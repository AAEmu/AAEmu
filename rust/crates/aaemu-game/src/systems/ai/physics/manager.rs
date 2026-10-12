//! `Core/Managers/World/PhysicsManager`: one physics world per world instance with its own
//! thread (`TargetPhysicsTps`, default 25) running the ship loop.
//!
//! Locking: the simulation state is only locked by the physics thread (and [`WorldPhysics::tick`]
//! in tests). Everything other code calls goes through the pending queue (add / remove / helm
//! input / driver), processed at the start of the next tick like the C# `_pendingActions`, or
//! reads the per-tick snapshots. The game-side effects of a tick (transforms, movement packets,
//! hull damage, lamp ignition) are applied after the state lock is released.

use std::collections::{HashMap, HashSet};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, Weak};
use std::time::{Duration, Instant};

use dashmap::DashMap;

use crate::systems::ai::geodata::manager::BaiRef;
use crate::systems::ai::numerics::Vec3Ext;
use crate::systems::skills::controllers::Vec3;

use super::barriers::BarrierZones;
use super::contacts::{self, DoodadObstacle, HullDamage};
use super::forces::{self, Storm, Whirlpool};
use super::harpoon::{self, HarpoonLine};
use super::jitter::{PhysWorld, TerrainSamples};
use super::replication::{self, MovementUpdate};
use super::ship::{self, PhysShip, ShipGameView};
use super::shore;
use super::{PhysEnv, WindModel};

/// What a tick reads from the game world besides heights / water ([`PhysEnv`]).
pub trait TickWorld: PhysEnv {
    /// The game-side view of a ship (`None` = its object is gone).
    fn ship_view(&self, obj_id: u32) -> Option<ShipGameView>;
    /// The harpoon lines of the hull's attached slaves.
    fn harpoon_lines(&self, obj_id: u32) -> Vec<HarpoonLine>;
    /// `ShipHarpoonRopeController.TickTensionTearAndGetHullRecoilDeltaV(slave, dt)` (game X, Y).
    fn harpoon_recoil(&self, obj_id: u32, dt: f32) -> (f32, f32);
    /// `ShipHarpoonRopeController.TickHarpoonRopeControllerLifetime(slave)`.
    fn harpoon_lifetime(&self, obj_id: u32);
    /// The doodads within `radius` of the ship (`WorldManager.GetAround<Doodad>`).
    fn doodads_around(&self, obj_id: u32, radius: f32) -> Vec<DoodadObstacle>;
    /// The XY of the visible whirlpool doodads (template 3086) of the world.
    fn whirlpools(&self) -> Vec<(f32, f32)>;
    /// Any slave of the world has the whirlpool buff.
    fn any_whirlpool_buff(&self) -> bool;
    /// The terrain collider samples (`None` = no heightmap).
    fn terrain(&self) -> Option<&dyn TerrainSamples>;
    /// Loads a cell of the collision world (CryPhysics ships).
    fn load_collision_cell(&self, _cell: (i32, i32)) {}
    /// Adds the loaded water volumes to the collision world as buoyancy areas.
    fn sync_collision_water(&self) {}
    /// `WorldCell.VerifyCellLoaded()` + the cell's 4x4 `BaiLoader` (`GeoDataMode`).
    fn bai_cell(&self, cell_x: i32, cell_y: i32) -> Option<[[Option<BaiRef>; 4]; 4]>;
    /// The instance's collision world (CryPhysics ships, [`super::cry`]); `None` = Jitter2.
    fn cry_world(&self) -> Option<Arc<crate::systems::ai::collision::world::PhysWorld>> {
        None
    }
}

/// The settings a tick uses.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct PhysConfig {
    pub wind: WindModel,
    /// `SeaWeatherModel == Realistic` (`StormShipLogic` on).
    pub realistic_sea_weather: bool,
    /// `GeoDataMode` (static barriers on).
    pub geo_data_mode: bool,
    pub game_time: f32,
    /// `Physics.CryPhysicsShips`: hulls on the CryPhysics port when the collision world exists.
    pub cryphysics: bool,
}

/// A queued call (`_pendingActions`).
#[derive(Debug)]
pub enum Pending {
    Add(Box<PhysShip>),
    Remove(u32),
    /// `ThrottleRequest` / `SteeringRequest` from the helm (`ShipRequestMoveType`).
    Request {
        obj_id: u32,
        throttle: i8,
        steering: i8,
    },
    /// A driver sat down / left (`AttachedCharacters` Driver slot).
    Driver {
        obj_id: u32,
        has_driver: bool,
    },
}

/// What other code can read about a ship (refreshed every tick).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ShipSnapshot {
    pub obj_id: u32,
    pub throttle: i8,
    pub steering: i8,
    pub throttle_request: i8,
    pub steering_request: i8,
    pub speed: f32,
    pub rot_speed: f32,
    /// Hull position (game axes) and transform rotation.
    pub position: Vec3,
    pub rotation: (f32, f32, f32),
    /// Rigid body velocity (game axes).
    pub velocity: Vec3,
    pub grounded: bool,
    /// Mass-box centre (game X, Y), bow heading (rad, game XY plane), half length / beam (scaled).
    pub mass_box_center: (f32, f32),
    pub bow: f32,
    pub half_length: f32,
    pub half_beam: f32,
    pub is_boat: bool,
}

/// The game-side effects of a tick.
#[derive(Debug, Default)]
pub struct TickOutput {
    pub damage: Vec<HullDamage>,
    pub movements: Vec<MovementUpdate>,
    pub ignite_lamps: Vec<u32>,
}

/// The simulation (only the physics thread holds it).
#[derive(Debug, Default)]
pub struct PhysState {
    pub world: PhysWorld,
    /// `_bodies` (insertion order).
    pub ships: Vec<PhysShip>,
    kicks: Vec<(u32, Vec3)>,
    whirlpool: Whirlpool,
    storm: Storm,
    pub barriers: BarrierZones,
    loop_index: u64,
    cache_stamp: HashMap<u32, (u64, (f32, f32))>,
    /// The CryPhysics hulls ([`super::cry`]).
    pub cry: super::cry::CryShips,
}

impl PhysState {
    pub fn apply(&mut self, p: Pending) {
        match p {
            Pending::Add(ship) => {
                let id = ship.spec.obj_id;
                self.ships.retain(|s| s.spec.obj_id != id);
                self.cache_stamp.remove(&id);
                self.ships.push(*ship);
            }
            Pending::Remove(id) => {
                self.ships.retain(|s| s.spec.obj_id != id);
                self.cache_stamp.remove(&id);
                self.kicks.retain(|k| k.0 != id);
            }
            Pending::Request {
                obj_id,
                throttle,
                steering,
            } => {
                if let Some(s) = self.ships.iter_mut().find(|s| s.spec.obj_id == obj_id) {
                    s.throttle_request = throttle;
                    s.steering_request = steering;
                }
            }
            Pending::Driver { obj_id, has_driver } => {
                if let Some(s) = self.ships.iter_mut().find(|s| s.spec.obj_id == obj_id) {
                    s.has_driver = has_driver;
                }
            }
        }
    }

    /// One iteration of `PhysicsThread` after the pending actions.
    pub fn tick(
        &mut self,
        w: &dyn TickWorld,
        instance_id: u32,
        dt_ms: i64,
        now_ms: i64,
        cfg: PhysConfig,
    ) -> TickOutput {
        let mut out = TickOutput::default();
        // The thread survives the last dismissal. Keep one tick to remove CryPhysics
        // entities, then stop querying/stepping an otherwise idle collision world.
        if self.ships.is_empty() && self.cry.ents.is_empty() {
            return out;
        }
        self.loop_index += 1;
        let dt = (dt_ms as f64 / 1000.0) as f32;

        // the game-side state of every ship (the C# reads it live off the Slave)
        let mut exists = Vec::with_capacity(self.ships.len());
        for s in &mut self.ships {
            match w.ship_view(s.spec.obj_id) {
                Some(v) => {
                    s.view = v;
                    s.harpoon_lines = w.harpoon_lines(s.spec.obj_id);
                    exists.push(true);
                }
                None => exists.push(false),
            }
        }

        // CryPhysics hulls: the engine floats, integrates and collides them
        let cry_pw = if cfg.cryphysics { w.cry_world() } else { None };
        let cry = cry_pw.is_some();

        // 3. Step the physics world (PreStep generators, then integration)
        if dt > 0.0 {
            if !cry {
                for s in &mut self.ships {
                    forces::buoyancy(s, w, now_ms);
                }
            }
            {
                let mut all: Vec<&mut PhysShip> = self.ships.iter_mut().collect();
                let any = w.any_whirlpool_buff();
                let pools = if any { w.whirlpools() } else { Vec::new() };
                self.whirlpool
                    .pre_step(&mut all, &pools, any, dt, &mut out.damage);
                if cfg.realistic_sea_weather {
                    let in_storm: HashSet<u32> = all
                        .iter()
                        .filter(|s| s.view.storm_buff)
                        .map(|s| s.spec.obj_id)
                        .collect();
                    out.ignite_lamps = self.storm.pre_step(&all, &in_storm);
                }
            }
            for (id, dv) in self.kicks.drain(..) {
                if let Some(s) = self.ships.iter_mut().find(|s| s.spec.obj_id == id) {
                    s.body.velocity += dv;
                }
            }
            if let Some(pw) = &cry_pw {
                // load the cells under the ships first (static colliders, terrain)
                for s in &self.ships {
                    let (x, y) = (s.pos.x, s.pos.y);
                    if let Some(c) = pw.cell_of(x, y) {
                        if !pw.touch_cell(c) {
                            w.load_collision_cell(c);
                        }
                    }
                }
                w.sync_collision_water();
                let mut st = pw.lock();
                self.cry.push(&mut st, &self.ships);
                st.world.step(dt);
                self.cry.pull(&st, &mut self.ships);
                out.damage
                    .extend(self.cry.collision_damage(&st, &self.ships, now_ms));
            } else {
                let mut bodies: Vec<&mut super::jitter::RigidBody> =
                    self.ships.iter_mut().map(|s| &mut s.body).collect();
                self.world.step(&mut bodies, dt, w.terrain());
            }
        }

        // 4. per-ship sync and helm
        let gravity = self.world.gravity;
        let mut this_tick: Vec<usize> = Vec::new();
        let mut lifetime: Vec<u32> = Vec::new();
        for (i, s) in self.ships.iter_mut().enumerate() {
            if !exists[i] || s.view.world_id != instance_id || s.summoning(now_ms) {
                continue;
            }
            lifetime.push(s.spec.obj_id);
            if !s.body.active {
                continue;
            }
            let xy = (s.pos.x, s.pos.y);
            let refresh = match self.cache_stamp.get(&s.spec.obj_id) {
                Some((lp, sxy)) => {
                    let moved = (xy.0 - sxy.0) * (xy.0 - sxy.0) + (xy.1 - sxy.1) * (xy.1 - sxy.1);
                    self.loop_index - lp >= 2 || moved > 1.0
                }
                None => true,
            };
            if refresh {
                s.create_water_and_land_surface_cache(w);
                self.cache_stamp
                    .insert(s.spec.obj_id, (self.loop_index, xy));
            }
            s.sync_transform_with_rigid_body();
            s.boat_physics_tick();
            let (rx, ry) = w.harpoon_recoil(s.spec.obj_id, dt);
            if rx * rx + ry * ry > 1e-8 {
                self.kicks.push((s.spec.obj_id, Vec3::new(rx, 0.0, ry)));
            }
            let terrain_hits = if cry {
                0
            } else {
                shore::resolve_terrain_contacts(s, w, dt, gravity)
            };
            for _ in 0..terrain_hits {
                out.damage.push(HullDamage {
                    target: s.spec.obj_id,
                    attacker: s.spec.obj_id,
                    percent: 1,
                });
            }
            // river flow drift (never while grounded)
            let under = Vec3::new(
                s.pos.x,
                s.pos.y,
                s.pos.z + s.model.mass_box_size_z / -2.0 * s.spec.scale,
            );
            if let Some(flow) = w.is_water(under) {
                if flow.length_squared() > 1e-10 && !s.grounded() {
                    s.body.position += Vec3::new(flow.x * dt, flow.z * dt, flow.y * dt);
                }
            }
            this_tick.push(i);
        }
        // rope controller expiry (also for sleeping hulls)
        for id in lifetime {
            w.harpoon_lifetime(id);
        }

        let mut ships: Vec<&mut PhysShip> = {
            let set: HashSet<usize> = this_tick.iter().copied().collect();
            self.ships
                .iter_mut()
                .enumerate()
                .filter(|(i, _)| set.contains(i))
                .map(|(_, s)| s)
                .collect()
        };

        for s in ships.iter_mut() {
            ship::apply_force_and_torque(s, dt, cfg.wind, cfg.game_time);
        }
        if !cry {
            contacts::resolve_all_pairs(&mut ships, dt, &mut out.damage);
        }

        for s in ships.iter_mut() {
            s.static_obstacle_hull_damage_contact_active = false;
        }
        if !ships.is_empty() && !cry {
            for s in ships.iter_mut() {
                let r = contacts::doodad_query_radius(s);
                let doodads = w.doodads_around(s.spec.obj_id, r);
                contacts::resolve_ship_vs_doodads(s, &doodads);
            }
            for s in ships.iter_mut() {
                s.sync_speed_from_bow_velocity();
            }
        }

        if cfg.geo_data_mode && !cry {
            for s in ships.iter() {
                if s.view.instance_id != instance_id {
                    continue;
                }
                let cell = (
                    (s.pos.x / 1024.0).floor() as i32,
                    (s.pos.y / 1024.0).floor() as i32,
                );
                let grid = w.bai_cell(cell.0, cell.1);
                self.barriers.ensure_cell(w, cell, grid.as_ref());
            }
            if !ships.is_empty() && !self.barriers.barriers.is_empty() {
                self.barriers.resolve_all(w, &mut ships, instance_id);
                for s in ships.iter_mut() {
                    s.sync_speed_from_bow_velocity();
                }
            }
        }

        if !ships.is_empty() && !cry {
            for s in ships.iter_mut() {
                if s.view.instance_id == instance_id {
                    contacts::resolve_ship_vs_steep_terrain(s, w);
                }
            }
            for s in ships.iter_mut() {
                s.sync_speed_from_bow_velocity();
            }
        }

        harpoon::apply_ship_pair_tow_impulses(&mut ships, dt);

        for s in ships.iter_mut() {
            for _ in 0..shore::tick_static_obstacle_hull_damage(s, dt) {
                out.damage.push(HullDamage {
                    target: s.spec.obj_id,
                    attacker: s.spec.obj_id,
                    percent: 1,
                });
            }
        }

        for s in ships.iter_mut() {
            out.movements
                .push(replication::send_updated_movement_data(s, w, dt, now_ms));
        }
        out
    }
}

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

/// Tick starts measured after sleeping. Advancing at the pre-sleep timestamp causes
/// alternating back-to-back ticks and integrates the sleep interval twice.
struct TickClock {
    previous: Duration,
    period: Duration,
}

impl TickClock {
    fn wait(&self, now: Duration) -> Duration {
        self.period
            .saturating_sub(now.saturating_sub(self.previous))
    }

    fn advance(&mut self, now: Duration) -> i64 {
        let elapsed = now.as_millis().saturating_sub(self.previous.as_millis()) as i64;
        self.previous = now;
        elapsed
    }
}

/// `WorldInstance.Physics`.
#[derive(Debug)]
pub struct WorldPhysics {
    pub instance_id: u32,
    state: Mutex<PhysState>,
    pending: Mutex<Vec<Pending>>,
    snapshots: DashMap<u32, ShipSnapshot>,
    running: AtomicBool,
}

impl WorldPhysics {
    pub fn new(instance_id: u32) -> Self {
        Self {
            instance_id,
            state: Mutex::new(PhysState::default()),
            pending: Mutex::new(Vec::new()),
            snapshots: DashMap::new(),
            running: AtomicBool::new(false),
        }
    }

    pub fn enqueue(&self, p: Pending) {
        lock(&self.pending).push(p);
    }

    pub fn snapshot(&self, obj_id: u32) -> Option<ShipSnapshot> {
        self.snapshots.get(&obj_id).map(|s| *s)
    }

    pub fn has_ship(&self, obj_id: u32) -> bool {
        self.snapshots.contains_key(&obj_id)
            || lock(&self.pending)
                .iter()
                .any(|p| matches!(p, Pending::Add(s) if s.spec.obj_id == obj_id))
    }

    /// Runs `f` on the simulation state under its lock (GM barrier commands; must not be called
    /// from a hook).
    pub fn with_state<R>(&self, f: impl FnOnce(&mut PhysState) -> R) -> R {
        f(&mut lock(&self.state))
    }

    /// Reads the simulation state (tests / diagnostics; must not be called from a hook).
    pub fn inspect<R>(&self, f: impl FnOnce(&PhysState) -> R) -> R {
        f(&lock(&self.state))
    }

    /// One physics loop iteration: the pending actions, the tick, the snapshots. Returns the
    /// effects to apply to the game world.
    pub fn tick(&self, w: &dyn TickWorld, dt_ms: i64, now_ms: i64, cfg: PhysConfig) -> TickOutput {
        let pending: Vec<Pending> = std::mem::take(&mut *lock(&self.pending));
        let mut st = lock(&self.state);
        for p in pending {
            if let Pending::Remove(id) = &p {
                self.snapshots.remove(id);
            }
            st.apply(p);
        }
        let out = st.tick(w, self.instance_id, dt_ms, now_ms, cfg);
        for s in &st.ships {
            self.snapshots.insert(s.spec.obj_id, snapshot_of(s));
        }
        out
    }

    /// `StartPhysics()`: the physics thread (until the server is dropped).
    pub fn start_thread(self: &Arc<Self>, server: Weak<crate::server::GameServer>, tps: f32) {
        if self.running.swap(true, Ordering::SeqCst) {
            return;
        }
        let me = Arc::downgrade(self);
        let name = format!("Physics-{}", self.instance_id);
        let tps = if tps > 0.0 { tps } else { 25.0 };
        let spawned = std::thread::Builder::new().name(name).spawn(move || {
            let target = Duration::from_secs_f32(1.0 / tps);
            let origin = Instant::now();
            let mut clock = TickClock {
                previous: Duration::ZERO,
                period: target,
            };
            loop {
                let wait = clock.wait(origin.elapsed());
                if !wait.is_zero() {
                    std::thread::sleep(wait);
                }
                let total = clock.advance(origin.elapsed());
                if total > target.as_millis() as i64 * 2 {
                    tracing::warn!(
                        "Physics thread is running slow at {} / {} ms",
                        total,
                        target.as_millis()
                    );
                }
                let (Some(server), Some(wp)) = (server.upgrade(), me.upgrade()) else {
                    return;
                };
                super::run_world_tick(&server, &wp, total);
            }
        });
        if let Err(e) = spawned {
            tracing::error!("failed to start the physics thread: {e}");
            self.running.store(false, Ordering::SeqCst);
        }
    }
}

fn snapshot_of(s: &PhysShip) -> ShipSnapshot {
    let v = s.body.velocity;
    ShipSnapshot {
        obj_id: s.spec.obj_id,
        throttle: s.throttle,
        steering: s.steering,
        throttle_request: s.throttle_request,
        steering_request: s.steering_request,
        speed: s.speed,
        rot_speed: s.rot_speed,
        position: s.pos,
        rotation: s.rot,
        velocity: Vec3::new(v.x, v.z, v.y),
        grounded: s.grounded(),
        mass_box_center: s.mass_box_center_xz(),
        bow: s.bow(),
        half_length: s.model.mass_box_size_y * s.spec.scale * 0.5,
        half_beam: s.model.mass_box_size_x * s.spec.scale * 0.5,
        is_boat: s.spec.kind.is_boat(),
    }
}

#[cfg(test)]
mod clock_tests {
    use super::*;

    #[test]
    fn tick_schedule_counts_sleep_once_and_preserves_wall_time() {
        let mut c = TickClock {
            previous: Duration::ZERO,
            period: Duration::from_millis(40),
        };
        let mut now = Duration::ZERO;
        let mut integrated = 0;
        for _ in 0..25 {
            now += c.wait(now);
            integrated += c.advance(now);
            // Physics work takes 3 ms, leaving 37 ms to the next tick, never 0 ms.
            now += Duration::from_millis(3);
            assert_eq!(c.wait(now), Duration::from_millis(37));
        }
        assert_eq!(integrated, 1000);
        assert_eq!(c.previous, Duration::from_secs(1));
    }

    #[test]
    fn late_ticks_do_not_duplicate_elapsed_time_or_lose_fractional_ms() {
        let mut c = TickClock {
            previous: Duration::ZERO,
            period: Duration::from_millis(40),
        };
        assert_eq!(c.advance(Duration::from_micros(40500)), 40);
        assert_eq!(c.advance(Duration::from_micros(81000)), 41);
        let late = Duration::from_millis(150);
        assert_eq!(c.wait(late), Duration::ZERO);
        assert_eq!(c.advance(late), 69);
        assert_eq!(c.wait(late), Duration::from_millis(40));
    }
}
