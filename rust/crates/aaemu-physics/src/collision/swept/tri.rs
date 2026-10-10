//! P2b work group `tri`: linear unprojection cells with a triangle as A, and their swap wrappers.
//!
//! | cell | x64 | x86 | port |
//! |---|---|---|---|
//! | `[0][tri][sph]` | 0x35175490 (4544 B) | 0x3517DB70 | [`unproject_tri_sph`] |
//! | `[0][sph][tri]` | 0x35176660 (335 B wrapper) | 0x3517ED40 | `swap_linear` → tri–sph |
//! | `[0][tri][ray]` | 0x35176B40 (4075 B) | 0x3517F190 | [`unproject_tri_ray`] |
//! | `[0][ray][tri]` | 0x35177B40 (335 B wrapper) | 0x351802D0 | `swap_linear` → tri–ray |
//!
//! Shared conventions of these functions:
//! * The triangle primitive is `{pt[3], n}`; the stored normal `n` is used as given (never
//!   recomputed or normalised), so an inconsistent normal changes the results the same way it
//!   does in the binary.
//! * The best `t` is kept as a rational `num/den` in double precision and candidates are merged
//!   with branch-free blends `(1−s)·old + s·new` (`s ∈ {0, 1}` converted to double) after a sign
//!   test on a float-rounded cross difference; see [`sel`] and [`sel_feat`].
//! * `INC_MOD3` (0x3520A3A0) gives the next vertex of an edge.

mod ray;
mod sph;

pub use ray::unproject_tri_ray;
pub use sph::unproject_tri_sph;

use super::common::{as_ray, as_sph, as_tri, swap_linear};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::Primitive;
use crate::ids::GeomType;

/// `cvtss2sd` / `cvtps2pd`.
#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// Sign bit of a float (`shr 31` on its bits).
#[inline]
fn sgn(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// The binary's blend `(double)(s ^ 1)·old + (double)s·new` (`s` = 0 or 1).
#[inline]
fn sel(s: u32, old: f64, new: f64) -> f64 {
    f64::from(s ^ 1) * old + f64::from(s) * new
}

/// The feature-id blend `((s − 1) & old) | (−s & new)`.
#[inline]
fn sel_feat(s: u32, old: i32, new: i32) -> i32 {
    ((s as i32 - 1) & old) | (-(s as i32) & new)
}

fn cell_tri_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_sph(p, as_tri(a), fa, as_sph(b), fb, out)
}

/// 0x35176660: sph–tri, the 335-byte linear swap wrapper around tri–sph.
fn cell_sph_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    swap_linear(p, out, |p, out| {
        unproject_tri_sph(p, as_tri(b), fb, as_sph(a), fa, out)
    })
}

fn cell_tri_ray(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_ray(p, as_tri(a), fa, as_ray(b), fb, out)
}

/// 0x35177B40: ray–tri, the 335-byte linear swap wrapper around tri–ray.
fn cell_ray_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    swap_linear(p, out, |p, out| {
        unproject_tri_ray(p, as_tri(b), fb, as_ray(a), fa, out)
    })
}

/// Fill the cells this group owns.
pub fn fill(t: &mut UnprojTables) {
    t.set(MODE_LINEAR, GeomType::TriMesh, GeomType::Sphere, cell_tri_sph);
    t.set(MODE_LINEAR, GeomType::Sphere, GeomType::TriMesh, cell_sph_tri);
    t.set(MODE_LINEAR, GeomType::TriMesh, GeomType::Ray, cell_tri_ray);
    t.set(MODE_LINEAR, GeomType::Ray, GeomType::TriMesh, cell_ray_tri);
}
