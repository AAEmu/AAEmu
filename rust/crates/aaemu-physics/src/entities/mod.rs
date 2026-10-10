//! Physical entities.
//!
//! * [`base`] — **P4a**: `CPhysicalEntity` (parts, SetParams/GetParams/GetStatus/Action base
//!   ids, AddGeometry, bbox).
//! * [`rigid`] — **P4a**: `CRigidEntity` (body integration, Step/RegisterContacts/Update,
//!   ApplyBuoyancy, air resistance).
//! * [`living`], [`wheeled`], [`particle`] — **P5**.
//!
//! Articulated, rope and soft entities are deferred (P6).

pub mod base;
pub mod living;
pub mod particle;
pub mod rigid;
pub mod wheeled;

/// An entity handle (the engine's 23-bit id, `+0x44` low bits). The world (P3) owns the
/// entity store; everything else refers to entities by id.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EntityId(pub i32);
