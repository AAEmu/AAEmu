//! `CreateMesh`'s mesh → primitive approximation: `CGeomManager` slot 6 (0x35098490), the
//! principal-axes helper sub_350975D0 and the fitter sub_3512D650.
//!
//! `CGeomManager::CreateMesh` (slot 3, 0x3514B710) first calls slot 6 with the strided vertex and
//! `u16` index arrays, the per-triangle materials, `nTris`, the build flags and the
//! approximation tolerance. Slot 6 returns a geometry type and a pointer to a static primitive
//! buffer; any value other than 1 makes `CreateMesh` return slot 7 `CreatePrimitive(type,
//! prim)` instead of building a `CTriMesh`. 1 means "keep the mesh".
//!
//! Slot 6 keeps the mesh when a material array is given and the materials are not all equal
//! **and** `!(tol > 0.5)` — non-uniform materials with `tol > 0.5` still go on to the fitter
//! (quirk kept) — or when `!(tol > 0)`. Otherwise it computes the area-weighted centroid and the
//! surface-covariance eigenvectors (sub_350975D0 — instruction for instruction the same routine
//! as the BV-tree helper sub_35195400, ported as
//! [`crate::geometry::bvtree::fit::principal_axes`]; here with `u16` indices) and runs the
//! fitter, which tries, in this order and only when the flag is set:
//!
//! | flag | primitive (type) | frame tried | static buffer |
//! |---|---|---|---|
//! | 0x800 | cylinder (5) | each principal axis as the cylinder axis | 0x352568F0 |
//! | 0x200000 | capsule (6) | each principal axis | 0x352568F0 |
//! | 0x1000 | box (0) | principal axes, then the world axes | 0x352568B0 |
//! | 0x2000 | sphere (4) | centre = the area-weighted centroid | 0x35256898 |
//!
//! The first candidate whose error measures pass returns. Each candidate gets
//!
//! * `err` = `0.7 ·` (area-weighted mean of the per-triangle error) `+ 0.3 · m`, and
//! * `max` = the maximum per-triangle error, maxed with `m`,
//!
//! where the per-triangle error is the max over its three vertices (and, for the sphere, its
//! plane distance) of `|distance / size − 1|`, and `m` compares the mesh's surface area with
//! the primitive's. It passes when `(double)(tol·1.5f) > max` and `(double)tol > err` (`comisd`,
//! so NaN fails) — except the sphere, which fails only when `max >= tol·1.5` or `err >= tol`
//! (NaN passes).
//!
//! Precision follows the disassembly (Hex-Rays' SSE output is unreliable here): projections,
//! extents, distances and normals in single precision; the per-vertex error `x·(1/size) − 1` is
//! computed in single precision and widened (`cvtps2pd`) before the `|·|` and the double max /
//! sum idioms; the sphere works in double except for its mean-radius pass. Kept quirks:
//!
//! * The candidate axis starts at index 3 and `err[3]` is the slot behind the three-entry error
//!   array, set to 1e10 by the cylinder and capsule passes.
//! * The capsule's cap-region vertex errors are **not** made absolute (only the side region's
//!   are), so cap vertices inside the hemisphere count as 0 through the `max(·, 0)` idiom.
//! * The cylinder and capsule area terms use the triangle area (`|n|/2`), the box uses `|n|`
//!   (and compares with `16·(xy + yz + zx)`); the box skips no degenerate triangle, the others
//!   skip `|n|² == 0` (and NaN).
//! * When the principal-axes box is flat (`sx·sy·sz == 0` or NaN) its error stays 1e10 and the
//!   second box pass reuses the principal axes instead of the world axes (the identity is only
//!   loaded after a non-flat pass).
//! * The box prefers the world axes when `(float)(err_world − err_principal·(double)0.95f)` is
//!   negative (sign bit, so `-0.0` too).

#![allow(clippy::neg_cmp_op_on_partial_ord)] // `!(a > b)` = the binary's NaN-taking branches

