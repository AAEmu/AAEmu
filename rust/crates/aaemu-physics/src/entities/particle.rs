//! **P5 — `CParticleEntity`** (type 5, 0x2A8 bytes; vtable 0x351EEE88; ctor 0x35039D40):
//! `pe_params_particle`, DoStep 0x3503C210. `entities.md` §5.
//!
//! Work in progress: the class hooks below are the entry points the world and the parameter
//! interface dispatch to.

use super::base::{Coords, Entity, EntityKind};
use crate::math::Vec3;
use crate::world::{EntIdx, World};

/// `CParticleEntity` members (`E+448 …`).
#[derive(Clone, Debug, Default)]
pub struct ParticleData {}

/// Constructor 0x35039D40 (on top of the base ctor).
pub fn construct(_w: &World, ent: &mut Entity) {
    ent.kind = EntityKind::Particle(Box::default());
    ent.set_sim_class(4);
}

/// Slot 7 `SetParams` 0x3503B800.
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::set::class_set_params(w, e, buf, ts)
}

/// Slot 8 `GetParams` 0x350398D0.
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::get::class_get_params(w, e, buf)
}

/// Slot 9 `GetStatus` 0x3503A310.
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::status::class_get_status(w, e, buf)
}

/// Slot 10 `Action` 0x3503A750.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::action::class_action(w, e, buf, ts)
}

/// Slot 75 `ComputeBBox` 0x35040F10.
pub fn compute_bbox(w: &mut World, e: EntIdx, flags: u32, ipart: i32, c: Option<Coords>) -> [Vec3; 2] {
    super::base::bbox::base_compute_bbox(w, e, flags, ipart, c)
}

/// Slot 55 `Awake` 0x35039B60.
pub fn awake(_w: &mut World, _e: EntIdx, _b: i32, _src: i32) -> i32 {
    0
}

/// Slot 56 `IsAwake` 0x35039BB0.
#[must_use]
pub fn is_awake(_w: &World, _e: EntIdx) -> i32 {
    0
}

/// Slot 26 `StartStep` 0x35039850.
pub fn start_step(_w: &mut World, _e: EntIdx, _dt: f32) {}

/// Slot 37 `GetMaxTimeStep` 0x35039CE0.
pub fn get_max_time_step(_w: &mut World, _e: EntIdx, dt: f32) -> f32 {
    dt
}

/// Slot 34 `Step` 0x350407E0 → DoStep 0x3503C210.
pub fn step(_w: &mut World, _e: EntIdx, _dt: f32) -> i32 {
    1
}

/// Slot 54 0x350406D0.
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    super::class_detach_hook(w, e, mode);
}
