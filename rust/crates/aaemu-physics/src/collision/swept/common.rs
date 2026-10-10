//! Shared pieces of the UNPROJECT cells: primitive accessors, the three kinds of swap wrapper,
//! and small helper routines several pair functions call.
//!
//! Swap wrappers (`geometry.md` §5.2):
//! * **335 B linear** (e.g. 0x35171570 tri–box → box–tri 0x3516FDB0): negate `dir`, call the
//!   mirrored pair with `(b, fb, a, fa)`; on success `pt −= dir·(float)t` (with the negated dir),
//!   negate `n`, swap the two feature ids; finally negate `dir` again. [`swap_linear`].
//! * **215 B linear + area** (e.g. 0x35185AA0 cyl–box → box–cyl 0x351801A0): the same, with the
//!   success fix-up done by sub_3516E850 ([`flip_result_and_area`]), which also flips the contact
//!   area. [`swap_linear_area`].
//! * **447 B rotational** (e.g. 0x351A0A40 box–ray → ray–box 0x3519EF90): negate the axis, call
//!   the mirror; on success rotate `pt` about (center, axis) by (cos, −sin) with sub_3519A670,
//!   rotate `n` by the same with sub_35020EA0 and negate it, swap the feature ids; restore the
//!   axis. [`swap_rot`].

