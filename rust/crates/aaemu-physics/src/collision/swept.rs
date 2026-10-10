//! **P2b — unprojection / continuous collision.**
//!
//! The UNPROJECT dispatch table of the binary (x64 0x35209FA0, x86 0x352045D8): `[2][8][8]`
//! function pointers indexed `mode·64 + typeA·8 + typeB` with the [`GeomType`] ids (box 0, tri 1,
//! hf 2, ray 3, sph 4, cyl 5, cap 6, vox 7). Mode 0 is linear unprojection (move A along
//! `params.dir` by the smallest `t` that separates A from B, or with `dir = 0` find the
//! least-penetration direction and write it back), mode 1 is rotational unprojection (rotate A
//! about `params.center`/`params.dir` by the smallest angle; the result is `(sin φ, cos φ)`).
//! `geometry.md` §4.6, §5.
//!
//! Every cell has the C signature
//! `int f(unproj_params*, primA*, int iFeatA, primB*, int iFeatB, unproj_result*, geom_contact_area*)`;
//! [`UnprojFn`] is its Rust form. The params are **mutable**: functions write the chosen
//! direction back, swap wrappers negate it temporarily, and the capsule compositions use `tmin`
//! as a running bound. Results are written even on failure in some cells (the compositions read
//! them), so ports reproduce every write, not just the successful outputs.
//!
//! Work split (one file per group under `swept/`, each with a `fill` that sets its own cells):
//!
//! | file | cells (x64) |
//! |---|---|
//! | [`common`] | swap wrappers (335 B linear, 215 B linear + area, 447 B rotational) and shared helpers |
//! | [`sph`] | sph–sph 0x35178110 |
//! | [`box_box`] | box–box 0x3517CD60 |
//! | [`box_cyl`] | box–cyl 0x351801A0 (+ cyl–box wrapper 0x35185AA0) |
//! | [`cyl_cyl`] | cyl–cyl 0x351893C0 |
//! | [`box_round`] | box–sph 0x35185B80, ray–box 0x35178540, ray–sph 0x3517BDA0 (+ wrappers) |
//! | [`tri`] | tri–box 0x3516FDB0, tri–tri 0x3516EAE0, tri–ray 0x35176B40, tri–sph 0x35175490 (+ wrappers) |
//! | [`tri_cyl`] | tri–cyl 0x351716D0 (+ wrapper) |
//! | [`ray_cyl`] | ray–cyl 0x35179650, cyl–sph 0x35187780 (+ wrappers) |
//! | [`poly`] | polynomial root finders 0x35149170 / 0x3514A2E0 / 0x35148830, rot. ray–sph 0x351A7CC0 |
//! | [`rot_tri`] | rotational tri–tri 0x3519A730, ray–tri 0x3519D340 (+ wrappers) |
//! | [`rot_box`] | rotational ray–box 0x3519EF90 (+ wrapper) |
//! | [`rot_cyl`] | rotational ray–cyl 0x351A0C10 (+ wrapper) |
//! | [`rot_cap`] | rotational ray–cap 0x351A4630 (+ wrapper) |
//! | [`capsule`] | the capsule compositions (box–cap, tri–cap, ray–cap, sph–cap, cap–sph, cyl–cap, cap–cap, …) |
//! | [`sweep`] | `RegisterIntersection` (CGeometry slot 54) and the sweep paths of Intersect |
//!
//! Test support for all groups (oracle probe, case encoding, goldens): [`probe`].
//!
//! # Status and fidelity
//!
//! All 46 non-empty cells of the binary's table are ported (21 unique linear routines,
//! 6 unique rotational routines, the capsule compositions and the 335/215/447/449-byte swap
//! wrappers), plus the polynomial solvers, `PreparePrimitive` (slot 48) for the primitive
//! geometries, the sweep path of the primitive `Intersect` and `RegisterIntersection`. Each
//! cell is checked bit for bit — every word of params, result and contact area, also on failure
//! — against the DLL through `physoracle --p2b` (goldens in `tests/golden/p2b_*.trace`, plus
//! ≥ 50 000–1 000 000 live cases per cell during development).
//!
//! Known, documented deviations (all outside what engine callers produce or read):
//! * `cos` of the result for sph–cap, cyl–cap, cap–cap and their wrappers: copied by the original
//!   from an uninitialised stack temporary; the port writes 0 (linear callers never read it).
//! * `RegisterIntersection` reads two uninitialised `unproj_params` slots (`tmax` on the
//!   penetration fallback, `minPtDist` always); the caller supplies them
//!   ([`sweep::StaleUnprojSlots`]).
//! * Area writers that ignore `nmaxpt` (box–box, box–cyl, cyl–cyl) grow the caller's buffers
//!   instead of overrunning them; entries below `nmaxpt` are identical.
//! * NaN *inputs* in tri–cyl: SSE returns the first NaN operand of a commutative op, LLVM may
//!   swap operands, so NaN sign bits can differ (≈0.4 % of all-NaN-input cases).
//! * The double CRT routines the cubic solver calls are ported in `poly/crt.rs` except the
//!   `|x| ≥ 500000` reduction of `sin`/`cos`, which the solver never reaches.

