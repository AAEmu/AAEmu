//! **P3 — world core**: entity store and ids, deferred command queue, `TimeStep` driver
//! (single-threaded), events, queries. Default tuning: [`crate::physvars::PhysVars`].
//! `world.md` §1–§6.
//!
//! * [`grid`] — entity grid (`SetupEntityGrid`, thunks, `RepositionEntity`, `GetEntitiesInBox`).
//! * [`areas`] — `CPhysArea`, global area, `CheckAreas`.
//! * [`queries`] — `RayWorldIntersection` (incl. the ArcheAge "DrawRayOnGrid" ray-extent limit),
//!   `PrimitiveWorldIntersection`, entity-level queries.
//! * [`events`] — the 15 event types, immediate/logged clients, `PumpLoggedEvents`.

pub mod areas;
pub mod events;
pub mod grid;
pub mod queries;

/// Index of an entity (or area) object in the world's entity store. Stands in for the
/// binary's object pointers; the store never reuses an index while anything may still refer
/// to it.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EntIdx(pub u32);

impl EntIdx {
    /// "null".
    pub const NONE: EntIdx = EntIdx(u32::MAX);

    #[inline]
    #[must_use]
    pub fn is_none(self) -> bool {
        self == Self::NONE
    }
}