use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{
    BoxPrim, CapsulePrim, CylinderPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use crate::math::Vec3;

/// `dword_3520A3A0` (and its copy `dword_3520A600`): `inc_mod3` = {1, 2, 0} followed by
/// `dec_mod3` = {2, 0, 1} at +16 (0x3520A3B0 / 0x3520A610), each padded to 4 ints, the pair
/// repeated once. Indexed raw by some functions (e.g. `dword_3520A3AC` = entry 3).
pub const TAB_3520A3A0: [i32; 16] = [1, 2, 0, 0, 2, 0, 1, 0, 1, 2, 0, 0, 2, 0, 1, 0];
/// `inc_mod3[i] = (i + 1) % 3` (0x3520A3A0).
pub const INC_MOD3: [i32; 3] = [1, 2, 0];
/// `dec_mod3[i] = (i + 2) % 3` (0x3520A3B0).
pub const DEC_MOD3: [i32; 3] = [2, 0, 1];

macro_rules! accessor {
    ($name:ident, $variant:ident, $ty:ty) => {
        /// The primitive as the type its table cell expects (the table guarantees the type).
        ///
        /// # Panics
        /// If the primitive has another type (a dispatch bug).
        #[must_use]
        pub fn $name(p: &Primitive) -> &$ty {
            match p {
                Primitive::$variant(x) => x,
                other => panic!(
                    concat!(
                        "unprojection cell expected ",
                        stringify!($variant),
                        ", got {:?}"
                    ),
                    other.geom_type()
                ),
            }
        }
    };
}
accessor!(as_box, Box, BoxPrim);
accessor!(as_tri, Triangle, Triangle);
accessor!(as_ray, Ray, RayPrim);
accessor!(as_sph, Sphere, SpherePrim);
accessor!(as_cyl, Cylinder, CylinderPrim);
accessor!(as_cap, Capsule, CapsulePrim);

/// Negate the three components of `params.dir` the way the wrappers do (sign flips).
#[inline]
pub fn negate_dir(p: &mut UnprojParams) {
    p.dir = Vec3::new(-p.dir.x, -p.dir.y, -p.dir.z);
}

/// The success fix-up of the 335-byte linear wrappers (inline in each of them):
/// `pt −= dir·(float)t`, `n = −n`, swap `i_feature`.
#[inline]
pub fn fix_swapped_linear(p: &UnprojParams, out: &mut UnprojResult) {
    let t = out.t as f32;
    out.pt.x -= p.dir.x * t;
    out.pt.y -= p.dir.y * t;
    out.pt.z -= p.dir.z * t;
    out.n = Vec3::new(-out.n.x, -out.n.y, -out.n.z);
    out.i_feature.swap(0, 1);
}

/// 335-byte linear swap wrapper. `call` must invoke the mirrored pair as
/// `f(p, b, fb, a, fa, out)`.
pub fn swap_linear(
    p: &mut UnprojParams,
    out: &mut UnprojResult,
    call: impl FnOnce(&mut UnprojParams, &mut UnprojResult) -> i32,
) -> i32 {
    negate_dir(p);
    let r = call(p, out);
    if r != 0 {
        fix_swapped_linear(p, out);
    }
    negate_dir(p);
    r
}

/// sub_3516E850 (x64): the fix-up of the 215-byte area wrappers. Does the
/// [`fix_swapped_linear`] fix-up; then, if an area with `npt != 0` is present, exchanges the
/// normals: `out.n = area.n` and `area.n = −n'` where `n'` is the already negated result normal
/// (so the area receives the callee's original result normal). Each area point then gets its
/// A/B feature ids swapped, is shifted by `−dir·(float)t` and projected onto the plane through
/// `out.pt` with normal `out.n` (the normal just taken from the area).
pub fn flip_result_and_area(
    p: &UnprojParams,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) {
    fix_swapped_linear(p, out);
    let Some(a) = area else { return };
    if a.npt == 0 {
        return;
    }
    // v15 = n.z, v16 = -n.x, v17 = -n.y (n is the already-negated result normal)
    let nz = out.n.z;
    let nx = -out.n.x;
    let ny = -out.n.y;
    out.n = a.n;
    a.n = Vec3::new(nx, ny, -nz);
    let mut i = 0usize;
    while (i as i32) < a.npt {
        a.feature.swap_elem(i);
        let t = out.t as f32;
        let tx = p.dir.y * t;
        let tz = p.dir.z * t;
        a.pt[i].x -= p.dir.x * t;
        a.pt[i].y -= tx;
        a.pt[i].z -= tz;
        let n = out.n;
        let d = ((a.pt[i].x - out.pt.x) * n.x + (a.pt[i].y - out.pt.y) * n.y)
            + (a.pt[i].z - out.pt.z) * n.z;
        a.pt[i].x -= n.x * d;
        a.pt[i].y -= n.y * d;
        a.pt[i].z -= n.z * d;
        i += 1;
    }
}

/// Swap entry `i` between the two feature arrays of a contact area.
trait SwapElem {
    fn swap_elem(&mut self, i: usize);
}
impl SwapElem for [Vec<i32>; 2] {
    fn swap_elem(&mut self, i: usize) {
        let [f0, f1] = self;
        std::mem::swap(&mut f0[i], &mut f1[i]);
    }
}

/// 215-byte linear swap wrapper with the area fix-up (sub_3516E850). `call` invokes the mirrored
/// pair as `f(p, b, fb, a, fa, out, area)`.
pub fn swap_linear_area(
    p: &mut UnprojParams,
    out: &mut UnprojResult,
    mut area: Option<&mut ContactArea>,
    call: impl FnOnce(&mut UnprojParams, &mut UnprojResult, Option<&mut ContactArea>) -> i32,
) -> i32 {
    negate_dir(p);
    let r = call(p, out, area.as_deref_mut());
    if r != 0 {
        flip_result_and_area(p, out, area);
    }
    negate_dir(p);
    r
}

/// sub_35020EA0: rotate `v` about the unit `axis` by the angle with cosine `c` and sine `s`
/// (`v∥ + v⊥·c + (axis × v⊥)·s`, with the binary's operation order).
#[must_use]
pub fn rotate_vec(v: Vec3, axis: Vec3, c: f32, s: f32) -> Vec3 {
    let a = axis;
    let d = (v.x * a.x + v.y * a.y) + v.z * a.z;
    let v12 = a.y * d;
    let v13 = a.x * d;
    let v14 = v.y - a.y * d;
    let v15 = v.x - v13;
    let v16 = a.z * d;
    let v17 = a.z * v14;
    let v18 = v.z - a.z * d;
    let v19 = a.x * v14;
    let v20 = a.z * v15 - a.x * v18;
    Vec3::new(
        (v15 * c + (a.y * v18 - v17) * s) + v13,
        (v14 * c + v20 * s) + v12,
        (v18 * c + (v19 - a.y * v15) * s) + v16,
    )
}

/// sub_3519A670: rotate the point `pt` about the line (`center`, `axis`) by (c, s):
/// `center + rotate_vec(pt − center)`.
#[must_use]
pub fn rotate_point(pt: Vec3, center: Vec3, axis: Vec3, c: f32, s: f32) -> Vec3 {
    let d = Vec3::new(pt.x - center.x, pt.y - center.y, pt.z - center.z);
    let r = rotate_vec(d, axis, c, s);
    Vec3::new(center.x + r.x, r.y + center.y, r.z + center.z)
}

/// 447-byte rotational swap wrapper (e.g. 0x351A0A40). `call` invokes the mirrored pair as
/// `f(p, b, fb, a, fa, out)`.
pub fn swap_rot(
    p: &mut UnprojParams,
    out: &mut UnprojResult,
    call: impl FnOnce(&mut UnprojParams, &mut UnprojResult) -> i32,
) -> i32 {
    negate_dir(p);
    let r = call(p, out);
    if r != 0 {
        // the (double) cos and −sin are converted to float for the calls
        let c = out.cos_t as f32;
        let s = (-out.t) as f32;
        out.pt = rotate_point(out.pt, p.center, p.dir, c, s);
        let c = out.cos_t as f32;
        let s = (-out.t) as f32;
        let n = rotate_vec(out.n, p.dir, c, s);
        out.n = Vec3::new(-n.x, -n.y, -n.z);
        out.i_feature.swap(0, 1);
    }
    negate_dir(p);
    r
}

/// sub_35021020: normalise in place (`|v|² ≤ 1e-20` → (0,0,1)); `v·(1/√|v|²)` per component,
/// squared length summed `(x² + y²) + z²`.
pub fn normalize_in_place(v: &mut Vec3) {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 <= 1e-20 {
        *v = Vec3::new(0.0, 0.0, 1.0);
    } else {
        let inv = 1.0 / l2.sqrt();
        *v = Vec3::new(v.x * inv, v.y * inv, v.z * inv);
    }
}

/// sub_350210A0: normalised copy (`|v|² ≤ 1e-20` → (0,0,1)), same arithmetic as
/// [`normalize_in_place`].
#[must_use]
pub fn normalized(v: Vec3) -> Vec3 {
    let mut r = v;
    normalize_in_place(&mut r);
    r
}

/// sub_35022380: `a · b` for row-major 3×3 matrices, `r[i][j] = (a[i][0]·b[0][j] +
/// a[i][1]·b[1][j]) + a[i][2]·b[2][j]`.
#[must_use]
pub fn mat_mul(a: &[f32; 9], b: &[f32; 9]) -> [f32; 9] {
    let mut r = [0.0f32; 9];
    for i in 0..3 {
        for j in 0..3 {
            r[i * 3 + j] = (a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j]) + a[i * 3 + 2] * b[6 + j];
        }
    }
    r
}