use crate::geometry::bvtree::fit::{principal_axes as principal_axes_i32, PrincipalAxes};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use crate::math::{max_idiom, min_idiom, Mat33, Vec3};

/// `GEOM_BOX`.
pub const KIND_BOX: i32 = 0;
/// "Keep the mesh" (`GEOM_TRIMESH`).
pub const KIND_MESH: i32 = 1;
/// `GEOM_SPHERE`.
pub const KIND_SPHERE: i32 = 4;
/// `GEOM_CYLINDER`.
pub const KIND_CYLINDER: i32 = 5;
/// `GEOM_CAPSULE`.
pub const KIND_CAPSULE: i32 = 6;

/// Build flag: try a cylinder.
pub const FLAG_CYLINDER: u32 = 0x800;
/// Build flag: try a box.
pub const FLAG_BOX: u32 = 0x1000;
/// Build flag: try a sphere.
pub const FLAG_SPHERE: u32 = 0x2000;
/// Build flag: try a capsule.
pub const FLAG_CAPSULE: u32 = 0x20_0000;

const PI: f64 = f64::from_bits(0x4009_21FB_5444_2D18);
/// `1/(2π)` (0x351EDBA8).
const INV_2PI: f64 = f64::from_bits(0x3FC4_5F30_6DC9_C883);
/// `1/(4π)` (0x351F27F8).
const INV_4PI: f64 = f64::from_bits(0x3FB4_5F30_6DC9_C883);
const W_MEAN: f64 = f64::from_bits(0x3FE6_6666_6666_6666);
const W_AREA: f64 = f64::from_bits(0x3FD3_3333_3333_3333);
/// `(double)0.95f` (0x351F27E8).
const BOX_WORLD_PREF: f64 = f64::from_bits(0x3FEE_6666_6000_0000);
/// The "no candidate" error (0x351F13D8).
const ERR_NONE: f64 = 1.0e10;
/// Degenerate radius / height threshold (0x351F2810), compared in double.
const EPS_SIZE: f64 = f64::from_bits(0x3EE4_F8B5_88E3_68F1);
/// `0.33333334f`.
const THIRD: f32 = f32::from_bits(0x3EAA_AAAB);
/// `1e-20f` (0x351ED60C).
const EPS20: f32 = f32::from_bits(0x1E3C_E508);

/// Double `max(a, b)` idiom `(|a − b| + (a + b))·0.5`.
#[inline]
fn dmax(a: f64, b: f64) -> f64 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// Double `max(e, 0)` idiom `(|−e| + e)·0.5`.
#[inline]
fn dmax0(e: f64) -> f64 {
    ((-e).abs() + e) * 0.5
}

/// `|(float)(x) − 1|` widened: the per-vertex relative error.
#[inline]
fn rel_err(x: f32) -> f64 {
    f64::from(x - 1.0).abs()
}

/// The strided arrays as the original reads them: vertex `idx[k]` (stride 12, stride 2).
#[derive(Clone, Copy)]
struct Mesh<'a> {
    v: &'a [Vec3],
    i: &'a [u16],
}

impl Mesh<'_> {
    #[inline]
    fn p(&self, k: usize) -> Vec3 {
        self.v[usize::from(self.i[k])]
    }
}

/// Row `k` of the single-precision axes dotted with `v`: `(x·a0 + y·a1) + z·a2`.
#[inline]
fn proj(a: &[f32; 9], k: usize, v: Vec3) -> f32 {
    (v.x * a[3 * k] + v.y * a[3 * k + 1]) + v.z * a[3 * k + 2]
}

/// Min / max of the projections of all `n3` index entries on the three rows (first entry, then
/// the idioms in index order).
fn extents(m: Mesh, n3: usize, a: &[f32; 9]) -> ([f32; 3], [f32; 3]) {
    let v = m.p(0);
    let mut lo = [proj(a, 0, v), proj(a, 1, v), proj(a, 2, v)];
    let mut hi = lo;
    for k in 1..n3 {
        let v = m.p(k);
        for j in 0..3 {
            let p = proj(a, j, v);
            lo[j] = min_idiom(lo[j], p);
            hi[j] = max_idiom(hi[j], p);
        }
    }
    (lo, hi)
}

