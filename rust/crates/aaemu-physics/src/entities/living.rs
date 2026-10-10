//! **P5 — `CLivingEntity`** (type 4, 0x468 bytes; vtable 0x351EE1D8; ctor 0x35025530).
//! `entities.md` §4.

use super::base::{Coords, Entity, EntityKind};
use crate::geometry::PhysGeometry;
use crate::math::Vec3;
use crate::params::GeomParams;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `CLivingEntity` members (`E+448 …`).
#[derive(Clone, Debug, Default)]
pub struct LivingData {
    /// `+584` 1/mass.
    pub mass_inv: f32,
}

/// Constructor 0x35025530.
pub fn construct(_w: &World, ent: &mut Entity) {
    ent.kind = EntityKind::Living(Box::default());
    ent.set_sim_class(3);
}

pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::set::class_set_params(w, e, buf, ts)
}
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::get::class_get_params(w, e, buf)
}
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::status::class_get_status(w, e, buf)
}
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::action::class_action(w, e, buf, ts)
}
pub fn compute_bbox(w: &mut World, e: EntIdx, flags: u32, ipart: i32, c: Option<Coords>) -> [Vec3; 2] {
    super::base::bbox::base_compute_bbox(w, e, flags, ipart, c)
}
pub fn add_geometry(w: &mut World, e: EntIdx, g: &Arc<PhysGeometry>, gp: &GeomParams, id: i32) -> i32 {
    super::base::geometry::base_add_geometry(w, e, g, gp, id)
}
pub fn awake(_w: &mut World, _e: EntIdx, _b: i32, _src: i32) -> i32 {
    1
}
pub fn start_step(_w: &mut World, _e: EntIdx, _dt: f32) {}
pub fn get_max_time_step(_w: &mut World, _e: EntIdx, dt: f32) -> f32 {
    dt
}
pub fn step(_w: &mut World, _e: EntIdx, _dt: f32) -> i32 {
    1
}
pub fn update(_w: &mut World, _e: EntIdx, _dt: f32, _damping: f32) -> u32 {
    1
}
pub fn register_contacts(_w: &mut World, _e: EntIdx, _dt: f32, _n: i32) -> i32 {
    0
}
#[must_use]
pub fn calc_energy(_w: &World, _e: EntIdx, _dt: f32) -> f32 {
    0.0
}
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    super::class_detach_hook(w, e, mode);
}
pub fn follow_ground(_w: &mut World, _e: EntIdx, _dt: f32) {}
