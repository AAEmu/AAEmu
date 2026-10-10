//! Server-side skill controllers (`Models/Game/Skills/SkillControllers`): `SkillController`,
//! `SkillControllerKind`, `FloatingSkillController` (psychokinesis pull / gliding lift + fall),
//! `WanderingSkillController` (fear), `LeapSkillController` and `DashSkillController`.
//!
//! A controller moves a non-character unit (the C# never runs one for characters: the 1.2 client
//! moves itself) every 100 ms and broadcasts a `SCOneUnitMovementPacket` per step. The C#
//! `TickManager.OnTick.Subscribe(Tick, 100 ms)` is a Tokio task per controller that ticks with the
//! measured elapsed time until the controller reaches `SCState.Ended`.
//!
//! `ShipHarpoonRopeController` is not ported here: ships belong to the slaves subsystem.
//!
//! `GeoData.GetHeight` is the npcs subsystem's heightmap height (its navmesh path is not
//! ported): the steps snap to the ground within 1 m (3 m for wandering) and the floating
//! controller falls to it (`_startZ` when it is not positive).
//!
//! Deviations (documented in place):
//! * actor model stances (`ModelManager.GetActorModel(..).Stances`) are not loaded: the fear speed
//!   of the wandering controller is its 7 m/s fallback;
//! * `Npc.StopMovement` writes `CurrentGameStance` / `CurrentAlertness`, which belong to the npc
//!   subsystem and are not tracked yet: their C# defaults (`Combat`, `Idle`) are written;
//! * `Unit.CheckMovedPosition` raises `Events.OnMovement` (no Rust subscribers) and calls
//!   `WorldManager.AddVisibleObject`, which is ported (`add_visible_object` + zone changes).
//!
//! Locking: the controller's own mutex is never held while an object lock is taken by world /
//! broadcast code, and controller methods are never called while an object lock is held.

use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use rand::Rng;

use crate::models::movement::{
    convert_radian_to_direction, GameStanceType, MoveKind, MoveType, MoveTypeActorFlags,
    MoveTypeAlertness, MoveTypeFlags, UnitMove,
};
use crate::packets::world::SCOneUnitMovementPacket;
use crate::packets::world_objects::SCTargetChangedPacket;
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::data::{BuffTemplate, SkillControllerTemplate};
use super::geometry::{world_of, world_pos, Target};
use super::stats::{stat, StatKind};
use super::{broadcast, buffs};

/// The `TickManager` period of every controller.
pub const TICK_MS: u64 = 100;

/// `SkillConstants` tag ids.
pub mod skill_constants {
    pub const DECREASE_MOVE_SPEED: u32 = 161;
    pub const SHACKLE: u32 = 160;
    pub const SNARE: u32 = 27;
}

/// `SkillControllerKind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(u32)]
pub enum SkillControllerKind {
    None = 0,
    Floating = 1,
    Leap = 2,
    Wandering = 3,
    Dash = 4,
    Rope = 5,
    Anchor = 6,
    Rotate = 7,
    Flowgraph = 8,
}

impl SkillControllerKind {
    pub fn from_u32(v: u32) -> Option<Self> {
        Some(match v {
            0 => Self::None,
            1 => Self::Floating,
            2 => Self::Leap,
            3 => Self::Wandering,
            4 => Self::Dash,
            5 => Self::Rope,
            6 => Self::Anchor,
            7 => Self::Rotate,
            8 => Self::Flowgraph,
            _ => return None,
        })
    }
}

/// `SkillController.SCState`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScState {
    Created,
    Running,
    Ended,
}

/// `LeapSkillController.LeapDirection` / `DashSkillController.DashDirection` (same values).
/// Values outside the enum behave as `Both` in every C# use (`switch` default / `==` test).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum MoveDirection {
    Both = 0,
    ForwardOnly = 1,
    BackwardOnly = 2,
}

impl MoveDirection {
    pub fn from_i32(v: i32) -> Self {
        match v {
            1 => Self::ForwardOnly,
            2 => Self::BackwardOnly,
            _ => Self::Both,
        }
    }
}

/// `System.Numerics.Vector3` (positions).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct Vec3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

impl Vec3 {
    pub const fn new(x: f32, y: f32, z: f32) -> Self {
        Self { x, y, z }
    }

    fn length_squared(self) -> f32 {
        self.x * self.x + self.y * self.y + self.z * self.z
    }

    /// `Vector3.Normalize` (`v / v.Length()`).
    fn normalize(self) -> Self {
        let len = self.length_squared().sqrt();
        Self::new(self.x / len, self.y / len, self.z / len)
    }
}

// ---- MathUtil / PositionAndRotation helpers ----------------------------------------------------

const TWO_PI: f32 = std::f32::consts::PI * 2.0;

/// `MathUtil.CalculateAngleFrom(x1, y1, x2, y2)` (degrees, `% 360`).
pub fn calculate_angle_from(a: Vec3, b: Vec3) -> f64 {
    let rad = f64::from(b.y - a.y).atan2(f64::from(b.x - a.x));
    (rad / std::f64::consts::PI * 180.0) % 360.0
}

/// `MathUtil.CalculateDistance(Vector3, Vector3, includeZ)` (double precision).
pub fn calculate_distance(a: Vec3, b: Vec3, include_z: bool) -> f32 {
    let dx = f64::from(a.x) - f64::from(b.x);
    let dy = f64::from(a.y) - f64::from(b.y);
    if include_z {
        let dz = f64::from(a.z) - f64::from(b.z);
        (dx * dx + dy * dy + dz * dz).sqrt() as f32
    } else {
        (dx * dx + dy * dy).sqrt() as f32
    }
}

/// `MathUtil.AddDistanceToFront(distance, x, y, rotZRad)`.
pub fn add_distance_to_front(distance: f32, x: f32, y: f32, rot_z_rad: f32) -> (f32, f32) {
    let rad = f64::from(rot_z_rad);
    (
        distance * rad.cos() as f32 + x,
        distance * rad.sin() as f32 + y,
    )
}

/// `PositionAndRotation.AddDistanceToFront(travelDist, targetDist, position, other)`.
pub fn add_distance_to_front_between(
    travel_dist: f32,
    target_dist: f32,
    position: Vec3,
    other: Vec3,
) -> Vec3 {
    let ratio = travel_dist / target_dist;
    Vec3::new(
        (other.x - position.x) * ratio + position.x,
        (other.y - position.y) * ratio + position.y,
        (other.z - position.z) * ratio + position.z,
    )
}