/// `Σ_k mid_k · row_k` as `(mid0·a[j] + mid1·a[3+j]) + mid2·a[6+j]`.
fn frame_point(a: &[f32; 9], mid: [f32; 3]) -> Vec3 {
    let c = |j: usize| (mid[0] * a[j] + mid[1] * a[3 + j]) + mid[2] * a[6 + j];
    Vec3::new(c(0), c(1), c(2))
}

/// Largest axial coordinate (from 0) and squared radial distance along `n` through `c`.
fn axial(m: Mesh, n3: usize, c: Vec3, n: [f32; 3]) -> (f32, f32) {
    let (mut h, mut r2) = (0.0f32, 0.0f32);
    for k in 0..n3 {
        let d = m.p(k) - c;
        let t = (d.y * n[1] + d.x * n[0]) + d.z * n[2];
        let (ry, rx, rz) = (d.y - n[1] * t, d.x - t * n[0], d.z - n[2] * t);
        let q = (ry * ry + rx * rx) + rz * rz;
        r2 = max_idiom(r2, q);
        h = max_idiom(h, t);
    }
    (h, r2)
}

/// Triangle normal `(v1 − v0) × (v2 − v0)` in the fitter's single-precision form.
fn tri_normal(v0: Vec3, v1: Vec3, v2: Vec3) -> (f32, f32, f32) {
    let e2 = v2 - v0;
    let e1 = v1 - v0;
    (
        e1.y * e2.z - e1.z * e2.y,
        e1.z * e2.x - e2.z * e1.x,
        e2.y * e1.x - e1.y * e2.x,
    )
}

/// What the fitter computed, for tests (the decision values of every pass that ran).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct FitTrace {
    /// Cylinder `err[0..3]` (+ the 1e10 slot `[3]`), `max[0..3]`, chosen axis (3 = none).
    pub cyl_err: [f64; 4],
    pub cyl_max: [f64; 3],
    pub cyl_best: i32,
    /// Capsule, same layout.
    pub cap_err: [f64; 4],
    pub cap_max: [f64; 3],
    pub cap_best: i32,
    /// Box `err` / `max` of the principal-axes (0) and world-axes (1) passes and the choice.
    pub box_err: [f64; 2],
    pub box_max: [f64; 2],
    pub box_sel: i32,
    /// Sphere mean radius, area-weighted mean error and maximum error.
    pub sph_r: f64,
    pub sph_err: f64,
    pub sph_max: f64,
}

/// sub_350975D0: area-weighted centroid and covariance eigenvectors (rows) of the triangles
/// `indices[3k..3k+3]`, `k < n_tris`, in double. The same routine as
/// [`crate::geometry::bvtree::fit::principal_axes`] (sub_35195400; checked instruction by
/// instruction: identical evaluation order, Jacobi with threshold 0).
#[must_use]
pub fn principal_axes(vertices: &[Vec3], indices: &[u16], n_tris: i32) -> PrincipalAxes {
    let n = 3 * n_tris.max(0) as usize;
    let idx: Vec<i32> = indices[..n].iter().map(|&i| i32::from(i)).collect();
    principal_axes_i32(vertices, &idx, n_tris)
}

/// sub_3512D650: tries the primitives enabled in `flags` (see the module docs) against the
/// triangles `indices[3k..3k+3]`, `k < n_tris` (`n_tris ≥ 1`), with the principal `axes` (rows,
/// double) and the area-weighted `center` of [`principal_axes`]. Returns the geometry type
/// ([`KIND_MESH`] when nothing fits) and the primitive.
#[must_use]
pub fn fit_primitive(
    vertices: &[Vec3],
    indices: &[u16],
    n_tris: i32,
    axes: &[[f64; 3]; 3],
    center: &[f64; 3],
    flags: u32,
    tol: f32,
) -> (i32, Option<Primitive>) {
    let mut tr = FitTrace::default();
    fit_primitive_traced(vertices, indices, n_tris, axes, center, flags, tol, &mut tr)
}

