//! **P3 — world core** (stub): entity store and ids, deferred command queue, `TimeStep` driver
//! (single-threaded; islands in a stable id order instead of heap-address order), events,
//! queries. Default tuning: [`crate::physvars::PhysVars`]. `world.md` §1–§6.
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
