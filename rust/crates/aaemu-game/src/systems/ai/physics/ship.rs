//! A ship in the physics world: the physics members of `Slave` (throttle / steering smoothing,
//! speed, ground latch, hull damage accumulators, caches...), `ShipController` (`Build`,
//! `ApplyForceAndTorque`, the wind model, upright stabilisation), `ReplicationSmoothing` and
//! `PhysicsManager.BoatPhysicsTick`.

use std::collections::HashMap;

use crate::systems::ai::data::ShipModel;
use crate::systems::ai::numerics::{deg_to_rad, Quat, Vec3Ext};
use crate::systems::skills::controllers::Vec3;

use super::harpoon::{self, HarpoonLine};
use super::jitter::{quat_rotation_y, yaw_pitch_roll, BoxShape, RigidBody};
use super::{SlaveKind, WindModel};

/// `MathUtil.HalfPi`.
pub const HALF_PI: f32 = std::f32::consts::FRAC_PI_2;

/// `ReplicationSmoothing`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct ReplicationSmoothing {
    pub seeded: bool,
    pub pos_x: f32,
    pub pos_y: f32,
    pub pos_z: f32,
    pub vel_px: f32,
    pub vel_py: f32,
    pub vel_pz: f32,
    pub bank_smoothed: f32,
    pub ground_pitch_smoothed: f32,
    pub contact_hold_ticks: u8,
}

impl ReplicationSmoothing {
    pub fn reset(&mut self) {
        self.seeded = false;
        self.contact_hold_ticks = 0;
    }

    /// `ContactHoldTicks = Math.Max(ContactHoldTicks, ticks)`.
    pub fn hold(&mut self, ticks: u8) {
        self.contact_hold_ticks = self.contact_hold_ticks.max(ticks);
    }
}

/// What the physics needs to add a ship (`PhysicsManager.AddShip(slave)`).
#[derive(Debug, Clone, PartialEq)]
pub struct ShipSpec {
    /// `slave.ObjId`.
    pub obj_id: u32,
    /// `slave.Id` (hull-collision cooldown keys, pair ordering).
    pub slave_id: u32,
    /// `slave.ModelId` (`ship_models`).
    pub model_id: u32,
    /// `slave.Template.SlaveKind`.
    pub kind: SlaveKind,
    /// `slave.Scale`.
    pub scale: f32,
    /// `slave.SpawnTime` (unix ms).
    pub spawn_time_ms: i64,
    /// `slave.Template.PortalTime` (seconds).
    pub portal_time: f32,
    /// World position (game axes) and yaw (`Transform.World.Rotation.Z`).
    pub position: Vec3,
    pub yaw: f32,
}

/// The game-side values a physics tick reads from the `Slave` (gathered before the tick).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ShipGameView {
    /// `Transform.WorldId`.
    pub world_id: u32,
    /// `Transform.InstanceId` (`ParentWorld.Id`).
    pub instance_id: u32,
    pub zone_id: u32,
    /// `slave.Region != null`.
    pub in_region: bool,
    /// `MoveSpeedMul`.
    pub move_speed_mul: f32,
    /// `TurnSpeed` (`CalculateWithBonuses(100, TurnSpeed) / 100`).
    pub turn_speed: f32,
    /// `Buffs.CheckBuff(1918)` (whirlpool).
    pub whirlpool_buff: bool,
    /// `Buffs.CheckBuff(1917)` (storm cloud).
    pub storm_buff: bool,
}

impl Default for ShipGameView {
    fn default() -> Self {
        Self {
            world_id: 0,
            instance_id: 0,
            zone_id: 0,
            in_region: true,
            move_speed_mul: 1.0,
            turn_speed: 1.0,
            whirlpool_buff: false,
            storm_buff: false,
        }
    }
}

/// One ship: the rigid body, the `ShipController` and the physics members of its `Slave`.
#[derive(Debug, Clone)]
pub struct PhysShip {
    pub spec: ShipSpec,
    pub model: ShipModel,
    /// Authored prefab brushes named `buoy*` (shared CGF geometries, local placements).
    pub buoys: Vec<crate::systems::ai::collision::objects::ShapePart>,
    /// Authored default collision geometry bounds in unscaled game/model axes. Native
    /// hull bounds are independent of the inertia-only mass box; None keeps asset-free fallback.
    pub collision_bounds: Option<aaemu_physics::geometry::primitives::BoxPrim>,
    pub body: RigidBody,
    pub replication: ReplicationSmoothing,
    /// The ship's `Transform` (game axes; ships are root objects so local = world). Written to the
    /// game object after the tick.
    pub pos: Vec3,
    /// (roll, pitch, yaw) radians.
    pub rot: (f32, f32, f32),
    pub view: ShipGameView,
    /// Harpoon lines of the hull's attached slaves (refreshed each tick).
    pub harpoon_lines: Vec<HarpoonLine>,
    /// `AttachedCharacters.ContainsKey(Driver)`.
    pub has_driver: bool,

