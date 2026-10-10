//! P2b work group `tri`: linear unprojection cells with a triangle as A, and their swap wrappers.
//!
//! | cell | x64 | x86 | port |
//! |---|---|---|---|
//! | `[0][tri][box]` | 0x3516FDB0 (6063 B) | 0x35177810 | [`unproject_tri_box`] |
//! | `[0][box][tri]` | 0x35171570 (335 B wrapper) | 0x351792D0 | `swap_linear` → tri–box |
//! | `[0][tri][sph]` | 0x35175490 (4544 B) | 0x3517DB70 | [`unproject_tri_sph`] |
//! | `[0][sph][tri]` | 0x35176660 (335 B wrapper) | 0x3517ED40 | `swap_linear` → tri–sph |
//! | `[0][tri][tri]` | 0x3516EAE0 (4804 B) | 0x35176460 | [`unproject_tri_tri`] |
//! | `[0][tri][ray]` | 0x35176B40 (4075 B) | 0x3517F190 | [`unproject_tri_ray`] |
//! | `[0][ray][tri]` | 0x35177B40 (335 B wrapper) | 0x351802D0 | `swap_linear` → tri–ray |
//!
//! Shared conventions of these functions:
//! * The triangle primitive is `{pt[3], n}`; the stored normal `n` is used as given (never
//!   recomputed or normalised), so an inconsistent normal changes the results the same way it
//!   does in the binary.
//! * The best `t` is kept as a rational `num/den` in double precision and candidates are merged
//!   with branch-free blends `(1−s)·old + s·new` (`s ∈ {0, 1}` converted to double) after a sign
//!   test on a float-rounded cross difference; see `sel` and `sel_feat`.
//! * `INC_MOD3` (0x3520A3A0) gives the next vertex of an edge.
//! * None of the four functions reads the input feature ids, the contact-area pointer or
//!   `tmax`; only tri–sph reads `tmin` (as the start value when the face test fails). They call
//!   no helpers, use no static state and never write `params`. Nothing is written to the result
//!   when they return 0. The 335-byte wrappers (identical code, verified against
//!   [`swap_linear`]) forward the area pointer, which the callees ignore.

mod boxx;
mod ray;
mod sph;
#[allow(clippy::module_inception)]
mod tri;

pub use boxx::unproject_tri_box;
pub use ray::unproject_tri_ray;
pub use sph::unproject_tri_sph;
pub use tri::unproject_tri_tri;

use super::common::{as_box, as_ray, as_sph, as_tri, swap_linear};
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

/// `((bits >> 30 (arithmetic)) & ~1) + 1`: +1 or −1 from the sign bit of a float.
#[inline]
fn sign_pm1(x: f32) -> i32 {
    ((x.to_bits() as i32 >> 30) & !1) + 1
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

fn cell_tri_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_box(p, as_tri(a), fa, as_box(b), fb, out)
}

/// 0x35171570: box–tri, the 335-byte linear swap wrapper around tri–box.
fn cell_box_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    swap_linear(p, out, |p, out| {
        unproject_tri_box(p, as_tri(b), fb, as_box(a), fa, out)
    })
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