/// [`fit_primitive`] that also fills a [`FitTrace`].
#[must_use]
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
pub fn fit_primitive_traced(
    vertices: &[Vec3],
    indices: &[u16],
    n_tris: i32,
    axes: &[[f64; 3]; 3],
    center: &[f64; 3],
    flags: u32,
    tol: f32,
    tr: &mut FitTrace,
) -> (i32, Option<Primitive>) {
    assert!(n_tris >= 1, "fit_primitive: n_tris must be positive");
    let m = Mesh {
        v: vertices,
        i: indices,
    };
    let nt = n_tris as usize;
    let n3 = 3 * nt;
    // `cvtsd2ss` of the double axes, row-major
    let mut a = [0.0f32; 9];
    for (k, x) in a.iter_mut().enumerate() {
        *x = axes[k / 3][k % 3] as f32;
    }
    let tol15 = f64::from(tol * 1.5);
    let told = f64::from(tol);

    if flags & FLAG_CYLINDER != 0 {
        if let Some(p) = round_pass(m, nt, n3, &a, false, tol15, told, tr) {
            return (KIND_CYLINDER, Some(Primitive::Cylinder(p)));
        }
    }
    if flags & FLAG_CAPSULE != 0 {
        if let Some(p) = round_pass(m, nt, n3, &a, true, tol15, told, tr) {
            return (KIND_CAPSULE, Some(Primitive::Capsule(p)));
        }
    }
    if flags & FLAG_BOX != 0 {
        if let Some(p) = box_pass(m, nt, n3, &a, tol15, told, tr) {
            return (KIND_BOX, Some(Primitive::Box(p)));
        }
    }
    if flags & FLAG_SPHERE != 0 {
        if let Some(p) = sphere_pass(m, nt, n3, center, tol15, told, tr) {
            return (KIND_SPHERE, Some(Primitive::Sphere(p)));
        }
    }
    (KIND_MESH, None)
}