    pub throttle_request: i8,
    pub throttle: i8,
    pub throttle_smoothed: f32,
    pub speed: f32,
    pub last_move_dir_sign: i8,
    pub steering_request: i8,
    pub steering: i8,
    pub steering_smoothed: f32,
    pub rot_speed: f32,
    pub turn_speed_velocity_mul: f32,
    pub bank_angle: f32,
    pub ground_pitch_angle: f32,
    pub grounded_by_stern: bool,
    pub grounded_last_tick: bool,
    pub ground_stuck_time: f32,
    pub ground_escape_assist: f32,
    pub ground_contact_latched: bool,
    pub ground_contact_floor_smoothed: f32,
    pub ground_pitch_front_floor_smoothed: f32,
    pub ground_pitch_back_floor_smoothed: f32,
    pub ground_pitch_floor_smoothing_seeded: bool,
    pub wave_pitch_phase: f32,
    pub ground_contact_floor_smoothing_seeded: bool,
    pub ground_contact_latched_time: f32,
    pub shore_ground_damage_seconds_accumulator: f32,
    pub static_obstacle_hull_damage_contact_active: bool,
    pub static_obstacle_hull_damage_seconds_accumulator: f32,
    pub static_obstacle_hull_damage_no_contact_seconds: f32,
    /// `ShipHullCollisionDamageCooldownByOtherShipId` (by the other `slave.Id`).
    pub hull_collision_cooldowns: HashMap<u32, f32>,
    pub cached_water_flow: Vec3,
    pub cached_water_surface: f32,
    pub cached_floor_level: f32,
}

/// `ShipMassBoxDefaults`.
fn mass_box_size_z(base: f32) -> f32 {
    base.max(0.01)
}

impl PhysShip {
    /// `PhysicsManager.AddShip`: `ShipController.Build` at the slave's position with a yaw-only
    /// orientation, the initial transform sync and the per-ship resets.
    pub fn new(spec: ShipSpec, model: ShipModel, view: ShipGameView) -> Self {
        // JVector(X, Z, Y) / JQuaternion.CreateRotationY(Rotation.Z)
        let pos = Vec3::new(spec.position.x, spec.position.z, spec.position.y);
        let orientation = quat_rotation_y(spec.yaw);
        let size_z = mass_box_size_z(model.mass_box_size_z);
        let shape = BoxShape::new(
            Vec3::new(model.mass_box_size_x, size_z, model.mass_box_size_y),
            Vec3::new(
                model.mass_center_x,
                model.mass_center_z,
                model.mass_center_y,
            ),
        );
        let body = RigidBody::new(shape, model.mass, pos, orientation);
        let mut ship = Self {
            pos: spec.position,
            rot: (0.0, 0.0, spec.yaw),
            spec,
            model,
            buoys: Vec::new(),
            collision_bounds: None,
            body,
            replication: ReplicationSmoothing::default(),
            view,
            harpoon_lines: Vec::new(),
            has_driver: false,
            throttle_request: 0,
            throttle: 0,
            throttle_smoothed: 0.0,
            speed: 0.0,
            last_move_dir_sign: 1,
            steering_request: 0,
            steering: 0,
            steering_smoothed: 0.0,
            rot_speed: 0.0,
            turn_speed_velocity_mul: 1.0,
            bank_angle: 0.0,
            ground_pitch_angle: 0.0,
            grounded_by_stern: false,
            grounded_last_tick: false,
            ground_stuck_time: 0.0,
            ground_escape_assist: 0.0,
            ground_contact_latched: false,
            ground_contact_floor_smoothed: 0.0,
            ground_pitch_front_floor_smoothed: 0.0,
            ground_pitch_back_floor_smoothed: 0.0,
            ground_pitch_floor_smoothing_seeded: false,
            wave_pitch_phase: 0.0,
            ground_contact_floor_smoothing_seeded: false,
            ground_contact_latched_time: 0.0,
            shore_ground_damage_seconds_accumulator: 0.0,
            static_obstacle_hull_damage_contact_active: false,
            static_obstacle_hull_damage_seconds_accumulator: 0.0,
            static_obstacle_hull_damage_no_contact_seconds: 0.0,
            hull_collision_cooldowns: HashMap::new(),
            cached_water_flow: Vec3::new(0.0, 0.0, 0.0),
            cached_water_surface: 0.0,
            cached_floor_level: 0.0,
        };
        ship.sync_transform_with_rigid_body();
        ship.replication.reset();
        ship
    }

    /// `slave.SpawnTime.AddSeconds(Template.PortalTime) > DateTime.UtcNow`.
    pub fn summoning(&self, now_ms: i64) -> bool {
        self.spec.spawn_time_ms + (f64::from(self.spec.portal_time) * 1000.0) as i64 > now_ms
    }

    /// The bow heading in the physics XZ plane (`rpy.Item1 + HalfPi`).
    pub fn bow(&self) -> f32 {
        yaw_pitch_roll(self.body.orientation).0 + HALF_PI
    }

    pub fn grounded(&self) -> bool {
        self.ground_contact_latched || self.cached_floor_level > self.cached_water_surface
    }

    /// `PhysicsManager.SyncTransformWithRigidBody`: `Translate(delta)` then
    /// `ApplyFromQuaternion(X, Z, Y, W)` (Jitter → game axes).
    pub fn sync_transform_with_rigid_body(&mut self) {
        let b = &self.body;
        let dx = b.position.x - self.pos.x;
        let dy = b.position.z - self.pos.y;
        let dz = b.position.y - self.pos.z;
        self.pos = Vec3::new(self.pos.x + dx, self.pos.y + dy, self.pos.z + dz);
        let q = b.orientation;
        self.rot = from_quaternion(q.x, q.z, q.y, q.w);
    }

    /// `Slave.CreateWaterAndLandSurfaceCache()` (heights at the transform position).
    pub fn create_water_and_land_surface_cache(&mut self, env: &dyn super::PhysEnv) {
        self.cached_floor_level = env.height(self.pos.x, self.pos.y);
        let (surface, flow) = env.water_surface(self.pos);
        self.cached_water_surface = surface;
        self.cached_water_flow = flow;
    }