/// `float.DegToRad()`.
fn deg_to_rad_f32(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

/// `PositionAndRotation.ToRollPitchYawSBytesMovement()` of a rotation (radians).
pub fn roll_pitch_yaw_sbytes_movement(rx: f32, ry: f32, rz: f32) -> (i8, i8, i8) {
    let f = |v: f32| convert_radian_to_direction(f64::from(v - TWO_PI));
    (f(rx), f(ry), f(rz))
}

/// The step every moving controller sends: the yaw (radians, `SetRotationDegree(0, 0,
/// angle - 90)`) and the velocity (`AddDistanceToFront(4000, 0, 0, angle.DegToRad())`) for the
/// heading from `from` to `to`.
pub fn heading(from: Vec3, to: Vec3) -> (f32, i16, i16) {
    let angle = calculate_angle_from(from, to);
    let (vx, vy) = add_distance_to_front(
        4000.0,
        0.0,
        0.0,
        (std::f64::consts::PI / 180.0 * angle) as f32,
    );
    (deg_to_rad_f32(angle as f32 - 90.0), vx as i16, vy as i16)
}

/// `(uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds`.
/// `Owner.ParentWorld.Template.GeoData.GetHeight(Owner.Transform.World.Position)` (the npcs
/// subsystem's heightmaps; 0 without a world or heightmaps, like the C# exception path).
fn geo_height(server: &GameServer, owner: &WorldObject) -> f32 {
    let Some(w) = world_of(server, owner) else {
        return 0.0;
    };
    let p = w.world_pos_rot(owner);
    crate::systems::npcs::npc::spawner::geo_data_height(server, &w, p.x, p.y)
}

/// `FloatingSkillController.GetGroundHeight()`: the heightmap height, `_startZ` when it is not
/// positive.
fn ground_height(server: &GameServer, owner: &WorldObject, start_z: f32) -> f32 {
    let geo = geo_height(server, owner);
    if geo > 0.0 {
        geo
    } else {
        start_z
    }
}

fn ms_of_day() -> u32 {
    now_ms().rem_euclid(86_400_000) as u32
}

/// `LeapSkillController.ApplyDirectionConstraint`.
pub fn apply_direction_constraint(
    direction: MoveDirection,
    owner_pos: Vec3,
    candidate_end: Vec3,
    target_pos: Vec3,
) -> Vec3 {
    let owner_to_target = calculate_distance(owner_pos, target_pos, true);
    let end_to_target = calculate_distance(candidate_end, target_pos, true);
    match direction {
        MoveDirection::BackwardOnly if end_to_target < owner_to_target => owner_pos,
        MoveDirection::ForwardOnly if end_to_target > owner_to_target => owner_pos,
        _ => candidate_end,
    }
}

// ---- per-kind state ----------------------------------------------------------------------------

/// Leap / Dash: a straight move to a fixed end position at a fixed speed.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct LinearMove {
    /// `Duration` (`Value[2]`, ms).
    pub duration: i32,
    /// `DistanceOffset` (`Value[3]`, mm).
    pub distance_offset: i32,
    /// `Direction` (`Value[6]`).
    pub direction: MoveDirection,
    pub end_position: Vec3,
    pub calculated_speed: f32,
}

fn duration_sec(duration: i32) -> f32 {
    if duration > 0 {
        duration as f32 / 1000.0
    } else {
        0.5
    }
}

impl LinearMove {
    /// The `LeapSkillController` constructor math.
    pub fn leap(template: &SkillControllerTemplate, owner_pos: Vec3, target_pos: Vec3) -> Self {
        let duration = template.value[2];
        let distance_offset = template.value[3];
        let direction = MoveDirection::from_i32(template.value[6]);
        let angle_deg = calculate_angle_from(owner_pos, target_pos) as f32;
        let angle_rad = (f64::from(angle_deg) * std::f64::consts::PI / 180.0) as f32;
        let offset = distance_offset as f32 / 1000.0;
        let (ex, ey) = add_distance_to_front(offset, target_pos.x, target_pos.y, angle_rad);
        let mut end = Vec3::new(ex, ey, target_pos.z);
        let constrained = apply_direction_constraint(direction, owner_pos, end, target_pos);
        if constrained != end {
            tracing::debug!(
                "LeapSC: {direction:?} constraint violated — staying at owner position"
            );
            end = constrained;
        }
        let distance = calculate_distance(owner_pos, end, true);
        Self {
            duration,
            distance_offset,
            direction,
            end_position: end,
            calculated_speed: distance / duration_sec(duration),
        }
    }

    /// The `DashSkillController` constructor math.
    pub fn dash(template: &SkillControllerTemplate, owner_pos: Vec3, target_pos: Vec3) -> Self {
        let duration = template.value[2];
        let distance_offset = template.value[3];
        let direction = MoveDirection::from_i32(template.value[6]);
        let mut angle_deg = calculate_angle_from(owner_pos, target_pos) as f32;
        let dist = distance_offset as f32 / 1000.0;
        if direction == MoveDirection::BackwardOnly {
            angle_deg += 180.0;
        }
        let angle_rad = (f64::from(angle_deg) * std::f64::consts::PI / 180.0) as f32;
        let (ex, ey) = add_distance_to_front(dist, owner_pos.x, owner_pos.y, angle_rad);
        let end = Vec3::new(ex, ey, owner_pos.z);
        let total = calculate_distance(owner_pos, end, true);
        Self {
            duration,
            distance_offset,
            direction,
            end_position: end,
            calculated_speed: total / duration_sec(duration),
        }
    }
}

/// `FloatingSkillController` state.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct FloatingMove {
    pub end_position: Vec3,
    pub speed: f32,
    pub pull_distance: f32,
    pub stop_distance: f32,
    pub is_lift_mode: bool,
    pub lift_height: f32,
    pub lift_speed: f32,
    pub lift_duration: f32,
    pub is_falling: bool,
    pub fall_speed: f32,
    pub start_z: f32,
    pub lift_start: Option<Instant>,
}

const GRAVITY: f32 = 9.81;
const MAX_FALL_SPEED: f32 = 20.0;

impl FloatingMove {
    /// The `FloatingSkillController` constructor math.
    pub fn new(
        template: Option<&SkillControllerTemplate>,
        owner_pos: Vec3,
        target_pos: Vec3,
        psychokinesis_speed: f32,
        lift_height: f32,
        lift_speed: f32,
        lift_duration: f32,
    ) -> Self {
        let lift_speed = if lift_speed > 0.0 { lift_speed } else { 3.0 };
        let is_lift_mode = lift_height > 0.0;
        let mut s = Self {
            end_position: owner_pos,
            speed: lift_speed,
            pull_distance: 0.0,
            stop_distance: 0.0,
            is_lift_mode,
            lift_height,
            lift_speed,
            lift_duration,
            is_falling: false,
            fall_speed: 0.0,
            start_z: 0.0,
            lift_start: None,
        };
        if is_lift_mode {
            return s;
        }
        s.speed = if psychokinesis_speed > 0.0 {
            psychokinesis_speed
        } else {
            match template {
                Some(t) if t.value[0] > 0 => t.value[0] as f32 / 1000.0,
                _ => 5.0,
            }
        };
        let dist = calculate_distance(owner_pos, target_pos, true);
        s.stop_distance = 1.5;
        s.pull_distance = (dist - s.stop_distance).max(0.0);
        if dist > s.stop_distance {
            let mut d = Vec3::new(
                target_pos.x - owner_pos.x,
                target_pos.y - owner_pos.y,
                target_pos.z - owner_pos.z,
            )
            .normalize();
            d.z = 0.0;
            if d.length_squared() < 0.01 {
                d = Vec3::new(1.0, 0.0, 0.0);
            }
            let d = d.normalize();
            s.end_position = Vec3::new(
                owner_pos.x + d.x * s.pull_distance,
                owner_pos.y + d.y * s.pull_distance,
                owner_pos.z,
            );
        }
        s
    }
}

/// `WanderingSkillController` state.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct WanderingMove {
    pub current_waypoint: Vec3,
    /// Seconds.
    pub duration: f32,
    /// Seconds.
    pub change_interval: f32,
    pub start: Instant,
    pub last_direction_change: Instant,
}