/// The cylinder (`capsule == false`, 0x3512D76F) and capsule (0x3512E960) passes: each
/// principal axis through the centre of the projected extents.
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn round_pass(
    m: Mesh,
    nt: usize,
    n3: usize,
    a: &[f32; 9],
    capsule: bool,
    tol15: f64,
    told: f64,
    tr: &mut FitTrace,
) -> Option<CylinderPrim> {
    let (lo, hi) = extents(m, n3, a);
    let mid = [
        (lo[0] + hi[0]) * 0.5,
        (lo[1] + hi[1]) * 0.5,
        (lo[2] + hi[2]) * 0.5,
    ];
    let c = frame_point(a, mid);
    let mut err = [0.0f64, 0.0, 0.0, ERR_NONE];
    let mut maxe = [0.0f64; 3];
    let mut rad = [0.0f32; 3];
    let mut half = [0.0f32; 3];
    let mut best = 3usize;
    for i in 0..3 {
        let n = [a[3 * i], a[3 * i + 1], a[3 * i + 2]];
        let (hmax, r2max) = axial(m, n3, c, n);
        let r = r2max.sqrt();
        // capsule: the cylindrical half-length
        let h = if capsule { hmax - r } else { hmax };
        rad[i] = r;
        half[i] = h;
        // `comisd |x|, 1e-5; jb skip` (NaN skips)
        if !(f64::from(r.abs()) >= EPS_SIZE) || !(f64::from(h.abs()) >= EPS_SIZE) {
            continue;
        }
        let inv_r = (1.0 / f64::from(r)) as f32;
        let inv_h = (1.0 / f64::from(h)) as f32;
        let (mut sum, mut mx) = (0.0f64, 0.0f64);
        // [0] side faces, [1] cap faces (float sums, added in double)
        let mut area = [0.0f32; 2];
        for t in 0..nt {
            let (v0, v1, v2) = (m.p(3 * t), m.p(3 * t + 1), m.p(3 * t + 2));
            let (nx, ny, nz) = tri_normal(v0, v1, v2);
            // the capsule sums (nx² + ny²) + nz², the cylinder (ny² + nx²) + nz²: the same value
            let len2 = (ny * ny + nx * nx) + nz * nz;
            // `ucomiss len2, 0; je skip` (NaN skips)
            if len2 == 0.0 || len2.is_nan() {
                continue;
            }
            let len = len2.sqrt();
            let ah = f64::from(len) * 0.5;
            let (flag, e) = if capsule {
                capsule_tri(v0, v1, v2, c, n, h, inv_r)
            } else {
                cylinder_tri(v0, v1, v2, c, n, (nx, ny, nz), len, inv_r, inv_h)
            };
            let e = e * 0.5;
            sum += e * ah;
            mx = dmax(mx, e);
            area[flag] = (f64::from(area[flag]) + ah) as f32;
        }
        let (side, cap) = (area[0], area[1]);
        let tot = side + cap;
        let mean = if capsule {
            let den = ((EPS20 - tot).abs() + (tot + EPS20)) * 0.5;
            sum / f64::from(den) * W_MEAN
        } else {
            sum / f64::from(tot) * W_MEAN
        };
        let ihr = inv_h * inv_r;
        let ir2 = inv_r * inv_r;
        let rh = r * h;
        let r2 = r * r;
        let ds = ((f64::from(side) - f64::from(rh) * PI * 4.0) * (f64::from(ihr) * INV_2PI)).abs();
        let dc = if capsule {
            ((f64::from(cap) - f64::from(r2) * PI * 4.0) * (f64::from(ir2) * INV_4PI)).abs()
        } else {
            ((f64::from(cap) - f64::from(r2) * PI * 2.0) * (f64::from(ir2) * INV_2PI)).abs()
        };
        let da = dmax(ds, dc);
        err[i] = mean + da * W_AREA;
        maxe[i] = dmax(mx, da);
        let diff = (err[i] - err[best]) as f32;
        if diff.is_sign_negative() {
            best = i;
        }
    }
    if capsule {
        tr.cap_err = err;
        tr.cap_max = maxe;
        tr.cap_best = best as i32;
    } else {
        tr.cyl_err = err;
        tr.cyl_max = maxe;
        tr.cyl_best = best as i32;
    }
    if best < 3 && tol15 > maxe[best] && told > err[best] {
        return Some(CylinderPrim {
            center: c,
            axis: Vec3::new(a[3 * best], a[3 * best + 1], a[3 * best + 2]),
            r: rad[best],
            hh: half[best],
        });
    }
    None
}

/// Cylinder triangle (0x3512E217): side face (`|cos| ≤ 0.5`, flag 0) → radial errors, cap face
/// (flag 1) → axial errors. Returns the flag and `max` of the three vertex errors **before**
/// the final `·0.5`.
#[allow(clippy::too_many_arguments)]
fn cylinder_tri(
    v0: Vec3,
    v1: Vec3,
    v2: Vec3,
    c: Vec3,
    n: [f32; 3],
    (nx, ny, nz): (f32, f32, f32),
    len: f32,
    inv_r: f32,
    inv_h: f32,
) -> (usize, f64) {
    let inv = 1.0f32 / len;
    let cos = ((ny * inv) * n[1] + (nx * inv) * n[0]) + (nz * inv) * n[2];
    let s = 0.5f32 - cos.abs();
    let cap = s.is_sign_negative();
    let vert = |v: Vec3| -> f64 {
        let d = v - c;
        let t = (d.y * n[1] + d.x * n[0]) + d.z * n[2];
        if cap {
            rel_err(t.abs() * inv_h)
        } else {
            let (ry, rx, rz) = (d.y - n[1] * t, d.x - t * n[0], d.z - n[2] * t);
            rel_err(((ry * ry + rx * rx) + rz * rz).sqrt() * inv_r)
        }
    };
    let m = dmax0(vert(v0));
    let m = dmax(m, vert(v1));
    let e2 = vert(v2);
    (usize::from(cap), (m - e2).abs() + (e2 + m))
}