    /// `PhysicsManager.BoatPhysicsTick`: on-land friction, then the helm input smoothing (or the
    /// reset without a driver).
    pub fn boat_physics_tick(&mut self) {
        super::shore::apply_on_land_physics(self);
        if self.has_driver {
            const SMOOTHING_FACTOR: f32 = 0.12;
            self.throttle_smoothed +=
                (f32::from(self.throttle_request) - self.throttle_smoothed) * SMOOTHING_FACTOR;
            self.steering_smoothed +=
                (f32::from(self.steering_request) - self.steering_smoothed) * SMOOTHING_FACTOR;
            self.throttle = round_to_sbyte(self.throttle_smoothed);
            self.steering = round_to_sbyte(self.steering_smoothed);
        } else {
            self.throttle_request = 0;
            self.steering_request = 0;
            self.throttle = 0;
            self.steering = 0;
            self.throttle_smoothed = 0.0;
            self.steering_smoothed = 0.0;
        }
    }

    /// `ShipShipInteraction.GetMassBoxCenterXz`.
    pub fn mass_box_center_xz(&self) -> (f32, f32) {
        let s = self.spec.scale;
        let local = Vec3::new(
            self.model.mass_center_x * s,
            self.model.mass_center_z * s,
            self.model.mass_center_y * s,
        );
        let w = rotate_vector_by_quaternion(local, self.body.orientation);
        (self.body.position.x + w.x, self.body.position.z + w.z)
    }

    /// `ShipShipInteraction.SyncSlaveSpeedFromBowVelocity`.
    pub fn sync_speed_from_bow_velocity(&mut self) {
        let bow = self.bow();
        let along = self.body.velocity.x * bow.cos() + self.body.velocity.z * bow.sin();
        let denom = (self.view.move_speed_mul / 4.0) * self.turn_speed_velocity_mul;
        if denom < 1e-5 {
            return;
        }
        self.speed = along / denom;
    }
}

/// `PositionAndRotation.FromQuaternion(x, y, z, w)` → (roll, pitch, yaw).
pub fn from_quaternion(x: f32, y: f32, z: f32, w: f32) -> (f32, f32, f32) {
    crate::models::transform::PositionAndRotation::rotation_from_quaternion(
        crate::models::transform::Quaternion { x, y, z, w },
    )
}

/// `ShipShipInteraction.RotateVectorByQuaternion`.
pub fn rotate_vector_by_quaternion(v: Vec3, q: Quat) -> Vec3 {
    let (qx, qy, qz, qw) = (q.x, q.y, q.z, q.w);
    let tx = 2.0 * (qy * v.z - qz * v.y);
    let ty = 2.0 * (qz * v.x - qx * v.z);
    let tz = 2.0 * (qx * v.y - qy * v.x);
    Vec3::new(
        v.x + qw * tx + (qy * tz - qz * ty),
        v.y + qw * ty + (qz * tx - qx * tz),
        v.z + qw * tz + (qx * ty - qy * tx),
    )
}

/// `(sbyte)Math.Clamp((int)Math.Round(v), -128, 127)` (banker's rounding).
pub fn round_to_sbyte(v: f32) -> i8 {
    let r = f64::from(v).round_ties_even();
    let i = if r.is_nan() { 0 } else { r as i32 };
    i.clamp(-128, 127) as i8
}

/// `Math.Sign` of a float as an int (`MathF.Sign`).
fn signf(v: f32) -> i32 {
    if v > 0.0 {
        1
    } else if v < 0.0 {
        -1
    } else {
        0
    }
}

fn sign_i(v: i8) -> i32 {
    i32::from(v.signum())
}

/// `ShipController.ShipMotionDefaults`.
pub mod motion {
    pub const GROUND_ESCAPE_MAX_SPEED_ABS: f32 = 1.5;
    pub const GROUND_REVERSE_SPEED_CAP_PERCENT_OF_WATER: f32 = 100.0;
    pub const SHALLOW_WATER_DEPTH_FOR_GROUND_SPEED_CAPS: f32 = 0.35;
    pub const OPPOSING_THROTTLE_ACCEL_MUL: f32 = 1.0;
    pub const OPPOSING_THROTTLE_BRAKE_TUNE_MUL: f32 = 2.4;
    pub const OPEN_WATER_COAST_DRAG_CAP_PER_SEC: f32 = 0.085;
    pub const SHOAL_COAST_DRAG_CAP_PER_SEC: f32 = 0.22;
    pub const STEERING_RESPONSIVENESS_MUL: f32 = 1.45;
    pub const COUNTER_STEER_RESPONSIVENESS_MUL: f32 = 1.35;
    pub const MIN_TURN_FACTOR_AT_ZERO_SPEED: f32 = 0.5;
    pub const TURN_FULL_FACTOR_AT_SPEED: f32 = 2.5;
    pub const TURN_SPEED_SLOWDOWN_FRAC: f32 = 0.1;
    pub const TURN_SPEED_VELOCITY_MUL_RESPONSE: f32 = 5.5;
    pub const LATERAL_VELOCITY_DAMP_PER_SEC: f32 = 5.0;
    pub const UPRIGHT_STABILIZE_MIN_SUBMERGED_METERS: f32 = 0.04;
    pub const UPRIGHT_STABILIZE_MAX_RAD_PER_SEC: f32 = 2.25;
    pub const UPRIGHT_STABILIZE_ANGLE_DEAD_ZONE_RAD: f32 = 0.004;
    pub const WIND_CONE_HALF_ANGLE_DEG: f32 = 15.0;
    pub const WIND_WITH_MAX_MUL: f32 = 1.15;
    pub const WIND_AGAINST_MAX_MUL: f32 = 0.85;
    pub const WATER_VERTICAL_VELOCITY_DAMP_PER_SEC: f32 = 6.0;
}