pub mod box_box;
pub mod box_cyl;
pub mod box_round;
pub mod capsule;
pub mod common;
pub mod cyl_cyl;
pub mod poly;
pub mod probe;
pub mod ray_cyl;
pub mod rot_box;
pub mod rot_cap;
pub mod rot_cyl;
pub mod rot_tri;
pub mod sph;
pub mod sweep;
pub mod tri;
pub mod tri_cyl;

use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::Primitive;
use crate::ids::GeomType;

/// An UNPROJECT cell:
/// `int f(unproj_params*, primA*, iFeatA, primB*, iFeatB, unproj_result*, geom_contact_area*)`.
pub type UnprojFn = fn(
    &mut UnprojParams,
    &Primitive,
    i32,
    &Primitive,
    i32,
    &mut UnprojResult,
    Option<&mut ContactArea>,
) -> i32;

/// The default cell (`ret0`): no unprojection.
pub fn unproject_none(
    _p: &mut UnprojParams,
    _a: &Primitive,
    _fa: i32,
    _b: &Primitive,
    _fb: i32,
    _out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    0
}

/// Mode-0 (linear) plane of the table.
pub const MODE_LINEAR: usize = 0;
/// Mode-1 (rotational) plane of the table.
pub const MODE_ROTATIONAL: usize = 1;

/// One plane of the table (`[typeA][typeB]`).
pub type UnprojPlane = [[UnprojFn; GeomType::COUNT]; GeomType::COUNT];

/// The UNPROJECT table (`[mode][typeA][typeB]`). Every group module exposes
/// `pub fn fill(t: &mut UnprojTables)` and writes only the cells it owns.
#[derive(Clone, Copy)]
pub struct UnprojTables {
    pub t: [UnprojPlane; 2],
}

impl UnprojTables {
    /// The table as the binary ships it, assembled from the groups.
    #[must_use]
    pub fn build() -> Self {
        let mut t = UnprojTables {
            t: [[[unproject_none as UnprojFn; GeomType::COUNT]; GeomType::COUNT]; 2],
        };
        sph::fill(&mut t);
        box_box::fill(&mut t);
        box_cyl::fill(&mut t);
        cyl_cyl::fill(&mut t);
        box_round::fill(&mut t);
        tri::fill(&mut t);
        tri_cyl::fill(&mut t);
        ray_cyl::fill(&mut t);
        poly::fill(&mut t);
        rot_tri::fill(&mut t);
        rot_box::fill(&mut t);
        rot_cyl::fill(&mut t);
        rot_cap::fill(&mut t);
        capsule::fill(&mut t);
        t
    }

    /// The cell for `mode` (0 linear, 1 rotational) and a pair of types.
    #[must_use]
    pub fn get(&self, mode: usize, a: GeomType, b: GeomType) -> UnprojFn {
        self.t[mode][a.id() as usize][b.id() as usize]
    }

    /// Set a cell (used by the group `fill` functions).
    pub fn set(&mut self, mode: usize, a: GeomType, b: GeomType, f: UnprojFn) {
        self.t[mode][a.id() as usize][b.id() as usize] = f;
    }

    /// Dispatch like the engine: `UNPROJECT[mode][tA][tB](params, a, fa, b, fb, out, area)`.
    #[allow(clippy::too_many_arguments)]
    pub fn unproject(
        &self,
        mode: usize,
        p: &mut UnprojParams,
        a: &Primitive,
        fa: i32,
        b: &Primitive,
        fb: i32,
        out: &mut UnprojResult,
        area: Option<&mut ContactArea>,
    ) -> i32 {
        (self.get(mode, a.geom_type(), b.geom_type()))(p, a, fa, b, fb, out, area)
    }
}

/// A contact-area buffer of `nmaxpt` entries (what the callers take from the per-thread pool of
/// 32 areas × 256 points): `npt = 0`, arrays zeroed.
#[must_use]
pub fn new_area(nmaxpt: usize) -> ContactArea {
    ContactArea {
        min_edge: 0.0,
        prim: [vec![0; nmaxpt], vec![0; nmaxpt]],
        feature: [vec![0; nmaxpt], vec![0; nmaxpt]],
        pt: vec![crate::math::Vec3::ZERO; nmaxpt],
        i_type: 0,
        npt: 0,
        nmaxpt: nmaxpt as i32,
        n: crate::math::Vec3::ZERO,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The set of non-`ret0` cells matches the binary's table (`geometry.md` §4.6): every pair
    /// of {box, tri, ray, sph, cyl, cap} except ray–ray in mode 0; ray against everything plus
    /// tri–tri in mode 1.
    #[test]
    fn table_cells_match_the_binary() {
        let t = UnprojTables::build();
        let prims = [0usize, 1, 3, 4, 5, 6];
        let mut n = 0;
        for mode in 0..2 {
            for a in 0..GeomType::COUNT {
                for b in 0..GeomType::COUNT {
                    let want = match mode {
                        0 => prims.contains(&a) && prims.contains(&b) && !(a == 3 && b == 3),
                        _ => {
                            (a == 1 && b == 1)
                                || (a == 3 && prims.contains(&b) && b != 3)
                                || (b == 3 && prims.contains(&a) && a != 3)
                        }
                    };
                    let have = !std::ptr::eq(
                        t.t[mode][a][b] as *const (),
                        unproject_none as *const (),
                    );
                    assert_eq!(have, want, "cell [{mode}][{a}][{b}]");
                    n += usize::from(have);
                }
            }
        }
        assert_eq!(n, 46);
    }
}