/// Capsule triangle (0x3512F40D): the centroid's axial coordinate decides between the
/// cylindrical part (flag 0, radial errors) and a cap (flag 1, distance to the cap centre
/// `c ± hh·n`, **signed** errors). Returns the flag and the unhalved max.
#[allow(clippy::too_many_arguments)]
fn capsule_tri(
    v0: Vec3,
    v1: Vec3,
    v2: Vec3,
    c: Vec3,
    n: [f32; 3],
    hh: f32,
    inv_r: f32,
) -> (usize, f64) {
    let sx = ((v0.x + v1.x) + v2.x) * THIRD;
    let sy = ((v0.y + v1.y) + v2.y) * THIRD;
    let sz = ((v0.z + v1.z) + v2.z) * THIRD;
    let hc = ((sx - c.x) * n[0] + (sy - c.y) * n[1]) + (sz - c.z) * n[2];
    let s = hh - hc.abs();
    let cap = s.is_sign_negative();
    let vert = |v: Vec3| -> f64 {
        let d = v - c;
        if cap {
            // `((bits >> 30) & ~1) + 1` → ±1, int → float
            let p = (((hc.to_bits() as i32 >> 30) & !1) + 1) as f32 * hh;
            let (ox, oy, oz) = (p * n[0], p * n[1], p * n[2]);
            let (qx, qy, qz) = (d.x - ox, d.y - oy, d.z - oz);
            // not made absolute (quirk)
            f64::from(((qx * qx + qy * qy) + qz * qz).sqrt() * inv_r - 1.0)
        } else {
            let t = (d.x * n[0] + d.y * n[1]) + d.z * n[2];
            let (ry, rx, rz) = (d.y - t * n[1], d.x - t * n[0], d.z - t * n[2]);
            rel_err(((ry * ry + rx * rx) + rz * rz).sqrt() * inv_r)
        }
    };
    let m = dmax0(vert(v0));
    let m = dmax(m, vert(v1));
    let e2 = vert(v2);
    (usize::from(cap), (m - e2).abs() + (e2 + m))
}