fn cell_tri_tri(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_tri_tri(p, as_tri(a), fa, as_tri(b), fb, out)
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
    t.set(MODE_LINEAR, GeomType::TriMesh, GeomType::Box, cell_tri_box);
    t.set(MODE_LINEAR, GeomType::Box, GeomType::TriMesh, cell_box_tri);
    t.set(
        MODE_LINEAR,
        GeomType::TriMesh,
        GeomType::Sphere,
        cell_tri_sph,
    );
    t.set(
        MODE_LINEAR,
        GeomType::Sphere,
        GeomType::TriMesh,
        cell_sph_tri,
    );
    t.set(
        MODE_LINEAR,
        GeomType::TriMesh,
        GeomType::TriMesh,
        cell_tri_tri,
    );
    t.set(MODE_LINEAR, GeomType::TriMesh, GeomType::Ray, cell_tri_ray);
    t.set(MODE_LINEAR, GeomType::Ray, GeomType::TriMesh, cell_ray_tri);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::{BoxPrim, RayPrim, SpherePrim, Triangle};
    use crate::math::Vec3;

    fn tri_z(z: f32, s: f32) -> Triangle {
        Triangle {
            pt: [
                Vec3::new(-s, -s, z),
                Vec3::new(2.0 * s, -s, z),
                Vec3::new(-s, 2.0 * s, z),
            ],
            n: Vec3::Z,
        }
    }

    fn params(dir: Vec3) -> UnprojParams {
        UnprojParams {
            dir,
            ..UnprojParams::default()
        }
    }

    #[test]
    fn tri_sph_face() {
        let mut p = params(Vec3::new(0.0, 0.0, -1.0));
        let mut out = UnprojResult::default();
        let s = SpherePrim {
            center: Vec3::new(0.25, 0.25, 0.5),
            r: 1.0,
        };
        assert_eq!(
            unproject_tri_sph(&mut p, &tri_z(0.0, 1.0), -1, &s, -1, &mut out),
            1
        );
        assert_eq!(out.t, 0.5);
        assert_eq!(out.pt, Vec3::new(0.25, 0.25, -0.5));
        assert_eq!(out.n, Vec3::Z);
        assert_eq!(out.i_feature, [0x40, 0x40]);
    }

    #[test]
    fn tri_ray_endpoint() {
        let mut p = params(Vec3::Z);
        let mut out = UnprojResult::default();
        let r = RayPrim {
            origin: Vec3::new(0.25, 0.25, 1.0),
            dir: Vec3::new(0.0, 0.0, -2.0),
        };
        assert_eq!(
            unproject_tri_ray(&mut p, &tri_z(0.0, 1.0), -1, &r, -1, &mut out),
            1
        );
        assert_eq!(out.t, 1.0);
        assert_eq!(out.pt, r.origin);
        assert_eq!(out.i_feature, [0x40, 0x80]);
    }

    #[test]
    fn tri_box_face_and_wrapper() {
        let b = BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0));
        let t = tri_z(0.75, 4.0);
        let mut p = params(Vec3::new(0.0, 0.0, -1.0));
        let mut out = UnprojResult::default();
        assert_eq!(unproject_tri_box(&mut p, &t, -1, &b, -1, &mut out), 1);
        assert_eq!(out.t, 1.75);
        assert_eq!(out.pt, Vec3::splat(-1.0));
        assert_eq!(out.i_feature, [0x40, 0]);
        // box–tri through the table: same contact seen from the box, moved back along dir
        let tables = UnprojTables::build();
        let mut p = params(Vec3::Z);
        let mut out = UnprojResult::default();
        let r = tables.unproject(
            MODE_LINEAR,
            &mut p,
            &Primitive::Box(b),
            -1,
            &Primitive::Triangle(t),
            -1,
            &mut out,
            None,
        );
        assert_eq!(r, 1);
        assert_eq!(p.dir, Vec3::Z);
        assert_eq!(out.t, 1.75);
        assert_eq!(out.pt, Vec3::new(-1.0, -1.0, 0.75));
        assert_eq!(out.n, Vec3::new(-0.0, -0.0, -1.0));
        assert_eq!(out.i_feature, [0, 0x40]);
    }

    #[test]
    fn tri_tri_vertex_face() {
        // B pokes 0.5 below A's plane with one vertex; A moves up (+z) along its normal side
        let a = tri_z(0.0, 2.0);
        let b = Triangle {
            pt: [
                Vec3::new(0.0, 0.0, -0.5),
                Vec3::new(0.5, 0.0, 3.0),
                Vec3::new(0.0, 0.5, 3.0),
            ],
            n: Vec3::X,
        };
        let mut p = params(Vec3::new(0.0, 0.0, -1.0));
        let mut out = UnprojResult::default();
        assert_eq!(unproject_tri_tri(&mut p, &a, -1, &b, -1, &mut out), 1);
        assert_eq!(out.t, 0.5);
        assert_eq!(out.pt, Vec3::new(0.0, 0.0, -0.5));
        assert_eq!(out.n, Vec3::Z);
        assert_eq!(out.i_feature, [0x40, 0x80]);
    }
}