/// `WanderingSkillController.GetFearSpeed()`: actor model stances are not loaded in Rust, so this
/// is always the C# fallback.
const FEAR_SPEED_FALLBACK: f32 = 7.0;

/// `PickNewDirection`: a random point 5..10 m away.
fn random_waypoint(current: Vec3) -> Vec3 {
    let mut rng = rand::thread_rng();
    let angle = rng.gen::<f64>() * 2.0 * std::f64::consts::PI;
    let dist = 5.0 + (rng.gen::<f64>() * 5.0) as f32;
    Vec3::new(
        current.x + (angle.cos() * f64::from(dist)) as f32,
        current.y + (angle.sin() * f64::from(dist)) as f32,
        current.z,
    )
}

/// The concrete controller class.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum Behavior {
    Floating(FloatingMove),
    Wandering(WanderingMove),
    Leap(LinearMove),
    Dash(LinearMove),
}

#[derive(Debug)]
struct Inner {
    state: ScState,
    source_buff_id: u32,
    behavior: Behavior,
}

// ---- the controller ----------------------------------------------------------------------------

/// `SkillController` (+ the concrete subclass in [`Behavior`]).
pub struct SkillController {
    /// `Template` (`None` for the floating controllers the buff fallbacks create directly).
    pub template: Option<SkillControllerTemplate>,
    /// `Owner` (the moved unit).
    owner: WorldObject,
    /// `Target?.ObjId` (0 = none; `u32::MAX` = a position unit).
    pub target_obj_id: u32,
    inner: Mutex<Inner>,
}

/// `Unit.ActiveSkillController` handle (compares by pointer).
#[derive(Clone)]
pub struct SkillControllerRef(pub Arc<SkillController>);

impl PartialEq for SkillControllerRef {
    fn eq(&self, other: &Self) -> bool {
        Arc::ptr_eq(&self.0, &other.0)
    }
}

impl std::fmt::Debug for SkillControllerRef {
    // Never locks the controller (a unit's data may be locked while it is formatted).
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SkillControllerRef")
            .field("template", &self.0.template.as_ref().map(|t| t.id))
            .field("owner", &self.0.owner.obj_id())
            .field("target", &self.0.target_obj_id)
            .finish()
    }
}

impl std::ops::Deref for SkillControllerRef {
    type Target = SkillController;
    fn deref(&self) -> &SkillController {
        &self.0
    }
}

/// What a tick decided to do once the controller lock is released.
enum Next {
    Continue,
    End,
    FinalEnd,
}

/// `SkillController.CreateSkillController(template, owner, target, psychokinesisSpeed,
/// liftHeight, liftSpeed, liftDuration)`; `None` for the kinds without a server controller.
#[allow(clippy::too_many_arguments)]
pub fn create(
    server: &GameServer,
    template: &SkillControllerTemplate,
    owner: &WorldObject,
    target: &Target,
    psychokinesis_speed: f32,
    lift_height: f32,
    lift_speed: f32,
    lift_duration: f32,
) -> Option<SkillControllerRef> {
    match SkillControllerKind::from_u32(template.kind_id) {
        Some(SkillControllerKind::Floating) => Some(new_floating(
            server,
            Some(template),
            owner,
            target,
            psychokinesis_speed,
            lift_height,
            lift_speed,
            lift_duration,
        )),
        Some(SkillControllerKind::Wandering) => {
            tracing::trace!("SkillController: create WanderingSkillController");
            let now = Instant::now();
            let tr = owner.transform();
            let rd = |v: i32| if v > 0 { v as f32 / 1000.0 } else { 0.0 };
            let duration = rd(template.value[1]);
            let interval = rd(template.value[2]);
            let w = WanderingMove {
                current_waypoint: random_waypoint(Vec3::new(tr.x, tr.y, tr.z)),
                duration: if duration > 0.0 { duration } else { 5.0 },
                change_interval: if interval > 0.0 { interval } else { 1.5 },
                start: now,
                last_direction_change: now,
            };
            tracing::debug!(
                "WanderingSC: owner={} duration={:.1}s interval={:.1}s",
                owner.obj_id(),
                w.duration,
                w.change_interval
            );
            Some(new_ref(
                Some(template),
                owner,
                target,
                Behavior::Wandering(w),
            ))
        }
        Some(SkillControllerKind::Leap) => {
            tracing::trace!("SkillController: create LeapSkillController");
            let (o, t) = positions(server, owner, target);
            let l = LinearMove::leap(template, o, t);
            tracing::debug!(
                "LeapSC: owner={} target={} dir={:?} dur={}ms endPos=({:.1},{:.1},{:.1}) speed={:.1}",
                owner.obj_id(),
                target.obj_id(),
                l.direction,
                l.duration,
                l.end_position.x,
                l.end_position.y,
                l.end_position.z,
                l.calculated_speed
            );
            Some(new_ref(Some(template), owner, target, Behavior::Leap(l)))
        }
        Some(SkillControllerKind::Dash) => {
            tracing::trace!("SkillController: create DashSkillController");
            let (o, t) = positions(server, owner, target);
            let d = LinearMove::dash(template, o, t);
            Some(new_ref(Some(template), owner, target, Behavior::Dash(d)))
        }
        _ => {
            tracing::trace!(
                "SkillController: create defaultSkillController kind={}",
                template.kind_id
            );
            None
        }
    }
}

/// `new FloatingSkillController(template, owner, target, psychokinesisSpeed, liftHeight,
/// liftSpeed, liftDuration)`.
#[allow(clippy::too_many_arguments)]
pub fn new_floating(
    server: &GameServer,
    template: Option<&SkillControllerTemplate>,
    owner: &WorldObject,
    target: &Target,
    psychokinesis_speed: f32,
    lift_height: f32,
    lift_speed: f32,
    lift_duration: f32,
) -> SkillControllerRef {
    let (o, t) = positions(server, owner, target);
    let f = FloatingMove::new(
        template,
        o,
        t,
        psychokinesis_speed,
        lift_height,
        lift_speed,
        lift_duration,
    );
    if f.is_lift_mode {
        tracing::debug!(
            "FloatingSC [LIFT]: owner={} liftHeight={:.1} liftSpeed={:.1} liftDuration={:.1}",
            owner.obj_id(),
            f.lift_height,
            f.lift_speed,
            f.lift_duration
        );
    } else {
        tracing::debug!(
            "FloatingSC [PULL]: owner={} target={} pullDist={:.1} speed={:.1} endPos=({:.1},{:.1},{:.1})",
            owner.obj_id(),
            target.obj_id(),
            f.pull_distance,
            f.speed,
            f.end_position.x,
            f.end_position.y,
            f.end_position.z
        );
    }
    new_ref(template, owner, target, Behavior::Floating(f))
}

fn positions(server: &GameServer, owner: &WorldObject, target: &Target) -> (Vec3, Vec3) {
    let o = world_pos(server, &Target::Obj(owner.clone()));
    let t = world_pos(server, target);
    (Vec3::new(o.x, o.y, o.z), Vec3::new(t.x, t.y, t.z))
}