/// sub_3516E6C0: clip the 2-D segment `seg = [p0.x, p0.y, p1.x, p1.y]` to the slab
/// `|coord[i]| ≤ h` (`i` = 0 or 1). Returns `false`-like 0 and zeroes `seg` when the segment is
/// entirely outside. Clipped ends get their feature ids: the low end `ids0[lo] = id0`,
/// `ids1[lo] = id1`; the high end `ids0[hi] = id0 | (2 >> i)`, `ids1[hi] = id1`.
///
/// Returns what the binary leaves in `eax`: 0 on reject, else `id0 | (2 >> i)` if the high end
/// was clipped, `i ^ 1` if only the low end was clipped, and `hi` otherwise (`hi` = index of the
/// end with the larger coordinate, from the *sign bit* of `p1[i] − p0[i]`, so −0 counts as
/// negative; Hex-Rays shows `>= 0.0`, which is wrong for −0).
pub fn clip_segment_slab(
    seg: &mut [f32; 4],
    h: f32,
    i: usize,
    ids0: &mut [i32],
    ids1: &mut [i32],
    id0: i32,
    id1: i32,
) -> i32 {
    let nh = -h;
    let d = seg[i + 2] - seg[i];
    let lo = (d.to_bits() >> 31) as usize;
    let hi = lo ^ 1;
    let ihi = i + 2 * hi;
    let ilo = i + 2 * lo;
    let vmax = seg[ihi];
    // comiss/jb: unordered rejects too
    if lt_or_nan(vmax, nh) {
        *seg = [0.0; 4];
        return 0;
    }
    let vmin = seg[ilo];
    if vmin > h {
        *seg = [0.0; 4];
        return 0;
    }
    let delta = [seg[2 * hi] - seg[2 * lo], seg[2 * hi + 1] - seg[2 * lo + 1]];
    let mut rinv = 0.0f32;
    if lt_or_nan(vmin, nh) || vmax > h {
        rinv = 1.0 / delta[i];
    }
    let j = i ^ 1;
    let mut ret = hi as i32;
    if lt_or_nan(vmin, nh) {
        let k = j + 2 * lo;
        seg[k] += ((nh - vmin) * delta[j]) * rinv;
        seg[ilo] = nh;
        ids0[lo] = id0;
        ids1[lo] = id1;
        ret = j as i32;
    }
    if lt_or_nan(h, seg[ihi]) {
        seg[j + 2 * hi] = ((h - seg[ilo]) * delta[j]) * rinv + seg[j + 2 * lo];
        seg[ihi] = h;
        ret = id0 | (2 >> i);
        ids0[hi] = ret;
        ids1[hi] = id1;
    }
    ret
}

