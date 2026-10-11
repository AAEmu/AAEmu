//! Collision detection between primitives.
//!
//! * [`contact`] — P0 (frozen shapes, filled by P2a/P2b): `geom_contact`, `prim_inters`,
//!   `intersection_params`, `unproj_params`/`unproj_result`, contact areas.
//! * [`pairs`] — **P2a**: the OVERLAP and INTERSECT 8×8 tables and their pair functions.
//! * [`swept`] — **P2b**: the UNPROJECT 2×8×8 table (linear and rotational unprojection),
//!   `RegisterIntersection`, swept tests.
//! * [`prim_intersect`] — the shared primitive `Intersect` (geometry slot 10, 0x35151F20) and
//!   its per-thread contact scratch.

pub mod contact;
pub mod pairs;
pub mod prim_intersect;
pub mod swept;