fn new_ref(
    template: Option<&SkillControllerTemplate>,
    owner: &WorldObject,
    target: &Target,
    behavior: Behavior,
) -> SkillControllerRef {
    SkillControllerRef(Arc::new(SkillController {
        template: template.cloned(),
        owner: owner.clone(),
        target_obj_id: target.obj_id(),
        inner: Mutex::new(Inner {
            state: ScState::Created,
            source_buff_id: 0,
            behavior,
        }),
    }))
}

/// `if (unit.ActiveSkillController != null) unit.ActiveSkillController.End(force: true);
/// unit.ActiveSkillController = sc; sc.Execute();`
pub fn activate(server: &GameServer, sc: &SkillControllerRef) {
    let owner = sc.owner.clone();
    let previous = owner
        .with_unit(|u| u.unit().skill_state.active_skill_controller.clone())
        .flatten();
    if let Some(prev) = previous {
        prev.end(server, true);
    }
    owner.with_unit(|u| u.unit_mut().skill_state.active_skill_controller = Some(sc.clone()));
    sc.execute(server);
}

/// `Unit.ActiveSkillController` of a unit.
pub fn active(owner: &WorldObject) -> Option<SkillControllerRef> {
    owner
        .with_unit(|u| u.unit().skill_state.active_skill_controller.clone())
        .flatten()
}

impl SkillControllerRef {
    /// `Execute()`.
    pub fn execute(&self, server: &GameServer) {
        self.lock().state = ScState::Running;
        tracing::trace!(
            "SkillController: Npc {} entering execute state=Running",
            self.owner.obj_id()
        );
        let behavior = self.lock().behavior;
        match behavior {
            Behavior::Floating(f) if f.is_lift_mode => {
                let z = self.owner.transform().z;
                if let Behavior::Floating(f) = &mut self.lock().behavior {
                    f.start_z = z;
                    f.lift_start = Some(Instant::now());
                }
                tracing::debug!(
                    "FloatingSC.Execute [LIFT]: owner={} startZ={z:.1} targetZ={:.1}",
                    self.owner.obj_id(),
                    z + f.lift_height
                );
                self.subscribe(server);
            }
            Behavior::Floating(f) => {
                if f.pull_distance <= 0.5 {
                    tracing::debug!(
                        "FloatingSC.Execute [PULL]: owner={} already close, skipping",
                        self.owner.obj_id()
                    );
                    self.end(server, false);
                    return;
                }
                self.subscribe(server);
            }
            Behavior::Wandering(_) => {
                let now = Instant::now();
                if let Behavior::Wandering(w) = &mut self.lock().behavior {
                    w.start = now;
                    w.last_direction_change = now;
                }
                // Clear the visual target on the client (keep the server-side targets).
                if self.owner.as_npc().is_some() {
                    broadcast(
                        server,
                        &self.owner,
                        &SCTargetChangedPacket {
                            obj_id: self.owner.obj_id(),
                            target_obj_id: 0,
                        },
                        true,
                    );
                }
                self.subscribe(server);
            }
            Behavior::Leap(_) | Behavior::Dash(_) => self.subscribe(server),
        }
    }

    /// `End(force)`. `force` = the controller is being replaced (no lift-to-fall transition).
    pub fn end(&self, server: &GameServer, force: bool) {
        self.end_impl(server, force, true);
    }

    fn end_impl(&self, server: &GameServer, force: bool, owner_present: bool) {
        let behavior = self.lock().behavior;
        if let Behavior::Floating(f) = behavior {
            if !force && f.is_lift_mode && !f.is_falling && owner_present && !self.owner_dead() {
                let ground_z = ground_height(server, &self.owner, f.start_z);
                let z = self.owner.transform().z;
                if ground_z > 0.0 && z > ground_z + 0.5 {
                    tracing::debug!(
                        "FloatingSC.End [LIFT→FALL]: owner={} starting fall from Z={z:.1} to ground={ground_z:.1}",
                        self.owner.obj_id()
                    );
                    let mut inner = self.lock();
                    if let Behavior::Floating(f) = &mut inner.behavior {
                        f.is_falling = true;
                        f.fall_speed = 0.0;
                    }
                    inner.state = ScState::Running;
                    return;
                }
            }
        }
        self.base_end(owner_present);
        if let Behavior::Wandering(_) = behavior {
            if !owner_present || self.owner.as_npc().is_none() {
                return;
            }
            let current_target = self
                .owner
                .with_unit(|u| u.unit().current_target)
                .unwrap_or(0);
            if current_target != 0 {
                broadcast(
                    server,
                    &self.owner,
                    &SCTargetChangedPacket {
                        obj_id: self.owner.obj_id(),
                        target_obj_id: current_target,
                    },
                    true,
                );
            }
            npc_stop_movement(server, &self.owner);
        }
    }

    fn tick(&self, server: &GameServer, delta: Duration) {
        let dt = delta.as_secs_f64() as f32;
        let present = self.owner_present(server);
        let behavior = self.lock().behavior;
        let source = self.source_buff_id();
        let next = match behavior {
            Behavior::Leap(l) | Behavior::Dash(l) => {
                // Owner gone / dead; a voluntary move (no source buff) also ends on stun / sleep.
                if !present
                    || self.owner_dead()
                    || (source == 0 && self.has_effect(server, |t| t.stun || t.sleep))
                {
                    Next::End
                } else {
                    let threshold = if matches!(behavior, Behavior::Leap(_)) {
                        1.0
                    } else {
                        0.5
                    };
                    self.linear_move_towards(server, &l, l.calculated_speed * dt, threshold)
                }
            }
            Behavior::Floating(f) => {
                if !present || self.owner_dead() {
                    Next::FinalEnd
                } else if f.is_falling {
                    self.fall_tick(server, &f, dt)
                } else if source == 0
                    && self.has_effect(server, |t| t.stun || t.sleep || t.knockdown)
                {
                    Next::End
                } else if f.is_lift_mode {
                    self.lift_tick(server, &f, dt)
                } else {
                    self.pull_tick(server, &f, f.speed * dt)
                }
            }
            Behavior::Wandering(w) => {
                if !present || self.owner_dead() {
                    Next::End
                } else {
                    self.wandering_tick(server, w, dt)
                }
            }
        };
        match next {
            Next::Continue => {}
            Next::End => self.end_impl(server, false, present),
            Next::FinalEnd => self.final_end(present),
        }
    }

    /// `TickManager.Instance.OnTick.Subscribe(Tick, 100 ms)`.
    fn subscribe(&self, server: &GameServer) {
        let Some(srv) = server.systems.skills.server() else {
            tracing::debug!("skills: no bound server, skill controller not ticking");
            return;
        };
        let Ok(handle) = tokio::runtime::Handle::try_current() else {
            tracing::debug!("skills: no Tokio runtime, skill controller not ticking");
            return;
        };
        let me = self.clone();
        handle.spawn(async move {
            let mut last = Instant::now();
            loop {
                tokio::time::sleep(Duration::from_millis(TICK_MS)).await;
                if me.state() == ScState::Ended {
                    break;
                }
                let now = Instant::now();
                let delta = now - last;
                last = now;
                me.tick(&srv, delta);
                if me.state() == ScState::Ended {
                    break;
                }
            }
        });
    }
}

