//! Collision detection between primitives.
//!
//! * [`contact`] — P0 (frozen shapes, filled by P2a/P2b): `geom_contact`, `prim_inters`,
//!   `intersection_params`, `unproj_params`/`unproj_result`, contact areas.
//! * [`pairs`] — **P2a**: the OVERLAP and INTERSECT 8×8 tables and their pair functions.
//! * [`swept`] — **P2b**: the UNPROJECT 2×8×8 table (linear and rotational unprojection),
//!   `RegisterIntersection`, swept tests.

pub mod contact;
pub mod pairs;
pub mod swept;