/// The box pass (0x3512FCCC): the principal axes, then (after a non-flat pass) the world axes.
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn box_pass(
    m: Mesh,
    nt: usize,
    n3: usize,
    a0: &[f32; 9],
    tol15: f64,
    told: f64,
    tr: &mut FitTrace,
) -> Option<BoxPrim> {
    let mut a = *a0;
    let mut err = [ERR_NONE; 2];
    let mut maxe = [ERR_NONE; 2];
    let mut sizes = [[0.0f32; 3]; 2];
    let mut centers = [Vec3::ZERO; 2];
    for it in 0..2 {
        let (lo, hi) = extents(m, n3, &a);
        let s = [
            (hi[0] - lo[0]) * 0.5,
            (hi[1] - lo[1]) * 0.5,
            (hi[2] - lo[2]) * 0.5,
        ];
        let mid = [
            (hi[0] + lo[0]) * 0.5,
            (hi[1] + lo[1]) * 0.5,
            (hi[2] + lo[2]) * 0.5,
        ];
        let c = frame_point(&a, mid);
        sizes[it] = s;
        centers[it] = c;
        let vol = (s[1] * s[0]) * s[2];
        if vol == 0.0 || vol.is_nan() {
            continue;
        }
        let inv = [1.0f32 / s[0], 1.0f32 / s[1], 1.0f32 / s[2]];
        let (mut sum, mut mx, mut tot) = (0.0f64, 0.0f64, 0.0f64);
        for t in 0..nt {
            let (v0, v1, v2) = (m.p(3 * t), m.p(3 * t + 1), m.p(3 * t + 2));
            let dx = ((v0.x + v1.x) + v2.x) * THIRD - c.x;
            let dy = ((v0.y + v1.y) + v2.y) * THIRD - c.y;
            let dz = ((v0.z + v1.z) + v2.z) * THIRD - c.z;
            let u = [
                (((dx * a[0] + dy * a[1]) + dz * a[2]) * inv[0]).abs(),
                (((dx * a[3] + dy * a[4]) + dz * a[5]) * inv[1]).abs(),
                (((dx * a[6] + dy * a[7]) + dz * a[8]) * inv[2]).abs(),
            ];
            // index of the largest |u| by sign bits
            let j = usize::from((u[0] - u[1]).is_sign_negative());
            let k = usize::from((u[j] - u[2]).is_sign_negative());
            let x = (2 * k) | j;
            let f = x & (((x ^ 2) | 4) >> 1);
            let e1 = v1 - v0;
            let e2 = v2 - v0;
            let nx = e1.y * e2.z - e1.z * e2.y;
            let ny = e2.x * e1.z - e1.x * e2.z;
            let nz = e1.x * e2.y - e2.x * e1.y;
            let area = f64::from(((ny * ny + nx * nx) + nz * nz).sqrt());
            let (fx, fy, fz) = (a[3 * f], a[3 * f + 1], a[3 * f + 2]);
            let vert = |v: Vec3| -> f64 {
                let d = v - c;
                rel_err(((d.y * fy + fx * d.x) + d.z * fz).abs() * inv[f])
            };
            let e = dmax0(vert(v0));
            let e = dmax(e, vert(v1));
            let e = dmax(e, vert(v2));
            mx = dmax(mx, e);
            sum += e * area;
            tot += area;
        }
        let inv_a = 1.0 / tot;
        let ds = (f64::from((((s[2] + s[1]) * s[0]) + s[2] * s[1]) * 16.0) * inv_a - 1.0).abs();
        err[it] = sum * inv_a * W_MEAN + ds * W_AREA;
        maxe[it] = dmax(mx, ds);
        a = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0];
    }
    let sel = usize::from(((err[1] - err[0] * BOX_WORLD_PREF) as f32).is_sign_negative());
    tr.box_err = err;
    tr.box_max = maxe;
    tr.box_sel = sel as i32;
    if tol15 > maxe[sel] && told > err[sel] {
        let (basis, oriented) = if sel == 0 {
            (
                Mat33 {
                    m: [
                        [a0[0], a0[1], a0[2]],
                        [a0[3], a0[4], a0[5]],
                        [a0[6], a0[7], a0[8]],
                    ],
                },
                1,
            )
        } else {
            (Mat33::IDENTITY, 0)
        };
        let s = sizes[sel];
        return Some(BoxPrim {
            basis,
            oriented,
            center: centers[sel],
            size: Vec3::new(s[0], s[1], s[2]),
        });
    }
    None
}