impl SkillController {
    /// `base.End()` + `TickManager.UnSubscribe` (the tick task stops at `Ended`).
    fn base_end(&self, owner_present: bool) {
        self.lock().state = ScState::Ended;
        let me: *const SkillController = self;
        self.owner.with_unit(|u| {
            let slot = &mut u.unit_mut().skill_state.active_skill_controller;
            if slot.as_ref().is_some_and(|s| Arc::as_ptr(&s.0) == me) {
                *slot = None;
            }
        });
        if owner_present {
            tracing::trace!(
                "SkillController: Npc {} entering end state=Ended",
                self.owner.obj_id()
            );
        }
    }

    /// `FloatingSkillController.FinalEnd()`.
    fn final_end(&self, owner_present: bool) {
        self.base_end(owner_present);
    }

    fn lock(&self) -> MutexGuard<'_, Inner> {
        self.inner.lock().expect("skill controller poisoned")
    }

    /// `State`.
    pub fn state(&self) -> ScState {
        self.lock().state
    }

    /// `SourceBuffId` (the buff that started this controller, 0 = none).
    pub fn source_buff_id(&self) -> u32 {
        self.lock().source_buff_id
    }

    pub fn set_source_buff_id(&self, id: u32) {
        self.lock().source_buff_id = id;
    }

    /// `Owner`.
    pub fn owner(&self) -> &WorldObject {
        &self.owner
    }

    /// A copy of the per-kind state.
    pub fn behavior(&self) -> Behavior {
        self.lock().behavior
    }

    /// The kind of this controller.
    pub fn kind(&self) -> SkillControllerKind {
        match self.lock().behavior {
            Behavior::Floating(_) => SkillControllerKind::Floating,
            Behavior::Wandering(_) => SkillControllerKind::Wandering,
            Behavior::Leap(_) => SkillControllerKind::Leap,
            Behavior::Dash(_) => SkillControllerKind::Dash,
        }
    }

    /// `Owner == null`: the owner left the world.
    fn owner_present(&self, server: &GameServer) -> bool {
        let id = self.owner.obj_id();
        server
            .world
            .object(id)
            .or_else(|| server.world.character_by_obj_id(id).map(WorldObject::from))
            .is_some_and(|o| o.same(&self.owner))
    }

    fn owner_dead(&self) -> bool {
        self.owner
            .with_unit(|u| u.unit().is_dead())
            .unwrap_or(false)
    }

    fn local_pos(&self) -> Vec3 {
        let t = self.owner.transform();
        Vec3::new(t.x, t.y, t.z)
    }

    /// `Owner.Buffs.HasEffectsMatchingCondition(e => pred(e.Template))`.
    fn has_effect(&self, server: &GameServer, pred: impl Fn(&BuffTemplate) -> bool) -> bool {
        let ids: Vec<u32> = self
            .owner
            .with_base_unit(|b| b.buffs().effects.iter().map(|e| e.template.id).collect());
        let data = &server.game_data.systems.skills;
        ids.into_iter()
            .any(|id| data.buff(id).is_some_and(|t| pred(t)))
    }

    /// `Buffs.CheckBuffsExcludingTags(GetBuffsByTagId(Shackle), [DecreaseMoveSpeed])`.
    fn shackled(&self, server: &GameServer) -> bool {
        let data = &server.game_data.systems.skills;
        let Some(ids) = data
            .buffs_by_tag(skill_constants::SHACKLE)
            .filter(|l| !l.is_empty())
        else {
            return false;
        };
        let effects: Vec<u32> = self
            .owner
            .with_base_unit(|b| b.buffs().effects.iter().map(|e| e.template.id).collect());
        effects.into_iter().any(|id| {
            id != 0
                && ids.contains(&id)
                && !data
                    .buff_tags(id)
                    .contains(&skill_constants::DECREASE_MOVE_SPEED)
        })
    }

    /// `DashSkillController.MoveTowards` / `LeapSkillController.MoveTowards`.
    fn linear_move_towards(
        &self,
        server: &GameServer,
        l: &LinearMove,
        mut distance: f32,
        end_threshold: f32,
    ) -> Next {
        if self.source_buff_id() == 0 {
            distance *= stat(server, &self.owner, StatKind::MoveSpeedMul) as f32;
            if distance < 0.01 {
                return Next::End;
            }
            if self.has_effect(server, |t| {
                t.stun || t.sleep || t.root || t.knockdown || t.fastened
            }) || self.owner_dead()
            {
                return Next::End;
            }
            // A voluntary player dash / leap ends on combat engagement.
            if self.owner.as_player().is_some()
                && self
                    .owner
                    .with_unit(|u| u.unit().is_in_battle)
                    .unwrap_or(false)
            {
                return Next::End;
            }
            // NPC: hard-root gate (slows ride along via the DecreaseMoveSpeed exclusion).
            if self.owner.as_npc().is_some()
                && (self.shackled(server)
                    || buffs::check_buffs_by_tag(server, &self.owner, skill_constants::SNARE))
            {
                return Next::End;
            }
        }
        let cur = self.local_pos();
        let target_dist = calculate_distance(cur, l.end_position, true);
        if target_dist <= end_threshold {
            return Next::End;
        }
        let travel = target_dist.min(distance);
        self.step(
            server,
            cur,
            l.end_position,
            travel,
            target_dist,
            4,
            MoveTypeFlags::MOVING,
        );
        Next::Continue
    }

    /// The common "move `travel` towards `end`, face it, broadcast" step of the moving
    /// controllers.
    #[allow(clippy::too_many_arguments)]
    fn step(
        &self,
        server: &GameServer,
        cur: Vec3,
        end: Vec3,
        travel: f32,
        target_dist: f32,
        actor_flags: u8,
        flags: MoveTypeFlags,
    ) {
        let mut new = add_distance_to_front_between(travel, target_dist, cur, end);
        self.owner
            .with_transform(|t| t.set_xyz(new.x, new.y, new.z));
        // GeoData.GetHeight snap: |newZ - groundZ| < 1 m (wandering: ground > 0 and < 3 m).
        let wandering = matches!(self.lock().behavior, Behavior::Wandering(_));
        let upd_z = geo_height(server, &self.owner);
        let snap = if wandering {
            upd_z > 0.0 && (new.z - upd_z).abs() < 3.0
        } else {
            (new.z - upd_z).abs() < 1.0
        };
        if snap {
            new.z = upd_z;
        }
        let (yaw, vel_x, vel_y) = heading(new, end);
        self.owner.with_transform(|t| {
            t.set_xyz(new.x, new.y, new.z);
            t.rot_x = 0.0;
            t.rot_y = 0.0;
            t.rot_z = yaw;
        });
        let (rx, ry, rz) = roll_pitch_yaw_sbytes_movement(0.0, 0.0, yaw);
        let um = UnitMove {
            x: new.x,
            y: new.y,
            z: new.z,
            vel_x,
            vel_y,
            rotation_x: rx,
            rotation_y: ry,
            rotation_z: rz,
            delta_movement: [0, 127, 0],
            stance: GameStanceType(0),
            alertness: MoveTypeAlertness::COMBAT,
            actor_flags: MoveTypeActorFlags(actor_flags),
            ..UnitMove::default()
        };
        self.send_move(server, um, flags);
    }

    /// `CheckMovedPosition(oldPosition)` + `BroadcastPacket(SCOneUnitMovementPacket, false)`.
    fn send_move(&self, server: &GameServer, um: UnitMove, flags: MoveTypeFlags) {
        let mut mt = MoveType::new(MoveKind::Unit(um));
        mt.flags = flags;
        mt.time = ms_of_day();
        check_moved_position(server, &self.owner);
        broadcast(
            server,
            &self.owner,
            &SCOneUnitMovementPacket {
                obj_id: self.owner.obj_id(),
                move_type: mt,
            },
            false,
        );
    }

    /// `Moving | (Owner is Npc && IsInBattle ? InCombat : 0)`.
    fn floating_flags(&self) -> MoveTypeFlags {
        let in_combat = self.owner.as_npc().is_some()
            && self
                .owner
                .with_unit(|u| u.unit().is_in_battle)
                .unwrap_or(false);
        if in_combat {
            MoveTypeFlags::MOVING | MoveTypeFlags::IN_COMBAT
        } else {
            MoveTypeFlags::MOVING
        }
    }

    /// `FloatingSkillController.LiftTick`.
    fn lift_tick(&self, server: &GameServer, f: &FloatingMove, dt: f32) -> Next {
        let cur = self.local_pos();
        let current_height = cur.z - f.start_z;
        if f.lift_duration > 0.0 {
            let elapsed = f.lift_start.map_or(0.0, |s| s.elapsed().as_secs_f64());
            if elapsed >= f64::from(f.lift_duration) {
                tracing::debug!(
                    "FloatingSC [LIFT]: owner={} lift duration expired after {elapsed:.1}s — descending",
                    self.owner.obj_id()
                );
                return Next::End;
            }
        }
        if current_height >= f.lift_height {
            tracing::trace!(
                "FloatingSC [LIFT]: owner={} reached lift height {:.1}, holding",
                self.owner.obj_id(),
                f.lift_height
            );
            return Next::Continue;
        }
        let lift = (f.lift_speed * dt).min(f.lift_height - current_height);
        let new_z = cur.z + lift;
        self.vertical_move(server, cur, new_z, 127);
        Next::Continue
    }

    /// `FloatingSkillController.FallTick`.
    fn fall_tick(&self, server: &GameServer, f: &FloatingMove, dt: f32) -> Next {
        let cur = self.local_pos();
        let ground_z = ground_height(server, &self.owner, f.start_z);
        if cur.z <= ground_z + 0.1 {
            tracing::debug!(
                "FloatingSC [FALL]: owner={} reached ground Z={ground_z:.1}",
                self.owner.obj_id()
            );
            self.owner.with_transform(|t| t.set_height(ground_z));
            // End the controller before StopMovement (the Npc.MoveTowards guard).
            self.final_end(true);
            if self.owner.as_npc().is_some() {
                npc_stop_movement(server, &self.owner);
            }
            return Next::Continue;
        }
        let fall_speed = (f.fall_speed + GRAVITY * dt).min(MAX_FALL_SPEED);
        if let Behavior::Floating(ff) = &mut self.lock().behavior {
            ff.fall_speed = fall_speed;
        }
        let new_z = (cur.z - fall_speed * dt).max(ground_z);
        self.vertical_move(server, cur, new_z, -127);
        Next::Continue
    }

    /// The lift / fall step: only Z changes, actor flags 5, vertical delta movement.
    fn vertical_move(&self, server: &GameServer, cur: Vec3, new_z: f32, delta_z: i8) {
        let tr = self.owner.with_transform(|t| {
            t.set_xyz(cur.x, cur.y, new_z);
            *t
        });
        let (rx, ry, rz) = roll_pitch_yaw_sbytes_movement(tr.rot_x, tr.rot_y, tr.rot_z);
        let um = UnitMove {
            x: cur.x,
            y: cur.y,
            z: new_z,
            rotation_x: rx,
            rotation_y: ry,
            rotation_z: rz,
            delta_movement: [0, 0, delta_z],
            stance: GameStanceType(0),
            alertness: MoveTypeAlertness::COMBAT,
            actor_flags: MoveTypeActorFlags(5),
            ..UnitMove::default()
        };
        self.send_move(server, um, self.floating_flags());
    }

    /// `FloatingSkillController.PullTick` / `MoveTowardsTarget`.
    fn pull_tick(&self, server: &GameServer, f: &FloatingMove, distance: f32) -> Next {
        if distance < 0.01 {
            return Next::End;
        }
        let cur = self.local_pos();
        let target_dist = calculate_distance(cur, f.end_position, true);
        if target_dist <= 0.5 {
            tracing::debug!(
                "FloatingSC [PULL]: owner={} reached pull endpoint (dist={target_dist:.2}), ending",
                self.owner.obj_id()
            );
            return Next::End;
        }
        let travel = target_dist.min(distance);
        let flags = self.floating_flags();
        self.step(server, cur, f.end_position, travel, target_dist, 4, flags);
        Next::Continue
    }

    /// `WanderingSkillController.Tick` / `MoveTowardsWaypoint`.
    fn wandering_tick(&self, server: &GameServer, w: WanderingMove, dt: f32) -> Next {
        let elapsed = w.start.elapsed().as_secs_f64();
        if elapsed >= f64::from(w.duration) {
            tracing::debug!(
                "WanderingSC.Tick: owner={} fear expired after {elapsed:.1}s",
                self.owner.obj_id()
            );
            return Next::End;
        }
        let mut waypoint = w.current_waypoint;
        if w.last_direction_change.elapsed().as_secs_f64() >= f64::from(w.change_interval) {
            waypoint = self.pick_new_direction();
        }
        let distance = FEAR_SPEED_FALLBACK * dt;
        if distance < 0.01 {
            return Next::Continue;
        }
        let cur = self.local_pos();
        let dist = calculate_distance(cur, waypoint, true);
        if dist <= 1.0 {
            self.pick_new_direction();
            return Next::Continue;
        }
        let travel = dist.min(distance);
        self.step(
            server,
            cur,
            waypoint,
            travel,
            dist,
            4,
            MoveTypeFlags::MOVING | MoveTypeFlags::IN_COMBAT,
        );
        Next::Continue
    }

    /// `PickNewDirection()` + `_lastDirectionChange = now`.
    fn pick_new_direction(&self) -> Vec3 {
        let wp = random_waypoint(self.local_pos());
        if let Behavior::Wandering(w) = &mut self.lock().behavior {
            w.current_waypoint = wp;
            w.last_direction_change = Instant::now();
        }
        tracing::trace!(
            "WanderingSC: owner={} new waypoint ({:.1},{:.1},{:.1})",
            self.owner.obj_id(),
            wp.x,
            wp.y,
            wp.z
        );
        wp
    }
}