/// `GetSteerMaxDegFixed(kind)`.
pub fn steer_max_deg_fixed(kind: SlaveKind) -> f32 {
    match kind {
        SlaveKind::BOAT => 4.35,
        SlaveKind::SMALL_SAILING_SHIP => 2.6,
        SlaveKind::BIG_SAILING_SHIP => 2.35,
        SlaveKind::FISHBOAT => 3.35,
        SlaveKind::SPEEDBOAT => 5.85,
        SlaveKind::MERCHANT_SHIP => 2.85,
        _ => 3.1,
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum WindProfile {
    None,
    SquareRig,
    LateenRig,
}

fn wind_profile(kind: SlaveKind) -> WindProfile {
    match kind {
        SlaveKind::BIG_SAILING_SHIP | SlaveKind::SMALL_SAILING_SHIP => WindProfile::SquareRig,
        SlaveKind::MERCHANT_SHIP | SlaveKind::SPEEDBOAT => WindProfile::LateenRig,
        _ => WindProfile::None,
    }
}

/// `GetOpenSeaWindFromGameClockRealistic` (`TimeManager.Get()` = game seconds).
fn open_sea_wind_realistic(game_time: f32) -> (f32, f32) {
    let mut sec = game_time % 86400.0;
    if sec < 0.0 {
        sec += 86400.0;
    }
    let mut hours = sec / 3600.0;
    hours = (hours % 24.0 + 24.0) % 24.0;
    let phase = std::f32::consts::PI * hours / 12.0;
    (phase.sin(), phase.cos())
}

fn open_sea_wind(model: WindModel, game_time: f32) -> (f32, f32) {
    match model {
        WindModel::Official => (0.0, 1.0),
        WindModel::Realistic => open_sea_wind_realistic(game_time),
    }
}

fn wind_dir_normalized(ship: &PhysShip, model: WindModel, game_time: f32) -> (f32, f32) {
    let f = ship.cached_water_flow;
    let len_sq = f.x * f.x + f.y * f.y;
    if len_sq > 1e-8 {
        let inv = 1.0 / len_sq.sqrt();
        return (f.x * inv, f.y * inv);
    }
    open_sea_wind(model, game_time)
}

fn wind_speed_mul_square_rig(dot_move: f32) -> f32 {
    let cos_cone = (motion::WIND_CONE_HALF_ANGLE_DEG * std::f32::consts::PI / 180.0).cos();
    if dot_move >= cos_cone {
        return motion::WIND_WITH_MAX_MUL;
    }
    if dot_move <= -cos_cone {
        return motion::WIND_AGAINST_MAX_MUL;
    }
    1.0 + (motion::WIND_WITH_MAX_MUL - 1.0) * (dot_move / cos_cone)
}

fn wind_speed_mul_lateen_rig(dot_move: f32) -> f32 {
    let a = motion::WIND_CONE_HALF_ANGLE_DEG * std::f32::consts::PI / 180.0;
    let (cos_cone, sin_cone) = (a.cos(), a.sin());
    let p = 1.0 - dot_move.abs();
    if p >= cos_cone {
        return motion::WIND_WITH_MAX_MUL;
    }
    if p <= sin_cone {
        return motion::WIND_AGAINST_MAX_MUL;
    }
    motion::WIND_AGAINST_MAX_MUL
        + (p - sin_cone) / (cos_cone - sin_cone)
            * (motion::WIND_WITH_MAX_MUL - motion::WIND_AGAINST_MAX_MUL)
}

/// `GetWindSpeedMul(slave, bowRad)`.
pub fn wind_speed_mul(ship: &PhysShip, bow_rad: f32, model: WindModel, game_time: f32) -> f32 {
    let profile = wind_profile(ship.spec.kind);
    if profile == WindProfile::None {
        return 1.0;
    }
    let (wx, wy) = wind_dir_normalized(ship, model, game_time);
    let (fx, fz) = (bow_rad.cos(), bow_rad.sin());
    let dot_bow = fx * wx + fz * wy;
    let dot_move = if ship.speed.abs() < 0.01 {
        dot_bow
    } else {
        signf(ship.speed) as f32 * dot_bow
    };
    if model == WindModel::Official {
        if !matches!(
            ship.spec.kind,
            SlaveKind::SMALL_SAILING_SHIP | SlaveKind::BIG_SAILING_SHIP | SlaveKind::FISHBOAT
        ) {
            return 1.0;
        }
        let cos_cone = (motion::WIND_CONE_HALF_ANGLE_DEG * std::f32::consts::PI / 180.0).cos();
        return if dot_bow.abs() >= cos_cone {
            motion::WIND_WITH_MAX_MUL
        } else {
            1.0
        };
    }
    match profile {
        WindProfile::SquareRig => wind_speed_mul_square_rig(dot_move),
        WindProfile::LateenRig => wind_speed_mul_lateen_rig(dot_move),
        WindProfile::None => 1.0,
    }
}

/// `ShipController.ApplyForceAndTorque(slave, deltaTime)`.
pub fn apply_force_and_torque(ship: &mut PhysShip, dt_sec: f32, wind: WindModel, game_time: f32) {
    use motion::*;
    let model = ship.model;
    // If not in water (grounded), keep limited control to let ships get unstuck.
    let is_grounded =
        (ship.cached_floor_level > ship.cached_water_surface) || ship.ground_contact_latched;
    let shallow_depth_for_caps = SHALLOW_WATER_DEPTH_FOR_GROUND_SPEED_CAPS.max(0.0);
    let water_depth = ship.cached_water_surface - ship.cached_floor_level;
    let is_shallow_for_caps = water_depth >= 0.0 && water_depth <= shallow_depth_for_caps;
    let is_grounded_for_speed_caps = is_grounded || is_shallow_for_caps;

    let heading0 = ship.bow();
    let (dir_x0, dir_z0) = (heading0.cos(), heading0.sin());
    let along0 = ship.body.velocity.x * dir_x0 + ship.body.velocity.z * dir_z0;
    let moving_backward0 = along0 < -0.05 || (along0.abs() <= 0.05 && ship.throttle_request < 0);
    let mut is_escape_input_on_ground = false;
    let mut escape_throttle_sign_on_ground = 0;

    if is_grounded_for_speed_caps {
        if !ship.grounded_last_tick {
            ship.grounded_by_stern = moving_backward0;
        }
        let grounded_by_stern = ship.grounded_by_stern;
        let escape_throttle_sign = if grounded_by_stern { 1 } else { -1 };
        escape_throttle_sign_on_ground = escape_throttle_sign;
        is_escape_input_on_ground =
            ship.throttle_request != 0 && sign_i(ship.throttle_request) == escape_throttle_sign;
        let is_stuck_on_ground = ship.speed.abs() < 0.08 && ship.throttle_request != 0;
        if is_stuck_on_ground {
            ship.ground_stuck_time += dt_sec;
        } else {
            ship.ground_stuck_time = 0.0;
        }
        let assist_target = if is_escape_input_on_ground && ship.ground_stuck_time > 1.2 {
            1.0
        } else {
            0.0
        };
        let assist_a = 1.0 - (-4.0 * dt_sec.max(0.0)).exp();
        ship.ground_escape_assist += (assist_target - ship.ground_escape_assist) * assist_a;

        let mut ground_throttle_forward_mul = if grounded_by_stern { 0.78 } else { 0.10 };
        let mut ground_throttle_reverse_mul = 1.0f32;
        if is_escape_input_on_ground {
            let escape_boost = 1.0 + 0.35 * ship.ground_escape_assist;
            if escape_throttle_sign > 0 {
                ground_throttle_forward_mul = 1f32.min(ground_throttle_forward_mul * escape_boost);
            } else {
                ground_throttle_reverse_mul = 1f32.min(ground_throttle_reverse_mul * escape_boost);
            }
        } else if ship.throttle_request != 0 && !is_escape_input_on_ground {
            if ship.throttle_request > 0 {
                ground_throttle_forward_mul *= 0.55;
            } else {
                ground_throttle_reverse_mul *= 0.20;
            }
        }
        let grounded_throttle_input_mul = if ship.throttle_request >= 0 {
            ground_throttle_forward_mul
        } else {
            ground_throttle_reverse_mul
        };
        ship.throttle =
            round_to_sbyte(f32::from(ship.throttle_request) * grounded_throttle_input_mul);
        if is_escape_input_on_ground
            && ship.throttle == 0
            && i32::from(ship.throttle_request).abs() >= 8
        {
            ship.throttle = (escape_throttle_sign * 8) as i8;
        }
        ship.throttle_smoothed = f32::from(ship.throttle);

        let mut ground_steer_input_mul = if grounded_by_stern { 0.9 } else { 0.8f32 };
        ground_steer_input_mul =
            1f32.min(ground_steer_input_mul + 0.15 * ship.ground_escape_assist);
        ship.steering = round_to_sbyte(f32::from(ship.steering_request) * ground_steer_input_mul);
        ship.steering_smoothed = f32::from(ship.steering);
        ship.turn_speed_velocity_mul = 1.0;
    } else {
        ship.grounded_by_stern = false;
        ship.ground_stuck_time = 0.0;
        ship.ground_escape_assist = 0.0;
    }
    ship.grounded_last_tick = is_grounded_for_speed_caps;

    if !is_grounded && !harpoon::has_taut_harpoon_engaged(&ship.harpoon_lines) {
        if ship.throttle > 0 && ship.speed < 1.0 && ship.speed >= 0.0 {
            ship.speed = 1.0;
        }
        if ship.throttle < 0 && ship.speed > -1.0 && ship.speed <= 0.0 {
            ship.speed = -1.0;
        }
    }

    let throttle_norm = f32::from(ship.throttle) * 0.007_874_016;
    let steering_norm = f32::from(ship.steering) * 0.007_874_016;
    let slave_rot_rad = ship.bow();

    let wind_mul = wind_speed_mul(ship, slave_rot_rad, wind, game_time);
    let msm = ship.view.move_speed_mul;
    let mut max_forward = model.velocity * msm / 2.0 * wind_mul;
    let water_max_reverse_abs = model.reverse_velocity * msm / 2.0 * wind_mul;
    let mut max_backward = -water_max_reverse_abs;
    if is_grounded_for_speed_caps {
        let rev_pct = GROUND_REVERSE_SPEED_CAP_PERCENT_OF_WATER.clamp(0.0, 100.0);
        max_backward = -water_max_reverse_abs * (rev_pct / 100.0);
    }
    let is_fallback_escape_on_ground = is_grounded_for_speed_caps
        && !is_escape_input_on_ground
        && ship.throttle_request != 0
        && ship.ground_stuck_time > 0.35;
    if is_grounded_for_speed_caps && (is_escape_input_on_ground || is_fallback_escape_on_ground) {
        let escape_sign = if is_escape_input_on_ground {
            escape_throttle_sign_on_ground
        } else {
            sign_i(ship.throttle_request)
        };
        if escape_sign > 0 {
            max_forward = max_forward.max(GROUND_ESCAPE_MAX_SPEED_ABS);
        } else if escape_sign < 0 {
            max_backward = max_backward.min(-GROUND_ESCAPE_MAX_SPEED_ABS);
        }
    }

    let mut linear_accel = model.accel;
    let is_opposing_throttle =
        throttle_norm != 0.0 && ship.speed != 0.0 && signf(ship.speed) != signf(throttle_norm);
    if is_opposing_throttle {
        let reverse_cap = if model.reverse_accel > 0.0 {
            model.reverse_accel
        } else {
            model.accel
        };
        linear_accel = model.accel.max(reverse_cap)
            * OPPOSING_THROTTLE_ACCEL_MUL
            * OPPOSING_THROTTLE_BRAKE_TUNE_MUL;
    }
    if throttle_norm != 0.0 && !is_opposing_throttle {
        let cap_abs = if throttle_norm > 0.0 {
            max_forward
        } else {
            -max_backward
        };
        if cap_abs > 1e-4 {
            let n = (ship.speed.abs() / cap_abs).clamp(0.0, 1.0);
            let approach_mul = (1.0 - n.powf(2.0)).clamp(0.10, 1.0);
            linear_accel *= approach_mul;
        }
    }

    ship.speed += throttle_norm * (linear_accel * dt_sec) / 2.0;

    if is_grounded_for_speed_caps && is_escape_input_on_ground && ship.ground_stuck_time > 0.35 {
        let target_escape_speed = GROUND_ESCAPE_MAX_SPEED_ABS;
        let escape_a = 1.0 - (-(6.0 + 2.0 * ship.ground_escape_assist) * dt_sec.max(0.0)).exp();
        let target = escape_throttle_sign_on_ground as f32 * target_escape_speed;
        ship.speed += (target - ship.speed) * escape_a;
    }

    let overspeed_a = 1.0 - (-3.5 * dt_sec.max(0.0)).exp();
    if ship.speed > max_forward {
        ship.speed += (max_forward - ship.speed) * overspeed_a;
    } else if ship.speed < max_backward {
        ship.speed += (max_backward - ship.speed) * overspeed_a;
    }

    const MOVE_DIR_EPSILON: f32 = 0.10;
    if ship.speed > MOVE_DIR_EPSILON {
        ship.last_move_dir_sign = 1;
    } else if ship.speed < -MOVE_DIR_EPSILON {
        ship.last_move_dir_sign = -1;
    }

    let speed_abs = ship.speed.abs();
    let speed01 = (speed_abs / TURN_FULL_FACTOR_AT_SPEED).clamp(0.0, 1.0);
    let turn_factor =
        MIN_TURN_FACTOR_AT_ZERO_SPEED + (1.0 - MIN_TURN_FACTOR_AT_ZERO_SPEED) * speed01;

    const REVERSE_STEER_EPSILON: f32 = 0.05;
    let is_moving_backward = ship.speed < -REVERSE_STEER_EPSILON
        || (ship.speed.abs() <= REVERSE_STEER_EPSILON && ship.last_move_dir_sign < 0);
    let effective_steering_norm = if is_moving_backward {
        -steering_norm
    } else {
        steering_norm
    };

    let base_kind_steer_deg = steer_max_deg_fixed(ship.spec.kind);
    let turn_speed_bonus_mul = 0.05f32.max(ship.view.turn_speed);
    let kind_steer_deg = base_kind_steer_deg * turn_speed_bonus_mul;
    let steer_max_deg_normal = 0.05f32.max(kind_steer_deg);
    let steer_max_deg_hard = 0.05f32.max(kind_steer_deg * 2.0);
    let steer_max_normal = deg_to_rad(steer_max_deg_normal * turn_factor);

    let turn_speed = 100.0 * turn_speed_bonus_mul * dt_sec * std::f32::consts::PI;
    let mut rot_delta = effective_steering_norm
        * (turn_speed / 100.0)
        * (model.turn_accel / 360.0)
        * STEERING_RESPONSIVENESS_MUL
        * turn_factor;
    if ship.rot_speed != 0.0
        && effective_steering_norm != 0.0
        && signf(ship.rot_speed) != signf(effective_steering_norm)
    {
        rot_delta *= COUNTER_STEER_RESPONSIVENESS_MUL;
    }
    if effective_steering_norm != 0.0 && steer_max_normal > 1e-6 {
        let same_dir = ship.rot_speed == 0.0 || signf(ship.rot_speed) == signf(rot_delta);
        if same_dir {
            let n = (ship.rot_speed.abs() / steer_max_normal).clamp(0.0, 1.0);
            let approach_mul = (1.0 - n.powf(2.0)).clamp(0.10, 1.0);
            rot_delta *= approach_mul;
        }
    }
    ship.rot_speed += rot_delta;

    let steer_max_hard = deg_to_rad(steer_max_deg_hard * turn_factor);
    ship.rot_speed = ship.rot_speed.clamp(-steer_max_hard, steer_max_hard);

    if ship.steering != 0 {
        let steer_max_normal_rad = deg_to_rad(steer_max_deg_normal * turn_factor);
        let rot_abs = ship.rot_speed.abs();
        if rot_abs > steer_max_normal_rad {
            let cap_a = 1.0 - (-8.0 * dt_sec.max(0.0)).exp();
            let target = signf(ship.rot_speed) as f32 * steer_max_normal_rad;
            ship.rot_speed += (target - ship.rot_speed) * cap_a;
        }
    }

    let steer_max = deg_to_rad(steer_max_deg_normal * turn_factor);
    let steer_max_safe = steer_max.max(1e-5);
    let turn_rate_norm = (ship.rot_speed.abs() / steer_max_safe).clamp(0.0, 1.0);
    let target_turn_vel_mul = 1.0 - TURN_SPEED_SLOWDOWN_FRAC * turn_rate_norm;
    let turn_mul_a = 1.0 - (-TURN_SPEED_VELOCITY_MUL_RESPONSE * dt_sec.max(0.0)).exp();
    ship.turn_speed_velocity_mul +=
        (target_turn_vel_mul - ship.turn_speed_velocity_mul) * turn_mul_a;

    const ANGULAR_DAMPING: f32 = 0.975;
    if ship.steering == 0 {
        ship.rot_speed *= ANGULAR_DAMPING;
    }

    const FLOOR_COLLISION_SPEED_MULTIPLIER: f32 = 0.96;
    if is_grounded_for_speed_caps {
        let ground_damping = if is_escape_input_on_ground {
            1.0
        } else {
            FLOOR_COLLISION_SPEED_MULTIPLIER
        };
        ship.speed *= ground_damping;
        ship.body.velocity *= ground_damping;
    }

    if is_grounded_for_speed_caps
        && ship.throttle_request != 0
        && !is_escape_input_on_ground
        && ship.speed.abs() < 0.12
    {
        ship.speed = 0.0;
    }

    if ship.throttle == 0 {
        let dt = dt_sec;
        let drag = 0f32.max(model.water_resistance);
        let speed_abs_now = ship.speed.abs();
        let low_speed01 = (speed_abs_now / 2.0).clamp(0.0, 1.0);
        let low_speed_curve = low_speed01 * low_speed01;
        const COAST_DRAG_MIN_MUL: f32 = 0.15;
        let coast_drag_max_mul = if is_grounded { 0.45 } else { 0.28 };
        let drag_mul =
            COAST_DRAG_MIN_MUL + (coast_drag_max_mul - COAST_DRAG_MIN_MUL) * low_speed_curve;
        let mut effective_drag = drag * drag_mul;
        if is_grounded {
            effective_drag *= 1.35;
        }
        let max_coast_drag_per_second = if is_grounded {
            0.26
        } else if is_grounded_for_speed_caps {
            SHOAL_COAST_DRAG_CAP_PER_SEC
        } else {
            OPEN_WATER_COAST_DRAG_CAP_PER_SEC
        };
        effective_drag = effective_drag.min(max_coast_drag_per_second);
        let decay = (-effective_drag * dt).exp();
        ship.speed *= decay;
        let stop_epsilon = if is_grounded { 0.04 } else { 0.002 };
        if ship.speed.abs() < stop_epsilon {
            ship.speed = 0.0;
        }
    }

    let force_throttle = ship.speed * msm / 4.0 * ship.turn_speed_velocity_mul;
    let (fx, fz) = (slave_rot_rad.cos(), slave_rot_rad.sin());
    if !is_grounded {
        let mut v = ship.body.velocity;
        let submerged_now = 0f32.max(ship.cached_water_surface - ship.body.position.y);
        if submerged_now > 0.01 {
            v.y *= (-WATER_VERTICAL_VELOCITY_DAMP_PER_SEC * dt_sec.max(0.0)).exp();
            v.y = v.y.clamp(-18.0, 6.0);
        }
        let along_dot = v.x * fx + v.z * fz;
        let perp_x = v.x - along_dot * fx;
        let perp_z = v.z - along_dot * fz;
        let lateral_damp = (-LATERAL_VELOCITY_DAMP_PER_SEC * dt_sec.max(0.0)).exp();
        ship.body.velocity = Vec3::new(
            force_throttle * fx + perp_x * lateral_damp,
            v.y,
            force_throttle * fz + perp_z * lateral_damp,
        );
    } else {
        let v = ship.body.velocity;
        ship.body.velocity = Vec3::new(force_throttle * fx, v.y, force_throttle * fz);
    }

    let speed_to_along_vel = msm / 4.0 * 0.001f32.max(ship.turn_speed_velocity_mul);
    harpoon::apply_terrain_hook_tow(
        ship,
        dt_sec,
        fx,
        fz,
        speed_to_along_vel,
        max_forward,
        max_backward,
    );

    if !is_grounded {
        let submerged = 0f32.max(ship.cached_water_surface - ship.body.position.y);
        if submerged >= UPRIGHT_STABILIZE_MIN_SUBMERGED_METERS {
            apply_water_upright_stabilization(&mut ship.body, dt_sec);
        }
    }

    ship.body.angular_velocity = Vec3::new(0.0, -ship.rot_speed, 0.0);
}

/// `ShipController.ApplyWaterUprightStabilization`.
pub fn apply_water_upright_stabilization(body: &mut RigidBody, dt_sec: f32) {
    if dt_sec <= 0.0 {
        return;
    }
    let q = body.orientation.normalized();
    let up = Vec3::new(0.0, 1.0, 0.0);
    let body_up = q.rotate(up);
    let mut axis = body_up.cross(up);
    let len_sq = axis.length_squared();
    let angle;
    if len_sq < 1e-14 {
        let dot_parallel = body_up.dot(up);
        if dot_parallel > 1.0 - 1e-6 {
            return;
        }
        if dot_parallel < -1.0 + 1e-6 {
            axis = body_up.cross(Vec3::new(1.0, 0.0, 0.0)).normalized();
            if axis.length_squared() < 1e-12 {
                axis = body_up.cross(Vec3::new(0.0, 0.0, 1.0)).normalized();
            }
            angle = std::f32::consts::PI;
        } else {
            return;
        }
    } else {
        axis = axis.normalized();
        let dot = body_up.dot(up).clamp(-1.0, 1.0);
        angle = dot.acos();
    }
    if angle < motion::UPRIGHT_STABILIZE_ANGLE_DEAD_ZONE_RAD {
        return;
    }
    let max_step = motion::UPRIGHT_STABILIZE_MAX_RAD_PER_SEC * dt_sec;
    let step = angle.min(max_step);
    let delta_q = Quat::from_axis_angle(axis, step);
    body.orientation = delta_q.multiply(q).normalized();
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    pub fn model() -> ShipModel {
        ShipModel {
            id: 1,
            velocity: 12.0,
            mass: 20000.0,
            mass_center_x: 0.0,
            mass_center_y: 0.0,
            mass_center_z: 0.0,
            mass_box_size_x: 4.0,
            mass_box_size_y: 14.0,
            mass_box_size_z: 3.0,
            water_resistance: 0.5,
            accel: 4.0,
            reverse_accel: 3.0,
            reverse_velocity: 6.0,
            turn_accel: 90.0,
            ..ShipModel::default()
        }
    }

    pub fn spec(obj_id: u32, x: f32, y: f32, z: f32, yaw: f32) -> ShipSpec {
        ShipSpec {
            obj_id,
            slave_id: obj_id + 1000,
            model_id: 1,
            kind: SlaveKind::BOAT,
            scale: 1.0,
            spawn_time_ms: 0,
            portal_time: 0.0,
            position: Vec3::new(x, y, z),
            yaw,
        }
    }

    #[test]
    fn build_maps_game_axes_to_jitter_axes() {
        let s = PhysShip::new(
            spec(1, 100.0, 200.0, 99.0, 0.5),
            model(),
            ShipGameView::default(),
        );
        assert_eq!(s.body.position, Vec3::new(100.0, 99.0, 200.0));
        assert!((s.rot.2 - 0.5).abs() < 1e-6 && s.rot.0.abs() < 1e-6 && s.rot.1.abs() < 1e-6);
        assert_eq!(s.pos, Vec3::new(100.0, 200.0, 99.0));
        assert!((s.body.mass() - 20000.0).abs() < 1e-2);
        assert!((s.bow() - (0.5 + HALF_PI)).abs() < 1e-5);
    }

    #[test]
    fn helm_smoothing_and_driverless_reset() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.has_driver = true;
        s.throttle_request = 127;
        s.steering_request = -127;
        s.boat_physics_tick();
        // 127 * 0.12 = 15.24 -> 15
        assert_eq!(s.throttle, 15);
        assert_eq!(s.steering, -15);
        s.has_driver = false;
        s.boat_physics_tick();
        assert_eq!(
            (s.throttle_request, s.throttle, s.throttle_smoothed),
            (0, 0, 0.0)
        );
        assert_eq!(round_to_sbyte(2.5), 2);
        assert_eq!(round_to_sbyte(3.5), 4);
        assert_eq!(round_to_sbyte(-300.0), -128);
    }

    #[test]
    fn throttle_accelerates_along_the_bow_and_coasts_down() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.cached_floor_level = 50.0;
        s.has_driver = true;
        s.throttle_request = 127;
        for _ in 0..200 {
            s.boat_physics_tick();
            apply_force_and_torque(&mut s, 0.04, WindModel::Official, 0.0);
        }
        // around model.velocity * MoveSpeedMul / 2 (Boat: no wind profile); the cap is
        // approached smoothly, the minimum accel keeps it slightly above
        assert!(s.speed > 5.5 && s.speed < 6.2, "{}", s.speed);
        // the hull moves along the bow (yaw 0 -> bow = +90 deg -> physics +Z = game +Y)
        let v = s.body.velocity;
        assert!(v.x.abs() < 1e-3 && v.z > 1.0, "{v:?}");
        s.throttle_request = 0;
        let before = s.speed;
        for _ in 0..10 {
            s.boat_physics_tick();
            apply_force_and_torque(&mut s, 0.04, WindModel::Official, 0.0);
        }
        assert!(s.speed < before && s.speed > 0.0);
    }

    #[test]
    fn steering_builds_a_capped_yaw_rate() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.cached_floor_level = 50.0;
        s.has_driver = true;
        s.steering_request = 127;
        s.throttle_request = 127;
        for _ in 0..400 {
            s.boat_physics_tick();
            apply_force_and_torque(&mut s, 0.04, WindModel::Official, 0.0);
        }
        // the normal cap is a soft target while steering, the hard cap is twice it
        let cap = deg_to_rad(4.35);
        assert!(
            s.rot_speed > cap && s.rot_speed <= 2.0 * cap,
            "{}",
            s.rot_speed
        );
        assert_eq!(s.body.angular_velocity.y, -s.rot_speed);
        assert!(s.turn_speed_velocity_mul < 1.0 && s.turn_speed_velocity_mul >= 0.9);
    }

    #[test]
    fn official_wind_bonus_for_sailing_ships_on_the_north_south_axis() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.spec.kind = SlaveKind::SMALL_SAILING_SHIP;
        // bow along physics +Z = game +Y (north)
        assert_eq!(wind_speed_mul(&s, HALF_PI, WindModel::Official, 0.0), 1.15);
        assert_eq!(wind_speed_mul(&s, 0.0, WindModel::Official, 0.0), 1.0);
        s.spec.kind = SlaveKind::SPEEDBOAT;
        assert_eq!(wind_speed_mul(&s, HALF_PI, WindModel::Official, 0.0), 1.0);
        // realistic lateen: best on a beam reach
        assert_eq!(wind_speed_mul(&s, 0.0, WindModel::Realistic, 0.0), 1.15);
        assert_eq!(wind_speed_mul(&s, HALF_PI, WindModel::Realistic, 0.0), 0.85);
    }

    #[test]
    fn upright_stabilization_removes_roll_gradually() {
        let mut b = RigidBody::new(
            BoxShape::new(Vec3::new(1.0, 1.0, 1.0), Vec3::new(0.0, 0.0, 0.0)),
            10.0,
            Vec3::new(0.0, 0.0, 0.0),
            Quat::from_axis_angle(Vec3::new(1.0, 0.0, 0.0), 0.5),
        );
        apply_water_upright_stabilization(&mut b, 0.04);
        let up = b.orientation.rotate(Vec3::new(0.0, 1.0, 0.0));
        let angle = up.y.clamp(-1.0, 1.0).acos();
        assert!((angle - (0.5 - 2.25 * 0.04)).abs() < 1e-4, "{angle}");
    }
}
