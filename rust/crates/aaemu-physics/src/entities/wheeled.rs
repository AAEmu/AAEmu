//! **P5 — `CWheeledVehicleEntity`** (type 3, 0x2BF8 bytes; vtable 0x351F2388; ctor
//! 0x35104900): rigid Step + slots 78/79 (wheel contacts, suspension and drive impulses).
//! `dynamics.md` §5; `entities.md` §4.5.
//!
//! Work in progress: the class hooks below are the entry points the world and the parameter
//! interface dispatch to.

use super::base::Entity;
use crate::geometry::PhysGeometry;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `CWheeledVehicleEntity` members (`E+1200 …`), held by [`super::rigid::RigidData::wheeled`].
#[derive(Clone, Debug, Default)]
pub struct WheeledData {}

/// Constructor 0x35104900 (after the rigid ctor).
pub fn construct(_w: &World, ent: &mut Entity) {
    if let Some(r) = ent.rigid_mut() {
        r.wheeled = Some(Box::default());
    }
}

/// Slot 7 `SetParams` 0x35106740.
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::set::class_set_params(w, e, buf, ts)
}

/// Slot 8 `GetParams` 0x35104B50.
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::get::class_get_params(w, e, buf)
}

/// Slot 9 `GetStatus` 0x3510A090.
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    super::interface::status::class_get_status(w, e, buf)
}

/// Slot 10 `Action` 0x351094A0.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    super::interface::action::class_action(w, e, buf, ts)
}

/// Slot 11 `AddGeometry` 0x3510BBB0 with a raw `pe_geomparams` image (type 0 or 1).
pub fn add_geometry(w: &mut World, e: EntIdx, g: &Arc<PhysGeometry>, buf: &[u8], id: i32) -> i32 {
    let gp = <crate::params::GeomParams as crate::params::PeStruct>::read(buf);
    super::base::geometry::rigid_add_geometry(w, e, g, &gp, id)
}

/// Slot 37 `GetMaxTimeStep` 0x35104790.
pub fn get_max_time_step(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    super::rigid::update::get_max_time_step(w, e, dt)
}

/// Slot 41 `Update` 0x351050A0.
pub fn update(w: &mut World, e: EntIdx, dt: f32, damping: f32) -> u32 {
    super::rigid::update::update(w, e, dt, damping)
}

/// Slot 40 `RegisterContacts` 0x35105E50.
pub fn register_contacts(w: &mut World, e: EntIdx, dt: f32, n_max: i32) -> i32 {
    super::rigid::update::register_contacts(w, e, dt, n_max)
}

/// Slot 43 `GetDamping` 0x35104FA0.
#[must_use]
pub fn get_damping(w: &World, e: EntIdx, dt: f32) -> f32 {
    super::rigid::update::get_damping(w, e, dt)
}

/// Slot 48 `RemoveCollider` 0x35104840.
pub fn remove_collider(w: &mut World, e: EntIdx, c: EntIdx, always: bool) -> i32 {
    super::class_remove_collider(w, e, c, always)
}

/// Slot 50 0x351047F0.
#[must_use]
pub fn has_contacts_with(w: &World, e: EntIdx, other: EntIdx) -> bool {
    super::class_has_contacts_with(w, e, other)
}

/// Slot 54 0x35104700.
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    super::class_detach_hook(w, e, mode);
}
