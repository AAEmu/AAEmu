//! **P2a — primitive pair functions** (stub). The two 8×8 dispatch tables of the binary,
//! indexed `[typeA][typeB]` by [`crate::ids::GeomType`]:
//!
//! * OVERLAP 0x3520A3E0 — cheap reject, default "maybe overlaps" (`ret1`).
//! * INTERSECT 0x35209D50 — contact generation into [`PrimInters`]; 20 unique functions, the
//!   other non-stub cells are swap wrappers with per-pair fix-ups (`geometry.md` §4.1).
//!   Heightfield and voxel rows/columns are empty: their BVs emit triangles.
//!
//! Work split (one agent per file, see the crate README):
//! * [`box_pairs`] — box–box 0x3516AE00, box–ray, box–sph, box–cyl, box–cap and their wrappers;
//!   OVERLAP rows for box.
//! * [`tri_pairs`] — tri–box 0x35162AA0, tri–tri 0x35159410, tri–sph, ray–tri and wrappers;
//!   OVERLAP tri rows.
//! * [`tri_round_pairs`] — tri–cyl 0x35164830 (14.8 KB), tri–cap 0x35168280 and wrappers.
//! * [`round_pairs`] — sph–sph, sph–ray, cyl–ray, cyl–sph, cyl–cyl, cyl–cap, cap–ray, cap–sph,
//!   cap–cap and wrappers; OVERLAP sph rows.
//!
//! The table type below is the stable interface: each group fills its own cells.

use super::contact::PrimInters;
use crate::geometry::primitives::Primitive;
use crate::ids::GeomType;

pub mod box_pairs;
pub mod round_pairs;
pub mod tri_pairs;
pub mod tri_round_pairs;

/// An INTERSECT cell: `int f(primA*, primB*, prim_inters* out)`; returns non-zero on contact.
pub type IntersectFn = fn(&Primitive, &Primitive, &mut PrimInters) -> bool;
/// An OVERLAP cell: `int f(primA*, primB*)` (the scratch argument is internal).
pub type OverlapFn = fn(&Primitive, &Primitive) -> bool;

/// OVERLAP default (`ret1`): "maybe overlaps".
#[must_use]
pub fn overlap_maybe(_a: &Primitive, _b: &Primitive) -> bool {
    true
}

/// INTERSECT default (`ret0`): no contact.
#[must_use]
pub fn intersect_none(_a: &Primitive, _b: &Primitive, _out: &mut PrimInters) -> bool {
    false
}

/// INTERSECT table type (`[typeA][typeB]`).
pub type IntersectTable = [[IntersectFn; GeomType::COUNT]; GeomType::COUNT];
/// OVERLAP table type (`[typeA][typeB]`).
pub type OverlapTable = [[OverlapFn; GeomType::COUNT]; GeomType::COUNT];

/// Both tables. Every group module exposes `pub fn fill(t: &mut PairTables)` and writes only the
/// cells it owns, so the groups never edit this file.
#[derive(Clone, Copy)]
pub struct PairTables {
    pub intersect: IntersectTable,
    pub overlap: OverlapTable,
}

impl PairTables {
    /// The tables as the binary ships them, assembled from the four groups.
    #[must_use]
    pub fn build() -> Self {
        let mut t = PairTables {
            intersect: [[intersect_none as IntersectFn; GeomType::COUNT]; GeomType::COUNT],
            overlap: [[overlap_maybe as OverlapFn; GeomType::COUNT]; GeomType::COUNT],
        };
        box_pairs::fill(&mut t);
        tri_pairs::fill(&mut t);
        tri_round_pairs::fill(&mut t);
        round_pairs::fill(&mut t);
        t
    }

    /// INTERSECT cell for a pair of types.
    #[must_use]
    pub fn intersect_fn(&self, a: GeomType, b: GeomType) -> IntersectFn {
        self.intersect[a.id() as usize][b.id() as usize]
    }

    /// OVERLAP cell for a pair of types.
    #[must_use]
    pub fn overlap_fn(&self, a: GeomType, b: GeomType) -> OverlapFn {
        self.overlap[a.id() as usize][b.id() as usize]
    }
}