/// `ucomiss a, b` followed by `je`/`jz` (or the fall-through of `jne` without a `jp` check):
/// true when equal **or unordered** (a NaN operand sets ZF). Hex-Rays prints this as `a == b`,
/// which differs for NaN.
#[inline]
#[must_use]
pub fn ucomiss_eq(a: f32, b: f32) -> bool {
    a == b || a.is_nan() || b.is_nan()
}

/// `comiss a, b` + `jb` (CF set): `a < b` **or unordered**. Rust's `a < b` is false for NaN.
/// (`ja`/`jae` are ordered and equal Rust's `>`/`>=`.)
#[inline]
#[must_use]
#[allow(clippy::neg_cmp_op_on_partial_ord)]
pub fn lt_or_nan(a: f32, b: f32) -> bool {
    !(a >= b)
}

/// `comiss a, b` + `jbe` (CF or ZF set): `a <= b` **or unordered**.
#[inline]
#[must_use]
#[allow(clippy::neg_cmp_op_on_partial_ord)]
pub fn le_or_nan(a: f32, b: f32) -> bool {
    !(a > b)
}

/// Double-precision [`lt_or_nan`] (`comisd` + `jb`).
#[inline]
#[must_use]
#[allow(clippy::neg_cmp_op_on_partial_ord)]
pub fn lt_or_nan_d(a: f64, b: f64) -> bool {
    !(a >= b)
}

/// Double-precision [`le_or_nan`] (`comisd` + `jbe`).
#[inline]
#[must_use]
#[allow(clippy::neg_cmp_op_on_partial_ord)]
pub fn le_or_nan_d(a: f64, b: f64) -> bool {
    !(a > b)
}

/// Double-precision variant of [`ucomiss_eq`] (`ucomisd`).
#[inline]
#[must_use]
pub fn ucomisd_eq(a: f64, b: f64) -> bool {
    a == b || a.is_nan() || b.is_nan()
}

/// Groups that own no cells yet use this as their `fill`.
pub fn fill_none(_t: &mut super::UnprojTables) {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clip_segment_slab_basic() {
        let mut s = [-2.0f32, 0.0, 2.0, 1.0];
        let (mut a, mut b) = ([0i32; 2], [0i32; 2]);
        let r = clip_segment_slab(&mut s, 1.0, 0, &mut a, &mut b, 4, 7);
        assert_eq!(s, [-1.0, 0.25, 1.0, 0.75]);
        assert_eq!(r, 4 | 2);
        assert_eq!(a, [4, 6]);
        assert_eq!(b, [7, 7]);
        let mut s = [3.0f32, 0.0, 2.0, 1.0];
        assert_eq!(clip_segment_slab(&mut s, 1.0, 0, &mut a, &mut b, 0, 0), 0);
        assert_eq!(s, [0.0; 4]);
    }

    #[test]
    fn rotate_quarter_turn() {
        let v = rotate_vec(Vec3::X, Vec3::Z, 0.0, 1.0);
        assert_eq!(v, Vec3::new(0.0, 1.0, 0.0));
        let p = rotate_point(Vec3::new(2.0, 0.0, 0.0), Vec3::X, Vec3::Z, 0.0, 1.0);
        assert_eq!(p, Vec3::new(1.0, 1.0, 0.0));
    }
}