/// The sphere pass (0x35131037), centred on the area-weighted centroid.
#[allow(clippy::too_many_arguments)]
fn sphere_pass(
    m: Mesh,
    nt: usize,
    n3: usize,
    center: &[f64; 3],
    tol15: f64,
    told: f64,
    tr: &mut FitTrace,
) -> Option<SpherePrim> {
    let [cx, cy, cz] = *center;
    let mut acc = 0.0f64;
    for k in 0..n3 {
        let v = m.p(k);
        let dx = (f64::from(v.x) - cx) as f32;
        let dy = (f64::from(v.y) - cy) as f32;
        let dz = (f64::from(v.z) - cz) as f32;
        acc += f64::from(((dx * dx + dy * dy) + dz * dz).sqrt());
    }
    let r = acc / f64::from(n3 as i32);
    let inv_r = 1.0 / r;
    let dist = |v: Vec3| -> f64 {
        let (x, y, z) = (
            f64::from(v.x) - cx,
            f64::from(v.y) - cy,
            f64::from(v.z) - cz,
        );
        (((x * x + y * y) + z * z).sqrt() - r).abs() * inv_r
    };
    let (mut sum, mut mx, mut tot) = (0.0f64, 0.0f64, 0.0f64);
    for t in 0..nt {
        let (p0, p1, p2) = (m.p(3 * t), m.p(3 * t + 1), m.p(3 * t + 2));
        let e0 = dist(p0);
        let e1 = dist(p1);
        let mut e = dmax(e0, e1);
        e = dmax(e, dist(p2));
        let w = |v: Vec3| (f64::from(v.x), f64::from(v.y), f64::from(v.z));
        let ((x0, y0, z0), (x1, y1, z1), (x2, y2, z2)) = (w(p0), w(p1), w(p2));
        let (e1x, e1y, e1z) = (x1 - x0, y1 - y0, z1 - z0);
        let (e2x, e2y, e2z) = (x2 - x0, y2 - y0, z2 - z0);
        let nx = e1y * e2z - e1z * e2y;
        let ny = e1z * e2x - e2z * e1x;
        let nz = e2y * e1x - e1y * e2x;
        let len = ((nx * nx + ny * ny) + nz * nz).sqrt();
        if len > 1.0e-5 {
            let pl = ((x0 - cx) * nx + (y0 - cy) * ny) + (z0 - cz) * nz;
            e = dmax(e, (pl / len - r).abs() * inv_r);
        }
        tot += len;
        mx = dmax(mx, e);
        sum += e * len;
    }
    let mean = sum / tot;
    tr.sph_r = r;
    tr.sph_err = mean;
    tr.sph_max = mx;
    // `comisd; jae fail`: NaN passes
    if mx >= tol15 || mean >= told {
        return None;
    }
    Some(SpherePrim {
        center: Vec3::new(cx as f32, cy as f32, cz as f32),
        r: r as f32,
    })
}

/// `CGeomManager` slot 6 (0x35098490): `Some(primitive)` when `CreateMesh` replaces the mesh
/// by a primitive, `None` = keep the mesh (the slot returns 1).
///
/// `vertices` are indexed by `indices` (the original's strided arrays with strides 12 / 2);
/// `mats` are the per-triangle materials (`nTris` bytes). `n_tris ≤ 0` returns `None` (the
/// original would read index 0 regardless; `CreateMesh` never calls it so: it returns 0 first).
#[must_use]
pub fn try_primitive(
    vertices: &[Vec3],
    indices: &[u16],
    mats: Option<&[i8]>,
    n_tris: i32,
    flags: u32,
    tol: f32,
) -> Option<Primitive> {
    try_primitive_traced(
        vertices,
        indices,
        mats,
        n_tris,
        flags,
        tol,
        &mut FitTrace::default(),
    )
    .1
}

/// [`try_primitive`] returning the slot's value (the geometry type, [`KIND_MESH`] = keep) and
/// filling a [`FitTrace`].
#[must_use]
pub fn try_primitive_traced(
    vertices: &[Vec3],
    indices: &[u16],
    mats: Option<&[i8]>,
    n_tris: i32,
    flags: u32,
    tol: f32,
    tr: &mut FitTrace,
) -> (i32, Option<Primitive>) {
    if let Some(mt) = mats {
        let mut i = 1i32;
        while i < n_tris && mt[i as usize] == mt[0] {
            i += 1;
        }
        // `comiss tol, 0.5; jbe keep`
        if i != n_tris && !(tol > 0.5) {
            return (KIND_MESH, None);
        }
    }
    if !(tol > 0.0) || n_tris <= 0 {
        return (KIND_MESH, None);
    }
    let pa = principal_axes(vertices, indices, n_tris);
    fit_primitive_traced(
        vertices, indices, n_tris, &pa.axes, &pa.center, flags, tol, tr,
    )
}