/// `Unit.CheckMovedPosition`: `WorldManager.AddVisibleObject(this)` (regions, visibility, zone
/// changes).
fn check_moved_position(server: &GameServer, owner: &WorldObject) {
    if let Some(world) = world_of(server, owner) {
        let changes = world.add_visible_object(owner, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
    }
}

/// `Npc.StopMovement()`. `CurrentGameStance` / `CurrentAlertness` are npc-subsystem state that is
/// not tracked yet: their C# defaults (`Combat`, `Idle`) are written.
pub fn npc_stop_movement(server: &GameServer, npc: &WorldObject) {
    let tr = npc.transform();
    let in_battle = npc.with_unit(|u| u.unit().is_in_battle).unwrap_or(false);
    let (_, _, rz) = roll_pitch_yaw_sbytes_movement(tr.rot_x, tr.rot_y, tr.rot_z);
    let um = UnitMove {
        x: tr.x,
        y: tr.y,
        z: tr.z,
        rotation_z: rz,
        stance: GameStanceType::COMBAT,
        alertness: MoveTypeAlertness::IDLE,
        ..UnitMove::default()
    };
    let mut mt = MoveType::new(MoveKind::Unit(um));
    mt.flags = if in_battle {
        MoveTypeFlags::STOPPING | MoveTypeFlags::IN_COMBAT
    } else {
        MoveTypeFlags::STOPPING
    };
    mt.time = ms_of_day();
    broadcast(
        server,
        npc,
        &SCOneUnitMovementPacket {
            obj_id: npc.obj_id(),
            move_type: mt,
        },
        false,
    );
}

#[cfg(test)]
mod tests {
    use super::super::data::BuffTemplate;
    use super::*;
    use crate::data::GameData;
    use crate::models::npc::Npc;
    use crate::opcodes::sc;
    use crate::test_support::*;
    use crate::world::{Entity, NpcRef, PlayerRef};

    fn sc_template(kind: SkillControllerKind, values: &[(usize, i32)]) -> SkillControllerTemplate {
        let mut t = SkillControllerTemplate {
            id: 77,
            kind_id: kind as u32,
            ..SkillControllerTemplate::default()
        };
        for &(i, v) in values {
            t.value[i] = v;
        }
        t
    }

    fn close(a: f32, b: f32) -> bool {
        (a - b).abs() < 1e-3
    }

    #[test]
    fn leap_end_position_pulls_in_front_of_target() {
        // offset -2 m: lands 2 m short of the target, on the owner -> target line.
        let t = sc_template(SkillControllerKind::Leap, &[(2, 1000), (3, -2000)]);
        let l = LinearMove::leap(&t, Vec3::new(0.0, 0.0, 0.0), Vec3::new(10.0, 0.0, 5.0));
        assert!(close(l.end_position.x, 8.0) && close(l.end_position.y, 0.0));
        assert_eq!(l.end_position.z, 5.0);
        let expected = (64.0f32 + 25.0).sqrt();
        assert!(
            close(l.calculated_speed, expected),
            "{}",
            l.calculated_speed
        );
        // no duration -> 0.5 s
        let t = sc_template(SkillControllerKind::Leap, &[(3, -2000)]);
        let l = LinearMove::leap(&t, Vec3::default(), Vec3::new(10.0, 0.0, 0.0));
        assert!(close(l.calculated_speed, 16.0));
    }

    #[test]
    fn leap_direction_constraints() {
        let owner = Vec3::new(0.0, 0.0, 0.0);
        let target = Vec3::new(1.0, 0.0, 0.0);
        // ForwardOnly landing farther from the target than the owner -> stays put.
        let t = sc_template(SkillControllerKind::Leap, &[(3, 5000), (6, 1)]);
        let l = LinearMove::leap(&t, owner, target);
        assert_eq!(l.direction, MoveDirection::ForwardOnly);
        assert_eq!(l.end_position, owner);
        assert_eq!(l.calculated_speed, 0.0);
        // Both: unconstrained
        let t = sc_template(SkillControllerKind::Leap, &[(3, 5000)]);
        assert!(close(
            LinearMove::leap(&t, owner, target).end_position.x,
            6.0
        ));
        // BackwardOnly landing closer -> stays put; farther -> allowed.
        let end = Vec3::new(0.5, 0.0, 0.0);
        let far = Vec3::new(-3.0, 0.0, 0.0);
        assert_eq!(
            apply_direction_constraint(MoveDirection::BackwardOnly, owner, end, target),
            owner
        );
        assert_eq!(
            apply_direction_constraint(MoveDirection::BackwardOnly, owner, far, target),
            far
        );
        assert_eq!(
            apply_direction_constraint(MoveDirection::ForwardOnly, owner, end, target),
            end
        );
    }

    #[test]
    fn dash_end_position_and_backward_flip() {
        let owner = Vec3::new(0.0, 0.0, 3.0);
        let target = Vec3::new(10.0, 0.0, 0.0);
        let t = sc_template(SkillControllerKind::Dash, &[(2, 500), (3, 4000)]);
        let d = LinearMove::dash(&t, owner, target);
        assert!(close(d.end_position.x, 4.0) && close(d.end_position.y, 0.0));
        assert_eq!(d.end_position.z, 3.0, "dash keeps the owner's height");
        assert!(close(d.calculated_speed, 8.0));
        let t = sc_template(SkillControllerKind::Dash, &[(2, 500), (3, 4000), (6, 2)]);
        let d = LinearMove::dash(&t, owner, target);
        assert!(close(d.end_position.x, -4.0) && d.end_position.y.abs() < 1e-3);
    }

    #[test]
    fn floating_pull_and_lift_modes() {
        let f = FloatingMove::new(
            None,
            Vec3::new(0.0, 0.0, 0.0),
            Vec3::new(0.0, 10.0, 4.0),
            0.0,
            0.0,
            0.0,
            0.0,
        );
        assert!(!f.is_lift_mode);
        assert_eq!(f.speed, 5.0);
        let pull = (100.0f32 + 16.0).sqrt() - 1.5;
        assert!(close(f.pull_distance, pull));
        assert!(close(f.end_position.y, pull) && f.end_position.x.abs() < 1e-4);
        assert_eq!(f.end_position.z, 0.0);
        // template speed (Value[0] mm/s) and psychokinesis speed override
        let t = sc_template(SkillControllerKind::Floating, &[(0, 8000)]);
        let f = FloatingMove::new(
            Some(&t),
            Vec3::default(),
            Vec3::new(5.0, 0.0, 0.0),
            0.0,
            0.0,
            0.0,
            0.0,
        );
        assert_eq!(f.speed, 8.0);
        let f = FloatingMove::new(
            Some(&t),
            Vec3::default(),
            Vec3::new(5.0, 0.0, 0.0),
            12.0,
            0.0,
            0.0,
            0.0,
        );
        assert_eq!(f.speed, 12.0);
        // too close: no pull
        let f = FloatingMove::new(
            None,
            Vec3::default(),
            Vec3::new(1.0, 0.0, 0.0),
            0.0,
            0.0,
            0.0,
            0.0,
        );
        assert_eq!(f.pull_distance, 0.0);
        assert_eq!(f.end_position, Vec3::default());
        // lift
        let f = FloatingMove::new(
            None,
            Vec3::default(),
            Vec3::new(5.0, 0.0, 0.0),
            0.0,
            5.0,
            0.0,
            2.0,
        );
        assert!(f.is_lift_mode);
        assert_eq!((f.lift_speed, f.speed, f.lift_duration), (3.0, 3.0, 2.0));
    }

    #[test]
    fn heading_velocity_and_rotation() {
        // facing +x: velocity (4000, 0), yaw -90 deg
        let (yaw, vx, vy) = heading(Vec3::default(), Vec3::new(5.0, 0.0, 0.0));
        assert_eq!((vx, vy), (4000, 0));
        assert!(close(yaw, -std::f32::consts::FRAC_PI_2));
        let (_, _, rz) = roll_pitch_yaw_sbytes_movement(0.0, 0.0, yaw);
        assert_eq!(rz, convert_radian_to_direction(f64::from(yaw - TWO_PI)));
        // facing +y: velocity (0, 4000), yaw 0
        let (yaw, vx, vy) = heading(Vec3::default(), Vec3::new(0.0, 5.0, 0.0));
        assert_eq!((vx, vy), (0, 4000));
        assert!(yaw.abs() < 1e-6);
        assert_eq!(roll_pitch_yaw_sbytes_movement(0.0, 0.0, 0.0), (0, 0, 0));
    }

    #[test]
    fn kinds_without_controller() {
        assert_eq!(
            SkillControllerKind::from_u32(4),
            Some(SkillControllerKind::Dash)
        );
        assert_eq!(SkillControllerKind::from_u32(9), None);
        assert_eq!(MoveDirection::from_i32(7), MoveDirection::Both);
    }

    const PLAYER_OBJ: u32 = 0x100;
    const NPC_OBJ: u32 = 0x200;

    fn setup() -> (Arc<GameServer>, TestClient, PlayerRef, NpcRef) {
        let server = Arc::new(test_server_with(GameData::empty()));
        super::super::bind(&server);
        let client = TestClient::new(&server, 1, 1);
        let c = test_character(1, "Viewer", 100.0, 100.0);
        let player = spawn_player(&server, &client, c, PLAYER_OBJ);
        let mut n = Npc::new(1234);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(105.0, 100.0, 100.0);
        let npc = Entity::new(NPC_OBJ, n);
        server
            .world
            .world(0)
            .expect("world")
            .spawn(&npc, &server.view_ctx());
        (server, client, player, npc)
    }

    async fn wait_until(mut f: impl FnMut() -> bool) {
        for _ in 0..300 {
            if f() {
                return;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    }

    #[tokio::test]
    async fn dash_moves_npc_and_broadcasts_movement() {
        let (server, mut client, player, npc) = setup();
        client.drain();
        let owner = WorldObject::from(&npc);
        // 3 m dash in 500 ms towards the player (at x = 100).
        let t = sc_template(SkillControllerKind::Dash, &[(2, 500), (3, 3000)]);
        let sc = create(
            &server,
            &t,
            &owner,
            &Target::Obj(WorldObject::from(&player)),
            0.0,
            0.0,
            0.0,
            0.0,
        )
        .expect("dash controller");
        let Behavior::Dash(d) = sc.behavior() else {
            panic!("not a dash");
        };
        assert!(close(d.end_position.x, 102.0) && close(d.end_position.y, 100.0));
        activate(&server, &sc);
        assert_eq!(sc.state(), ScState::Running);
        assert_eq!(active(&owner), Some(sc.clone()));
        wait_until(|| sc.state() == ScState::Ended).await;
        assert_eq!(sc.state(), ScState::Ended);
        let tr = npc.data().unit.transform;
        assert!(tr.x < 102.6 && tr.x >= 101.99, "x = {}", tr.x);
        assert!(close(tr.y, 100.0));
        // facing -x: yaw = 180 - 90 deg
        assert!(close(tr.rot_z, std::f32::consts::FRAC_PI_2));
        assert_eq!(active(&owner), None, "the controller cleared itself");
        let frames = client.drain();
        let moves: Vec<&Vec<u8>> = frames
            .iter()
            .filter(|(op, _)| *op == sc::SCOneUnitMovementPacket)
            .map(|(_, b)| b)
            .collect();
        assert!(moves.len() >= 2, "{} movement packets", moves.len());
        // body: bc obj id, type 1 (unit), time, flags Moving
        let body = moves[0];
        assert_eq!(&body[..3], &[0x00, 0x02, 0x00]);
        assert_eq!(body[3], 1);
        assert_eq!(body[8], MoveTypeFlags::MOVING.0);
    }

    #[tokio::test]
    async fn replacing_ends_the_previous_controller() {
        let (server, _client, player, npc) = setup();
        let owner = WorldObject::from(&npc);
        let target = Target::Obj(WorldObject::from(&player));
        let t = sc_template(SkillControllerKind::Dash, &[(2, 60_000), (3, 3000)]);
        let first = create(&server, &t, &owner, &target, 0.0, 0.0, 0.0, 0.0).expect("sc");
        activate(&server, &first);
        let second = create(&server, &t, &owner, &target, 0.0, 0.0, 0.0, 0.0).expect("sc");
        second.set_source_buff_id(5);
        activate(&server, &second);
        assert_eq!(first.state(), ScState::Ended);
        assert_eq!(second.state(), ScState::Running);
        assert_eq!(active(&owner), Some(second.clone()));
        second.end(&server, false);
        assert_eq!(active(&owner), None);
    }

    #[tokio::test]
    async fn lift_then_fall_back_to_start_height() {
        let (server, _client, player, npc) = setup();
        let owner = WorldObject::from(&npc);
        let sc = new_floating(
            &server,
            None,
            &owner,
            &Target::Obj(WorldObject::from(&player)),
            0.0,
            1.0,
            20.0,
            0.0,
        );
        activate(&server, &sc);
        wait_until(|| npc.data().unit.transform.z >= 101.0).await;
        assert!(close(npc.data().unit.transform.z, 101.0));
        // dispel path: End() switches to the fall phase instead of ending.
        sc.end(&server, false);
        assert_eq!(sc.state(), ScState::Running);
        assert!(matches!(sc.behavior(), Behavior::Floating(f) if f.is_falling));
        assert_eq!(active(&owner), Some(sc.clone()));
        wait_until(|| sc.state() == ScState::Ended).await;
        assert_eq!(sc.state(), ScState::Ended);
        assert_eq!(npc.data().unit.transform.z, 100.0);
        assert_eq!(active(&owner), None);
    }

    #[tokio::test]
    async fn psychokinesis_buff_starts_and_dispel_ends_the_pull() {
        const BUFF: u32 = 60_001;
        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: BUFF,
            kind: 2,
            duration: 60_000,
            stack_rule: 1,
            psychokinesis: true,
            psychokinesis_speed: 2.0,
            ..BuffTemplate::default()
        });
        let server = Arc::new(test_server_with(data));
        super::super::bind(&server);
        let client = TestClient::new(&server, 1, 1);
        let player = spawn_player(
            &server,
            &client,
            test_character(1, "Puller", 100.0, 100.0),
            PLAYER_OBJ,
        );
        let mut n = Npc::new(1);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(105.0, 100.0, 100.0);
        let npc = Entity::new(NPC_OBJ, n);
        server
            .world
            .world(0)
            .expect("world")
            .spawn(&npc, &server.view_ctx());
        let owner = WorldObject::from(&npc);
        buffs::add_buff_by_id(&server, &owner, BUFF, &WorldObject::from(&player), 0);
        let sc = active(&owner).expect("pull controller started by the buff");
        assert_eq!(sc.kind(), SkillControllerKind::Floating);
        assert_eq!(sc.source_buff_id(), BUFF);
        assert_eq!(sc.state(), ScState::Running);
        let Behavior::Floating(f) = sc.behavior() else {
            unreachable!()
        };
        assert!(close(f.end_position.x, 101.5) && close(f.speed, 2.0));
        wait_until(|| npc.data().unit.transform.x < 104.9).await;
        let index = npc.data().unit.buffs.effects[0].index;
        buffs::exit(&server, &owner, index, false);
        assert_eq!(sc.state(), ScState::Ended);
        assert_eq!(active(&owner), None);
    }
}
