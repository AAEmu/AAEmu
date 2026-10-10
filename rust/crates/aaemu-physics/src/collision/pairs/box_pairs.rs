//! **P2a group 1 — box pairs.** Every INTERSECT / OVERLAP cell of the dispatch tables in which a
//! box takes part and that no other group owns (`geometry.md` §4.1, §4.5):
//!
//! | cell | x64 | port |
//! |---|---|---|
//! | INTERSECT box–box | 0x3516AE00 | [`intersect_box_box`] |
//! | INTERSECT box–ray | 0x3515F110 | [`intersect_box_ray`] |
//! | INTERSECT box–sph | 0x3515D460 | [`intersect_box_sphere`] |
//! | INTERSECT box–cyl | 0x3515BA70 | [`intersect_box_cylinder`] |
//! | INTERSECT box–cap | 0x3515D8A0 | [`intersect_box_capsule`] |
//! | INTERSECT ray–box / sph–box / cyl–box / cap–box | 0x3515F690 / 0x3515D820 / 0x3515D3F0 / 0x3515F0A0 | swap wrappers |
//! | OVERLAP box–box | 0x351981B0 | [`overlap_box_box`] |
//! | OVERLAP box–tri | 0x351968F0 | [`overlap_box_tri`] |
//! | OVERLAP box–hf | 0x351986E0 | [`overlap_box_heightfield`] |
//! | OVERLAP box–ray | 0x35195EA0 | [`overlap_box_ray`] |
//! | OVERLAP box–sph | 0x35196CE0 | [`overlap_box_sphere`] |
//! | OVERLAP ray–box / sph–box / tri–box / hf–box | 0x35196010 / 0x35196E50 / 0x35196CC0 / 0x35199120 | swap wrappers |
//!
//! INTERSECT box–tri (0x35164780) is the swap wrapper of tri–box and belongs to
//! [`super::tri_pairs`]. OVERLAP box–vox / vox–box (0x35199140 / 0x35199B10) are not filled: the
//! shared [`Primitive`] has no voxel variant (voxel BVs never reach the table as primitives in
//! the port; they emit triangles like heightfields).
//!
//! **Fidelity.** Every function is a transcription of the x64 disassembly: single-precision
//! steps stay `f32`, every `cvtps2pd … mulsd` step is evaluated in `f64` with the same
//! association, comparisons keep the binary's NaN behaviour (`comisd`/`ucomisd` flag
//! semantics), packed SSE code (the OVERLAP tests, SSE4.1 path) is evaluated lane by lane with
//! the same shuffles. Verified bit for bit against the DLL with `physoracle --math p2a1_*`
//! probes (`tests/p2a1_box_pairs.rs`). Bugs and oddities of the original are kept and listed
//! at each function.
//!
//! **Output convention.** The functions write exactly the [`PrimInters`] fields the binary
//! writes and leave every other field untouched (callers reuse one record). The border polyline
//! (`+56` pointer / `+64` count) is [`PrimInters::border`]: "count := 0" is `clear()`, an append
//! is `push`.
//!
//! **`border_max` is not honoured — as in the binary.** None of the five box functions reads
//! `prim_inters +68` (`nbordersz`, [`PrimInters::border_max`]); they append unconditionally
//! (box–box up to 48 points, box–cylinder / box–capsule more). With the 15-point buffer the
//! primitive caller 0x3514DFD0 provides, the original can overrun it; the port's `Vec` simply
//! grows. Callers that need the cap must truncate themselves.

// `!(x >= 0.0)` and friends are deliberate: they reproduce the binary's unordered-compare
// outcome (`comisd` + `jae`/`jbe`) for NaN operands.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::{IntersectFn, OverlapFn, PairTables};
use crate::collision::contact::PrimInters;
use crate::geometry::primitives::{
    BoxPrim, CylinderPrim, HeightfieldPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use crate::ids::GeomType;
use crate::math::Vec3;

// ------------------------------------------------------------------------------------------
// shared helpers
// ------------------------------------------------------------------------------------------

/// `1e-80`, the tie-break factor of the double-precision quotient comparisons.
const QEPS: f64 = 1e-80;

/// The 16 dwords of a box primitive as the binary sees them (`basis[9]`, `bOriented`, `center`,
/// `size`); word 9 is the `bOriented` int reinterpreted.
#[inline]
fn bw(b: &BoxPrim) -> [f32; 16] {
    let m = &b.basis.m;
    [
        m[0][0],
        m[0][1],
        m[0][2],
        m[1][0],
        m[1][1],
        m[1][2],
        m[2][0],
        m[2][1],
        m[2][2],
        f32::from_bits(b.oriented as u32),
        b.center.x,
        b.center.y,
        b.center.z,
        b.size.x,
        b.size.y,
        b.size.z,
    ]
}

/// `((bits >> 30) & ~1) + 1` as a float (`cvtdq2ps`).
#[inline]
fn sgnf(x: f32) -> f32 {
    (((x.to_bits() as i32) >> 30) & !1).wrapping_add(1) as f32
}

/// Sign bit of a double after `cvtsd2ss` (the binary tests `float(x) >> 31`).
#[inline]
fn sbit_d(x: f64) -> u32 {
    (x as f32).to_bits() >> 31
}

/// `ucomisd x, 0; je …`: true when `x == 0` **or** `x` is NaN (unordered sets ZF).
#[inline]
fn ucom_eq0(x: f64) -> bool {
    x == 0.0 || x.is_nan()
}

/// `x / y`, or 0 when `ucomisd y, 0` takes the "equal" branch.
#[inline]
fn qdiv(x: f64, y: f64) -> f64 {
    if ucom_eq0(y) {
        0.0
    } else {
        x / y
    }
}

#[inline]
fn d(x: f32) -> f64 {
    f64::from(x)
}

/// Write `pt` to both contact points.
#[inline]
fn set_pts(out: &mut PrimInters, pt: Vec3) {
    out.pt[0] = pt;
    out.pt[1] = pt;
}

/// The 4-byte `iFeature` dword (`+36`), little endian.
#[inline]
fn set_feat(out: &mut PrimInters, dw: u32) {
    let b = dw.to_le_bytes();
    out.i_feature = [[b[0], b[1]], [b[2], b[3]]];
}

/// Swap-wrapper feature fix-up: bytes `36↔39`, `37↔38`.
#[inline]
fn reverse_feat(out: &mut PrimInters) {
    let [[a0, a1], [b0, b1]] = out.i_feature;
    out.i_feature = [[b1, b0], [a1, a0]];
}

#[inline]
fn neg_n(out: &mut PrimInters) {
    out.n = Vec3::new(-out.n.x, -out.n.y, -out.n.z);
}

// ------------------------------------------------------------------------------------------
// packed SSE emulation for the OVERLAP tests (lane-exact)
// ------------------------------------------------------------------------------------------

#[derive(Clone, Copy, Debug)]
struct F4([f32; 4]);

impl F4 {
    const ZERO: F4 = F4([0.0; 4]);
    #[inline]
    fn splat(x: f32) -> F4 {
        F4([x; 4])
    }
    /// `movss` load: `[x, 0, 0, 0]`.
    #[inline]
    fn ss(x: f32) -> F4 {
        F4([x, 0.0, 0.0, 0.0])
    }
    #[inline]
    fn load(w: &[f32], i: usize) -> F4 {
        let g = |k: usize| w.get(i + k).copied().unwrap_or(0.0);
        F4([g(0), g(1), g(2), g(3)])
    }
    #[inline]
    fn add(self, b: F4) -> F4 {
        F4(std::array::from_fn(|i| self.0[i] + b.0[i]))
    }
    #[inline]
    fn sub(self, b: F4) -> F4 {
        F4(std::array::from_fn(|i| self.0[i] - b.0[i]))
    }
    #[inline]
    fn mul(self, b: F4) -> F4 {
        F4(std::array::from_fn(|i| self.0[i] * b.0[i]))
    }
    /// `andnot(signmask, x)`.
    #[inline]
    fn abs(self) -> F4 {
        F4(self.0.map(f32::abs))
    }
    /// `_mm_shuffle_ps(a, b, imm)`.
    #[inline]
    fn shuf(self, b: F4, imm: u32) -> F4 {
        let s = |k: u32| ((imm >> (2 * k)) & 3) as usize;
        F4([self.0[s(0)], self.0[s(1)], b.0[s(2)], b.0[s(3)]])
    }
    #[inline]
    fn sh(self, imm: u32) -> F4 {
        self.shuf(self, imm)
    }
    /// `_mm_unpacklo_ps(a, b)`.
    #[inline]
    fn unpacklo(self, b: F4) -> F4 {
        F4([self.0[0], b.0[0], self.0[1], b.0[1]])
    }
    /// `cmpltps` mask as 4 bools.
    #[inline]
    fn lt(self, b: F4) -> [bool; 4] {
        std::array::from_fn(|i| self.0[i] < b.0[i])
    }
    /// `dpps a, b, 0x7F`: `(a0·b0 + a1·b1) + (a2·b2 + 0)` broadcast.
    #[inline]
    fn dp7f(self, b: F4) -> F4 {
        let t = [self.0[0] * b.0[0], self.0[1] * b.0[1], self.0[2] * b.0[2]];
        F4::splat((t[0] + t[1]) + (t[2] + 0.0))
    }
}

/// `movemask(shuffle(m, m, 36))`: lanes 0, 1, 2, 0.
#[inline]
fn any3(m: [bool; 4]) -> bool {
    m[0] || m[1] || m[2]
}

#[inline]
fn any4(m: [bool; 4]) -> bool {
    m.iter().any(|&b| b)
}

/// Column `j` of the box basis as `unpacklo(unpacklo(m0j, m2j), unpacklo(m1j, 0))`
/// = `[m0j, m1j, m2j, 0]`.
#[inline]
fn col4(w: &[f32; 16], j: usize) -> F4 {
    F4::ss(w[j])
        .unpacklo(F4::ss(w[6 + j]))
        .unpacklo(F4::ss(w[3 + j]).unpacklo(F4::ZERO))
}

/// `(v.y·c1 + v.x·c0) + v.z·c2` with columns `c` (the binary's packed "basis × vector").
#[inline]
fn mat_cols_mul(c: [F4; 3], v: F4) -> F4 {
    v.sh(85)
        .mul(c[1])
        .add(v.sh(0).mul(c[0]))
        .add(v.sh(170).mul(c[2]))
}

// ------------------------------------------------------------------------------------------
// INTERSECT box–sphere 0x3515D460 (+ wrapper sph–box 0x3515D820)
// ------------------------------------------------------------------------------------------

/// INTERSECT box–sphere (0x3515D460).
///
/// The sphere centre is clamped into the box (in box coordinates, `min(size − |p|, 0)·sgn(p)`
/// per axis), the clamped point becomes `pt[0] = pt[1]` and the single border point,
/// `n = sphere.center − pt` (not normalised), features `0x40` ×4. Returns
/// `isneg(|clamp offset|² − r²)`.
///
/// Quirk kept: every output field (points, border, normal, features) is written even when the
/// result is "no contact"; a centre on a face gives a zero normal.
pub fn intersect_box_sphere(bx: &BoxPrim, s: &SpherePrim, out: &mut PrimInters) -> bool {
    let a = bw(bx);
    let dx = s.center.x - a[10];
    let dy = s.center.y - a[11];
    let dz = s.center.z - a[12];
    let p0 = (a[1] * dy + a[0] * dx) + a[2] * dz;
    let p1 = (a[4] * dy + a[3] * dx) + a[5] * dz;
    let p2 = (a[7] * dy + a[6] * dx) + a[8] * dz;
    let clamp = |sz: f32, p: f32| {
        let t = sz - p.abs();
        ((t - t.abs()) * 0.5) * sgnf(p)
    };
    let o0 = clamp(a[13], p0);
    let o1 = clamp(a[14], p1);
    let o2 = clamp(a[15], p2);
    let q0 = o0 + p0;
    let q1 = o1 + p1;
    let q2 = o2 + p2;
    let x = ((a[3] * q1 + a[0] * q0) + a[6] * q2) + a[10];
    let y = ((a[4] * q1 + a[1] * q0) + a[7] * q2) + a[11];
    let z = ((a[5] * q1 + a[2] * q0) + a[8] * q2) + a[12];
    let pt = Vec3::new(x, y, z);
    set_pts(out, pt);
    out.border.clear();
    out.border.push(pt);
    out.n = Vec3::new(s.center.x - x, s.center.y - y, s.center.z - z);
    set_feat(out, 0x4040_4040);
    let d2 = ((o1 * o1 + o0 * o0) + o2 * o2) - s.r * s.r;
    d2.to_bits() >> 31 != 0
}

/// INTERSECT sph–box (0x3515D820): box–sphere with swapped arguments, features reversed and
/// `n` negated.
pub fn intersect_sphere_box(s: &SpherePrim, bx: &BoxPrim, out: &mut PrimInters) -> bool {
    let r = intersect_box_sphere(bx, s, out);
    reverse_feat(out);
    neg_n(out);
    r
}

// ------------------------------------------------------------------------------------------
// INTERSECT box–ray 0x3515F110 (+ wrapper ray–box 0x3515F690)
// ------------------------------------------------------------------------------------------

/// INTERSECT box–ray (0x3515F110): slab clipping of the segment `origin + t·dir`, `t ∈ [0, 1]`,
/// with the slab fractions kept as double-precision quotients.
///
/// The contact is the entry point (or, when the segment starts inside, the exit point), the
/// normal is the box axis of the clipping slab with the sign of `(pt − center)·axis`;
/// features `{0x40, 0x20, 0x40, 0x20}`. The border is not touched.
pub fn intersect_box_ray(bx: &BoxPrim, r: &RayPrim, out: &mut PrimInters) -> bool {
    let a = bw(bx);
    let o = r.origin;
    let dx = o.x - a[10];
    let dy = o.y - a[11];
    let dz = o.z - a[12];
    let p = [
        d((a[0] * dx + a[1] * dy) + a[2] * dz),
        d((a[3] * dx + a[4] * dy) + a[5] * dz),
        d((a[6] * dx + a[7] * dy) + a[8] * dz),
    ];
    let dir = r.dir;
    let v = [
        d((a[0] * dir.x + a[1] * dir.y) + a[2] * dir.z),
        d((a[3] * dir.x + a[4] * dir.y) + a[5] * dir.z),
        d((a[6] * dir.x + a[7] * dir.y) + a[8] * dir.z),
    ];
    // tmin = {num, den} = {0, 1}, tmax = {1, 1}
    let (mut tn0, mut td0) = (0.0f64, 1.0f64);
    let (mut tn1, mut td1) = (1.0f64, 1.0f64);
    let mut imin: i32 = -1;
    let mut imax: i32 = -1;
    for i in 0..3 {
        let s = a[13 + i];
        let sgn = (v[i] as f32).to_bits() >> 31;
        let ds = f64::from(1 - 2 * sgn as i32);
        let lo = (d(-s) - p[i]) * ds;
        let hi = (d(s) - p[i]) * ds;
        let den = ds * v[i];
        // entry[sgn] = lo, entry[1 - sgn] = hi
        let (e0n, e1n) = if sgn == 0 { (lo, hi) } else { (hi, lo) };
        let (e0d, e1d) = (den, den);
        if (td0 * e0n - e0d * tn0) + (e0n - tn0) * QEPS > 0.0 {
            tn0 = e0n;
            td0 = e0d;
            imin = i as i32;
        }
        let x = (e1n * td1 - e1d * tn1) + (e1n - tn1) * QEPS;
        if !(x >= 0.0) {
            tn1 = e1n;
            td1 = e1d;
            imax = i as i32;
        }
    }
    let x = (td1 * tn0 - tn1 * td0) + (tn0 - tn1) * QEPS;
    if x > 0.0 || (imin & imax) == -1 {
        return false;
    }
    let sel = (-(imin >> 31)) as usize;
    let (num, den, ax) = if sel == 0 {
        (tn0, td0, imin)
    } else {
        (tn1, td1, imax)
    };
    let t = qdiv(num, den) as f32;
    let pt = Vec3::new(o.x + dir.x * t, o.y + dir.y * t, o.z + dir.z * t);
    set_pts(out, pt);
    let k = 3 * ax as usize;
    let n = Vec3::new(a[k], a[k + 1], a[k + 2]);
    let dot = ((pt.x - a[10]) * n.x + (pt.y - a[11]) * n.y) + (pt.z - a[12]) * n.z;
    let sg = sgnf(dot);
    out.n = Vec3::new(sg * n.x, sg * n.y, sg * n.z);
    set_feat(out, 0x2040_2040);
    true
}

/// INTERSECT ray–box (0x3515F690): box–ray with swapped arguments and reversed features only
/// (the normal keeps the box orientation).
pub fn intersect_ray_box(r: &RayPrim, bx: &BoxPrim, out: &mut PrimInters) -> bool {
    let res = intersect_box_ray(bx, r, out);
    reverse_feat(out);
    res
}

// ------------------------------------------------------------------------------------------
// INTERSECT box–box 0x3516AE00
// ------------------------------------------------------------------------------------------

/// `sub_35022380`: `out = a · b` for row-major 3×3 float matrices, element
/// `(a[i][0]·b[0][j] + a[i][1]·b[1][j]) + a[i][2]·b[2][j]`.
#[inline]
fn mat_mul_9(a: &[f32], b: &[f32; 9]) -> [f32; 9] {
    std::array::from_fn(|e| {
        let (i, j) = (e / 3, e % 3);
        (a[3 * i] * b[j] + a[3 * i + 1] * b[3 + j]) + a[3 * i + 2] * b[6 + j]
    })
}

/// INTERSECT box–box (0x3516AE00).
///
/// For each box in turn, the 12 edges of the other box are clipped (in double precision,
/// quotients with the `1e-80` tie-break) against its slabs; every clipped end point of an edge
/// that survives is appended to the border in world space. On contact `pt[0] = pt[1] =`
/// border\[0], `n = 0` (the caller derives the direction), features `0x40` ×4.
///
/// Quirks kept: an edge lying completely inside the other box contributes no point (only
/// *clipped* end points are emitted), so e.g. two coincident boxes report **no contact**; the
/// border count is reset to 0 even on "no contact"; zero denominators give `t = 0`.
pub fn intersect_box_box(ba: &BoxPrim, bb: &BoxPrim, out: &mut PrimInters) -> bool {
    let a = bw(ba);
    let b = bw(bb);
    let dx = b[10] - a[10];
    let dy = b[11] - a[11];
    let dz = b[12] - a[12];
    // offset of B in A's frame, then of A in B's frame (both rounded to float, kept as double)
    let pa = [
        d((a[0] * dx + dy * a[1]) + dz * a[2]),
        d((a[3] * dx + dy * a[4]) + dz * a[5]),
        d((a[6] * dx + dy * a[7]) + dz * a[8]),
    ];
    let ex = a[10] - b[10];
    let ey = a[11] - b[11];
    let ez = a[12] - b[12];
    let pb = [
        d((ex * b[0] + ey * b[1]) + ez * b[2]),
        d((ex * b[3] + ey * b[4]) + ez * b[5]),
        d((ex * b[6] + ey * b[7]) + ez * b[8]),
    ];
    let at = [a[0], a[3], a[6], a[1], a[4], a[7], a[2], a[5], a[8]];
    // R = B · Aᵀ: row i = B axis i in A's frame
    let r9 = mat_mul_9(&b[..9], &at);
    let mut r: [f64; 9] = r9.map(d);
    out.border.clear();
    const T1: [usize; 3] = [1, 2, 0];
    const T2: [usize; 3] = [2, 0, 1];
    for pass in 0..2 {
        let (clip, edges, p) = if pass == 0 {
            (&a, &b, &pa)
        } else {
            (&b, &a, &pb)
        };
        for e in 0..12i32 {
            let k = (e >> 2) as usize;
            let mut sg = [0i32; 3];
            sg[k] = -1;
            sg[T1[k]] = 2 * (e & 1) - 1;
            sg[T2[k]] = (e & 2) - 1;
            let s: [f64; 3] = std::array::from_fn(|j| d(sg[j] as f32 * edges[13 + j]));
            let vx = ((p[0] + s[0] * r[0]) + s[1] * r[3]) + s[2] * r[6];
            let vy = ((p[1] + r[1] * s[0]) + r[4] * s[1]) + r[7] * s[2];
            let vz = ((p[2] + r[2] * s[0]) + r[5] * s[1]) + r[8] * s[2];
            let vtx = [vx, vy, vz];
            let dir = [r[3 * k], r[3 * k + 1], r[3 * k + 2]];
            // tmin = {0, 1}, tmax = {2·size_k, 1}
            let (mut n0, mut d0) = (0.0f64, 1.0f64);
            let (mut n1, mut d1) = (d(edges[13 + k] * 2.0), 1.0f64);
            let mut any_min = 0u32;
            let mut any_max = 0u32;
            for j in 0..3 {
                let dj = dir[j];
                let sz = clip[13 + j];
                let adj = dj.abs();
                let sv = f64::from(sgnf(dj as f32)) * vtx[j];
                let lo = d(-sz) - sv;
                let hi = d(sz) - sv;
                let c1 = sbit_d(adj * n0 - d0 * lo);
                any_min |= c1;
                let (c1d, nc1d) = (f64::from(c1), f64::from(1 - c1 as i32));
                d0 = nc1d * d0 + c1d * adj;
                n0 = nc1d * n0 + c1d * lo;
                let c2 = sbit_d(d1 * hi - adj * n1);
                any_max |= c2;
                let (c2d, nc2d) = (f64::from(c2), f64::from(1 - c2 as i32));
                d1 = nc2d * d1 + c2d * adj;
                n1 = nc2d * n1 + c2d * hi;
            }
            let x = (d1 * n0 - n1 * d0) + (n0 - n1) * QEPS;
            if x >= 0.0 {
                continue;
            }
            let emit = |t: f64, out: &mut PrimInters| {
                let qx = t * dir[0] + vx;
                let qy = dir[1] * t + vy;
                let qz = dir[2] * t + vz;
                let c = clip;
                let wx = d(c[10]) + ((d(c[3]) * qy + d(c[0]) * qx) + d(c[6]) * qz);
                let wy = d(c[11]) + ((d(c[4]) * qy + d(c[1]) * qx) + d(c[7]) * qz);
                let wz = d(c[12]) + ((d(c[5]) * qy + d(c[2]) * qx) + d(c[8]) * qz);
                out.border.push(Vec3::new(wx as f32, wy as f32, wz as f32));
            };
            if any_min != 0 {
                emit(qdiv(n0, d0), out);
            }
            if any_max != 0 {
                emit(qdiv(n1, d1), out);
            }
        }
        // transpose R for the second pass (A's axes in B's frame)
        r.swap(1, 3);
        r.swap(2, 6);
        r.swap(5, 7);
    }
    if out.border.is_empty() {
        return false;
    }
    let p0 = out.border[0];
    set_pts(out, p0);
    out.n = Vec3::ZERO;
    set_feat(out, 0x4040_4040);
    true
}

// ------------------------------------------------------------------------------------------
// OVERLAP box–sphere 0x35196CE0, box–ray 0x35195EA0
// ------------------------------------------------------------------------------------------

/// OVERLAP box–sphere (0x35196CE0): `|max(|p| − size, 0)|² < r²` with `p` the centre in box
/// coordinates (the box basis is applied only when `bOriented`); `max(x, 0)` is
/// `(|−x| + x)·0.5`. Sum order `(y² + x²) + z²`.
#[must_use]
pub fn overlap_box_sphere(bx: &BoxPrim, s: &SpherePrim) -> bool {
    let a = bw(bx);
    let (c0, c1, c2) = (a[10], a[11], a[12]);
    let (p0, p1, p2) = if bx.oriented != 0 {
        let v2 = s.center.y - c1;
        let v3 = s.center.x - c0;
        let v4 = s.center.z - c2;
        (
            (v2 * a[1] + v3 * a[0]) + v4 * a[2],
            (v2 * a[4] + v3 * a[3]) + v4 * a[5],
            (v2 * a[7] + v3 * a[6]) + v4 * a[8],
        )
    } else {
        (s.center.x - c0, s.center.y - c1, s.center.z - c2)
    };
    let m = |p: f32, sz: f32| {
        let t = p.abs() - sz;
        (-t).abs() + t
    };
    let v11 = m(p0, a[13]);
    let v12 = m(p2, a[15]);
    let v13 = m(p1, a[14]);
    let h13 = v13 * 0.5;
    let h11 = v11 * 0.5;
    let h12 = v12 * 0.5;
    let d2 = ((h13 * h13 + h11 * h11) + h12 * h12) - s.r * s.r;
    d2.to_bits() >> 31 != 0
}

/// OVERLAP box–ray (0x35195EA0): segment-vs-box separating axes (3 box axes + 3 cross axes),
/// packed SSE. The ray dword after `dir` (`+24`, outside the primitive) is read into lane 3 but
/// never reaches the result.
#[must_use]
pub fn overlap_box_ray(bx: &BoxPrim, r: &RayPrim) -> bool {
    let a = bw(bx);
    let half = F4([r.dir.x, r.dir.y, r.dir.z, 0.0]).mul(F4::splat(0.5));
    let mut v2 = half;
    let mut v3 = F4([r.origin.x, r.origin.y, r.origin.z, r.dir.x]).sub(F4::load(&a, 10));
    if bx.oriented != 0 {
        // rows of the basis as packed columns: lane i = basis[i][k]
        let c = [col4(&a, 0), col4(&a, 1), col4(&a, 2)];
        v2 = mat_cols_mul(c, v2);
        v3 = mat_cols_mul(c, v3);
    }
    let v7 = v3.add(v2);
    let v8 = v2.abs();
    let size = F4::load(&a, 13);
    if any3(size.add(v8).lt(v7.abs())) {
        return false;
    }
    let lhs = size.sh(1).mul(v8.sh(26)).add(size.sh(26).mul(v8.sh(1)));
    let rhs = v7.sh(2).mul(v2.sh(25)).sub(v7.sh(25).mul(v2.sh(2))).abs();
    !any4(lhs.lt(rhs))
}

// ------------------------------------------------------------------------------------------
// OVERLAP box–box 0x351981B0
// ------------------------------------------------------------------------------------------

/// OVERLAP box–box (0x351981B0, SSE4.1 path): the 15-axis separating-axis test with a `1e-4`
/// per-axis slack on the cross axes (`dpps(sizeA, 1e-4)`).
///
/// The CPU check `sub_35199B20` always selects the SSE4.1 path in this binary (its flag is set
/// to 1 unconditionally), so the scalar fallback `sub_35197880` is dead code and not ported.
#[must_use]
pub fn overlap_box_box(ba: &BoxPrim, bb: &BoxPrim) -> bool {
    let a = bw(ba);
    let b = bw(bb);
    let bcols = [col4(&b, 0), col4(&b, 1), col4(&b, 2)];
    let (v7, v8, v9) = if ba.oriented != 0 {
        if bb.oriented != 0 {
            let row = |i: usize| {
                F4::ss(a[3 * i + 1])
                    .sh(0)
                    .mul(bcols[1])
                    .add(F4::ss(a[3 * i]).sh(0).mul(bcols[0]))
                    .add(F4::ss(a[3 * i + 2]).sh(0).mul(bcols[2]))
            };
            (row(0), row(1), row(2))
        } else {
            (F4::load(&a, 0), F4::load(&a, 3), F4::load(&a, 6))
        }
    } else {
        (bcols[0], bcols[1], bcols[2])
    };
    let v13 = v9.abs();
    let v14 = v7.abs();
    let v15 = v8.abs();
    let v16 = v13.shuf(v13, 68);
    let v17 = v14.shuf(v15, 68);
    let v18 = v17.shuf(v16, 136);
    let v19 = v17.shuf(v16, 221);
    let v20 = v14.shuf(v15, 238).shuf(v13.shuf(v13, 238), 136);
    let mut v21 = F4::load(&b, 10).sub(F4::load(&a, 10));
    if ba.oriented != 0 {
        let acols = [col4(&a, 0), col4(&a, 1), col4(&a, 2)];
        v21 = acols[1]
            .mul(v21.sh(85))
            .add(acols[0].mul(v21.sh(0)))
            .add(acols[2].mul(v21.sh(170)));
    }
    let v22 = F4::load(&a, 13);
    let lhs = F4::splat(b[15])
        .mul(v20)
        .add(F4::splat(b[14]).mul(v19))
        .add(F4::splat(b[13]).mul(v18).add(v22));
    if any3(lhs.lt(v21.abs())) {
        return false;
    }
    let v24 = F4::load(&b, 13);
    let v25 = v21.sh(0);
    let v26 = v21.sh(85);
    let v27 = v21.sh(170);
    let v28 = F4::splat(a[14]).mul(v15);
    let v29 = F4::splat(a[15]).mul(v13).add(v28);
    let lhs = v29.add(F4::splat(a[13]).mul(v14).add(v24));
    let rhs = v26.mul(v8).add(v25.mul(v7)).add(v27.mul(v9)).abs();
    if any3(lhs.lt(rhs)) {
        return false;
    }
    let v38 = v22.sh(170);
    let v39 = v24.sh(1);
    let v31 = v24.sh(26);
    let v40 = v22.sh(85);
    let v32 = v22.dp7f(F4::splat(1e-4));
    let lhs = v14
        .sh(1)
        .mul(v31)
        .add(v14.sh(26).mul(v39))
        .add(v13.mul(v40).add(v15.mul(v38)))
        .add(v32);
    let rhs = v27.mul(v8).sub(v26.mul(v9)).abs();
    if any3(lhs.lt(rhs)) {
        return false;
    }
    let v34 = v22.sh(0);
    let lhs = v15
        .sh(1)
        .mul(v31)
        .add(v15.sh(26).mul(v39))
        .add(v13.mul(v34).add(v14.mul(v38)))
        .add(v32);
    let rhs = v25.mul(v9).sub(v27.mul(v7)).abs();
    if any3(lhs.lt(rhs)) {
        return false;
    }
    let lhs = v13
        .sh(1)
        .mul(v31)
        .add(v13.sh(26).mul(v39))
        .add(v15.mul(v34).add(v14.mul(v40)))
        .add(v32);
    let rhs = v26.mul(v7).sub(v25.mul(v8)).abs();
    !any3(lhs.lt(rhs))
}

// ------------------------------------------------------------------------------------------
// OVERLAP box–triangle 0x351968F0
// ------------------------------------------------------------------------------------------

/// Packed words of a triangle (`pt[3]`, `n`); the lane after `n` (outside the primitive) is 0.
fn tri_words(t: &Triangle) -> [f32; 12] {
    [
        t.pt[0].x, t.pt[0].y, t.pt[0].z, t.pt[1].x, t.pt[1].y, t.pt[1].z, t.pt[2].x, t.pt[2].y,
        t.pt[2].z, t.n.x, t.n.y, t.n.z,
    ]
}

/// OVERLAP box–triangle (0x351968F0, SSE4.1 path): triangle AABB vs box (with a `0.25·Σ|edge|`
/// inflation), triangle plane vs box, then the 9 edge × box-axis cross tests.
///
/// Lane 3 of the vertex loads holds the next vertex's `x` (and, for the last one, the normal's
/// `x`) minus `size.x`; it never reaches a decision. The scalar fallback `sub_351960B0` is dead
/// code (see [`overlap_box_box`]).
#[must_use]
pub fn overlap_box_tri(bx: &BoxPrim, t: &Triangle) -> bool {
    let a = bw(bx);
    let w = tri_words(t);
    let c = F4::load(&a, 10);
    let v6 = F4::load(&a, 13);
    let mut v7 = F4::load(&w, 9);
    let mut v8 = F4::load(&w, 0).sub(c);
    let mut v9 = F4::load(&w, 3).sub(c);
    let mut v10 = F4::load(&w, 6).sub(c);
    if bx.oriented != 0 {
        let cols = [col4(&a, 0), col4(&a, 1), col4(&a, 2)];
        v8 = mat_cols_mul(cols, v8);
        v9 = mat_cols_mul(cols, v9);
        v10 = mat_cols_mul(cols, v10);
        v7 = mat_cols_mul(cols, v7);
    }
    let v14 = v8.sub(v9).abs();
    let v15 = v9.sub(v10).abs();
    let v16 = v10.sub(v8).abs();
    let v17 = v15.add(v14).add(v16);
    let lhs = F4::splat(0.25).mul(v17).add(v6).mul(v17);
    let rhs = v10
        .add(v9)
        .mul(v15)
        .add(v9.add(v8).mul(v14))
        .add(v10.add(v8).mul(v16))
        .mul(F4::splat(0.5))
        .abs();
    if any3(lhs.lt(rhs)) {
        return false;
    }
    if any4(v7.abs().dp7f(v6).lt(v7.dp7f(v8).abs())) {
        return false;
    }
    let v = [v8, v9, v10];
    const NEXT: [usize; 3] = [1, 2, 0];
    const PREV: [usize; 3] = [2, 0, 1];
    let v22 = v6.sh(18);
    let v23 = v6.sh(9);
    for i in 0..3 {
        let v24 = v[PREV[i]];
        let v25 = v[NEXT[i]].sub(v[i]);
        let v26 = v25.sh(9);
        let v27 = v25.sh(18);
        let v28 = v[i].sh(18).mul(v26).sub(v[i].sh(9).mul(v27));
        let v29 = v24.sh(18).mul(v26).sub(v24.sh(9).mul(v27));
        let lhs = v28
            .sub(v29)
            .mul(F4::splat(0.5))
            .abs()
            .add(v22.mul(v26).abs())
            .add(v23.mul(v27).abs());
        let rhs = v29.add(v28).mul(F4::splat(0.5)).abs();
        if any3(lhs.lt(rhs)) {
            return false;
        }
    }
    true
}

// ------------------------------------------------------------------------------------------
// OVERLAP box–heightfield 0x351986E0
// ------------------------------------------------------------------------------------------

/// `-1 - ((bits >> 30) & ~1)` as a float: `−sgnnz(x)`.
#[inline]
fn nsgnf(x: f32) -> f32 {
    (-1i32).wrapping_sub(((x.to_bits() as i32) >> 30) & !1) as f32
}

/// `round_magic` result as the raw biased int (`bits − 0x4B400000`).
#[inline]
fn magic_i(x: f32) -> i32 {
    (x.to_bits() as i32).wrapping_sub(1_262_485_504)
}

/// OVERLAP box–heightfield (0x351986E0).
///
/// The box is taken into the heightfield frame; its xy footprint selects the cell rectangle
/// (`(x ∓ ext)·stepr ∓ 0.5` rounded with the `12582912` trick, clamped to `≥ 0` below and
/// `≤ size` above). Large rectangles (`(ix1−ix0)·(iy1−iy0) > 6`) only test whether some vertex
/// height exceeds the box's lowest corner; small ones first require the maximum height
/// (`max(0, …)`) to reach that corner, then run [`overlap_box_tri`] against the two triangles
/// of every cell (the box re-expressed as an oriented box in the heightfield frame).
///
/// Quirks kept: the small-rectangle maximum starts from 0, so a field entirely below 0 is
/// compared against 0; the clamp `max(…, 0)` of the lower bound is the bit trick
/// `~(i >> 31) & i`; the cell loops stop at `< ix1` / `< iy1` (the last row/column of the
/// rectangle is never tested as a triangle pair); `getSurfType` / holes are ignored.
#[must_use]
pub fn overlap_box_heightfield(bx: &BoxPrim, hf: &HeightfieldPrim) -> bool {
    let a = bw(bx);
    let hb = &hf.basis.m;
    let h = [
        hb[0][0], hb[0][1], hb[0][2], hb[1][0], hb[1][1], hb[1][2], hb[2][0], hb[2][1], hb[2][2],
    ];
    let ho = hf.origin;
    // box basis in hf frame: R rows (v29,v14,v34 / v15,v38,v35 / v39,v25,v30), centre (v31,v40,v41)
    let (r, v31, v40, v41);
    if hf.oriented != 0 {
        let m = if bx.oriented != 0 {
            // sub_35022380(box.basis, transpose(hf.basis))
            let ht = [h[0], h[3], h[6], h[1], h[4], h[7], h[2], h[5], h[8]];
            mat_mul_9(&a[..9], &ht)
        } else {
            // (the transposed hf basis, used as is)
            [h[0], h[3], h[6], h[1], h[4], h[7], h[2], h[5], h[8]]
        };
        r = m;
        let v20 = a[11] - ho.y;
        let v21 = a[10] - ho.x;
        let v22 = a[12] - ho.z;
        v31 = ((h[0] * v21) + v20 * h[1]) + v22 * h[2];
        v40 = (h[3] * v21 + (a[11] - ho.y) * h[4]) + (a[12] - ho.z) * h[5];
        v41 = (h[6] * v21 + v20 * h[7]) + v22 * h[8];
    } else {
        r = [a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8]];
        v31 = a[10] - ho.x;
        v40 = a[11] - ho.y;
        v41 = a[12] - ho.z;
    }
    let [v29, v14, v34, v15, v38, v35, v39, v25, v30] = r;
    let v42 = a[13];
    let s1 = a[14];
    let s2 = a[15];
    // flip each box axis so that its z component is negative
    let f0 = nsgnf(v34);
    let f1 = nsgnf(v35);
    let f2 = nsgnf(v30);
    let v45 = v34 * f0;
    let v47 = v15 * f1;
    let v48 = v38 * f1;
    let v157 = v35 * f1;
    let v125 = v39 * f2;
    let v160 = v25 * f2;
    let v127 = -v45;
    let v123 = -(v29 * f0);
    let v49 = -(v14 * f0);
    let v148 = v30 * f2;
    let v50 = ((v157 * s1) + (v45 * v42)) + (v148 * s2);
    let v51 = ((s1 * v47) + (v42 * v123)) + (s2 * v125);
    let v52 = ((s1 * v48) + (v49 * v42)) + (s2 * v160);
    let v55 = -v48;
    let v56 = -v47;
    let v119 = -v49;
    let v58 = s1 * v55;
    let v59 = v42 * -v123;
    let v60 = -v55 * s1;
    let v61 = ((s1 * v56) + v59) + (s2 * v125);
    let v62 = -v56 * s1;
    let v120 = v119 * v42;
    let v63 = s2 * v160;
    let v160n = -v160;
    let v64 = (v58 + v120) + v63;
    let v65 = -v125 * s2;
    let v66 = (v62 + v59) + v65;
    let v67 = v160n * s2;
    let v121 = (v60 + v120) + v67;
    // the oriented box handed to overlap_box_tri: basis rows (v140..v148), centre, size
    let tb = BoxPrim {
        basis: crate::math::Mat33::from_array([
            -v123,
            -v49,
            -v127,
            -v56,
            -v55,
            v157,
            -(-v125),
            -v160n,
            v148,
        ]),
        oriented: 1,
        center: Vec3::new(v31, v40, v41),
        size: Vec3::new(v42, s1, s2),
    };
    let v68 = v51.abs();
    let v69 = v52.abs();
    let v70 = v61.abs();
    let v71 = ((-v68).abs() + v68) * 0.5;
    let v72 = ((-v69).abs() + v69) * 0.5;
    let v73 = (v71 - v70).abs() + (v70 + v71);
    let v74 = v64.abs();
    let v75 = v72 - v74;
    let v76 = v66.abs();
    let v77 = v74 + v72;
    let v78 = v50 + v41;
    let v79 = v121.abs();
    let v80 = (v75.abs() + v77) * 0.5;
    let v81 = (((v73 * 0.5) - v76).abs() + (v76 + (v73 * 0.5))) * 0.5;
    let v82 = ((v80 - v79).abs() + (v79 + v80)) * 0.5;
    let v83 = (((v31 - v81) * hf.stepr[0]) - 0.5) + 12_582_912.0;
    let v84 = (((v40 - v82) * hf.stepr[1]) - 0.5) + 12_582_912.0;
    let v85 = (((v31 + v81) * hf.stepr[0]) + 0.5) + 12_582_912.0;
    let v158 = (((v40 + v82) * hf.stepr[1]) + 0.5) + 12_582_912.0;
    let clamp0 = |m: i32| !(m >> 31) & m;
    let clamp_hi = |m: i32, n: i32| {
        let t = m.wrapping_sub(n);
        n.wrapping_add(t & (t >> 31))
    };
    let mut ix0 = clamp0(magic_i(v83));
    let iy0 = clamp0(magic_i(v84));
    let ix1 = clamp_hi(magic_i(v85), hf.size[0]);
    let iy1 = clamp_hi(magic_i(v158), hf.size[1]);
    let height = |ix: i32, iy: i32| -> f32 {
        let f = hf.get_height.expect("heightfield without getHeight");
        f(ix, iy) * hf.height_scale
    };
    if ix1.wrapping_sub(ix0).wrapping_mul(iy1.wrapping_sub(iy0)) > 6 {
        while ix0 <= ix1 {
            let mut iy = iy0;
            while iy <= iy1 {
                if !(height(ix0, iy) <= v78) {
                    return true;
                }
                iy += 1;
            }
            ix0 += 1;
        }
        return false;
    }
    let mut hmax = 0.0f32;
    let mut ix = ix0;
    while ix <= ix1 {
        let mut iy = iy0;
        while iy <= iy1 {
            let hh = height(ix, iy);
            let lo = hmax - hh;
            let hi = hh + hmax;
            hmax = (lo.abs() + hi) * 0.5;
            iy += 1;
        }
        ix += 1;
    }
    if !(hmax >= v78 && ix0 < ix1) {
        return false;
    }
    let (sx, sy) = (hf.step[0], hf.step[1]);
    while ix0 < ix1 {
        let mut iy = iy0;
        if iy < iy1 {
            let v98 = ix0 as f32;
            loop {
                let x0 = v98 * sx;
                let y0 = iy as f32 * sy;
                let x1 = sx + v98 * sx;
                let y1 = sy + y0;
                let h00 = height(ix0, iy);
                let h10 = height(ix0 + 1, iy);
                iy += 1;
                let h01 = height(ix0, iy);
                let ey = y1 - y0;
                let ex = x1 - x0;
                // triangle (x0,y0,h00) (x1,y0,h10) (x0,y1,h01)
                let t1 = Triangle {
                    pt: [
                        Vec3::new(x0, y0, h00),
                        Vec3::new(x1, y0, h10),
                        Vec3::new(x0, y1, h01),
                    ],
                    n: Vec3::new(
                        0.0 * (h01 - h00) - (h10 - h00) * ey,
                        (h10 - h00) * 0.0 - (h01 - h00) * ex,
                        ey * ex - 0.0 * 0.0,
                    ),
                };
                if overlap_box_tri(&tb, &t1) {
                    return true;
                }
                // triangle (x0,y1,h01) (x1,y0,h10) (x0+sx,y1,h11): pt0 := old pt2, pt1 kept,
                // pt2 := (x0 + step.x, y1, h11)
                let h11 = height(ix0 + 1, iy);
                let bx1 = x0 + sx;
                let e = bx1 - x0;
                let t2 = Triangle {
                    pt: [
                        Vec3::new(x0, y1, h01),
                        Vec3::new(x1, y0, h10),
                        Vec3::new(bx1, y1, h11),
                    ],
                    n: Vec3::new(
                        (y0 - y1) * (h11 - h01) - (h10 - h01) * 0.0,
                        (h10 - h01) * e - (h11 - h01) * (x1 - x0),
                        0.0 * (x1 - x0) - (y0 - y1) * e,
                    ),
                };
                if overlap_box_tri(&tb, &t2) {
                    return true;
                }
                if iy >= iy1 {
                    break;
                }
            }
        }
        ix0 += 1;
    }
    false
}

// ------------------------------------------------------------------------------------------
// table cells
// ------------------------------------------------------------------------------------------

/// An INTERSECT cell over [`Primitive`]: unwraps the two variants the table position
/// guarantees (anything else, impossible through the table, is "no contact").
macro_rules! cell {
    ($name:ident, $pa:ident, $pb:ident, $f:path) => {
        fn $name(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
            match (a, b) {
                (Primitive::$pa(a), Primitive::$pb(b)) => $f(a, b, out),
                _ => false,
            }
        }
    };
}
/// An OVERLAP cell over [`Primitive`] (mismatched variants answer the table default "maybe").
macro_rules! ocell {
    ($name:ident, $pa:ident, $pb:ident, $f:expr) => {
        fn $name(a: &Primitive, b: &Primitive) -> bool {
            match (a, b) {
                (Primitive::$pa(a), Primitive::$pb(b)) => $f(a, b),
                _ => true,
            }
        }
    };
}

cell!(c_box_box, Box, Box, intersect_box_box);
cell!(c_box_ray, Box, Ray, intersect_box_ray);
cell!(c_box_sph, Box, Sphere, intersect_box_sphere);
cell!(c_box_cyl, Box, Cylinder, intersect_box_cylinder);
cell!(c_box_cap, Box, Capsule, intersect_box_capsule);
cell!(c_ray_box, Ray, Box, intersect_ray_box);
cell!(c_sph_box, Sphere, Box, intersect_sphere_box);
cell!(c_cyl_box, Cylinder, Box, intersect_cylinder_box);
cell!(c_cap_box, Capsule, Box, intersect_capsule_box);

ocell!(o_box_box, Box, Box, overlap_box_box);
ocell!(o_box_tri, Box, Triangle, overlap_box_tri);
ocell!(o_box_hf, Box, Heightfield, overlap_box_heightfield);
ocell!(o_box_ray, Box, Ray, overlap_box_ray);
ocell!(o_box_sph, Box, Sphere, overlap_box_sphere);
ocell!(o_ray_box, Ray, Box, |r, b| overlap_box_ray(b, r));
ocell!(o_sph_box, Sphere, Box, |s, b| overlap_box_sphere(b, s));
ocell!(o_tri_box, Triangle, Box, |t, b| overlap_box_tri(b, t));
ocell!(o_hf_box, Heightfield, Box, |h, b| overlap_box_heightfield(
    b, h
));

/// Write this group's INTERSECT / OVERLAP cells.
///
/// INTERSECT: box row (box, ray, sph, cyl, cap) and the ray/sph/cyl/cap–box wrappers.
/// OVERLAP: box row (box, tri, hf, ray, sph) and the ray/sph/tri/hf–box wrappers (the tri and
/// sph rows' box cells are plain swaps of this group's functions; the triangle / round groups
/// may overwrite them with the same behaviour).
pub fn fill(t: &mut PairTables) {
    let set_i = |t: &mut PairTables, a: GeomType, b: GeomType, f: IntersectFn| {
        t.intersect[a.id() as usize][b.id() as usize] = f;
    };
    let set_o = |t: &mut PairTables, a: GeomType, b: GeomType, f: OverlapFn| {
        t.overlap[a.id() as usize][b.id() as usize] = f;
    };
    set_i(t, GeomType::Box, GeomType::Box, c_box_box);
    set_i(t, GeomType::Box, GeomType::Ray, c_box_ray);
    set_i(t, GeomType::Box, GeomType::Sphere, c_box_sph);
    set_i(t, GeomType::Box, GeomType::Cylinder, c_box_cyl);
    set_i(t, GeomType::Box, GeomType::Capsule, c_box_cap);
    set_i(t, GeomType::Ray, GeomType::Box, c_ray_box);
    set_i(t, GeomType::Sphere, GeomType::Box, c_sph_box);
    set_i(t, GeomType::Cylinder, GeomType::Box, c_cyl_box);
    set_i(t, GeomType::Capsule, GeomType::Box, c_cap_box);
    set_o(t, GeomType::Box, GeomType::Box, o_box_box);
    set_o(t, GeomType::Box, GeomType::TriMesh, o_box_tri);
    set_o(t, GeomType::Box, GeomType::Heightfield, o_box_hf);
    set_o(t, GeomType::Box, GeomType::Ray, o_box_ray);
    set_o(t, GeomType::Box, GeomType::Sphere, o_box_sph);
    set_o(t, GeomType::Ray, GeomType::Box, o_ray_box);
    set_o(t, GeomType::Sphere, GeomType::Box, o_sph_box);
    set_o(t, GeomType::TriMesh, GeomType::Box, o_tri_box);
    set_o(t, GeomType::Heightfield, GeomType::Box, o_hf_box);
}

// ------------------------------------------------------------------------------------------
// INTERSECT box–cylinder 0x3515BA70 (+ wrapper cyl–box 0x3515D3F0)
// ------------------------------------------------------------------------------------------

/// Box-local point (double) to world: `c_j + ((B[0][j]·w0 + B[1][j]·w1) + B[2][j]·w2)`,
/// rounded to float once (the emit sequence shared by box–box / box–cyl / box–cap).
#[inline]
fn box_to_world(a: &[f32; 16], w0: f64, w1: f64, w2: f64) -> Vec3 {
    let x = d(a[10]) + ((d(a[0]) * w0 + d(a[3]) * w1) + d(a[6]) * w2);
    let y = d(a[11]) + ((d(a[1]) * w0 + d(a[4]) * w1) + d(a[7]) * w2);
    let z = d(a[12]) + ((d(a[2]) * w0 + d(a[5]) * w1) + d(a[8]) * w2);
    Vec3::new(x as f32, y as f32, z as f32)
}

/// Both sign bits set after `cvtsd2ss` (`and eax, …; js`).
#[inline]
fn both_neg(x: f64, y: f64) -> bool {
    ((x as f32).to_bits() & (y as f32).to_bits()) >> 31 != 0
}

/// The 2-bit "which chord end points lie inside the slab" code of the rim pass; `a`, `p`, `b`
/// are the sign bits of the discriminant, of `v180·v181` and of `v180`.
#[inline]
fn rim_mask(a: u32, p: u32, b: u32) -> u32 {
    let x = b ^ a;
    let hi = (b & p) | ((p ^ 1) & x);
    let lo = ((b ^ 1) & (p ^ 1)) | ((x ^ 1) & p);
    (hi << 1) | lo
}

/// The `{1, 2, 0}` / `{2, 0, 1}` index tables at 0x35209F50 / 0x35209F60.
const T1: [usize; 3] = [1, 2, 0];
const T2: [usize; 3] = [2, 0, 1];

/// INTERSECT box–cylinder (0x3515BA70). Works in the box frame in double precision and collects
/// a border polyline:
///
/// 1. every box edge against both cap discs (edge ∩ cap plane inside the disc and inside the
///    edge), then against the lateral surface (both roots of the edge / infinite-cylinder
///    quadratic, kept when inside the edge and between the caps);
/// 2. for every box axis not (anti)parallel to the cylinder axis (`|a_k| < 0.999`), each cap
///    rim against both box faces of that axis: the chord of the rim on the face plane, its end
///    points kept by a sign-bit decision table when inside the face rectangle;
/// 3. the cylinder axis segment clipped by the box slabs (entry / exit points, `1e-80`
///    tie-broken quotients).
///
/// On contact `pt[0] = pt[1] =` the first border point, `n = 0`, features `0x40` ×4.
///
/// Quirks kept: in pass 2 the rim direction `n` is normalised *in place* inside the inner loop,
/// so later iterations re-normalise the already normalised vector (and keep the `(0,0,1)`
/// fallback once taken); an axis with `|a_k| ≥ 0.999` skips its rims entirely (NaN does not);
/// every inside / outside decision is a sign bit taken after `cvtsd2ss`.
pub fn intersect_box_cylinder(bx: &BoxPrim, cy: &CylinderPrim, out: &mut PrimInters) -> bool {
    let b = bw(bx);
    let geo = CylFrame::new(&b, cy);
    out.border.clear();
    geo.edges_vs_caps_and_side(&b, out);
    geo.rims_vs_faces(&b, out);
    geo.axis_vs_slabs(&b, out);
    finish_border(out)
}

/// On a non-empty border: `pt[0] = pt[1] = border[0]`, `n = 0`, features `0x40` ×4.
fn finish_border(out: &mut PrimInters) -> bool {
    if out.border.is_empty() {
        return false;
    }
    let p0 = out.border[0];
    set_pts(out, p0);
    out.n = Vec3::ZERO;
    set_feat(out, 0x4040_4040);
    true
}

/// A cylinder / capsule in the box frame (all double, from float dot products).
struct CylFrame {
    /// Axis in the box frame.
    a: [f64; 3],
    /// Centre in the box frame.
    c: [f64; 3],
    size: [f64; 3],
    r: f64,
    hh: f64,
}

impl CylFrame {
    fn new(b: &[f32; 16], cy: &CylinderPrim) -> Self {
        let (ax, ay, az) = (cy.axis.x, cy.axis.y, cy.axis.z);
        let dx = cy.center.x - b[10];
        let dy = cy.center.y - b[11];
        let dz = cy.center.z - b[12];
        CylFrame {
            a: [
                d((b[0] * ax + b[1] * ay) + b[2] * az),
                d((b[3] * ax + b[4] * ay) + b[5] * az),
                d((b[6] * ax + b[7] * ay) + b[8] * az),
            ],
            c: [
                d((dx * b[0] + dy * b[1]) + dz * b[2]),
                d((dx * b[3] + dy * b[4]) + dz * b[5]),
                d((dx * b[6] + dy * b[7]) + dz * b[8]),
            ],
            size: [d(b[13]), d(b[14]), d(b[15])],
            r: d(cy.r),
            hh: d(cy.hh),
        }
    }

    /// Pass 1 of box–cylinder: the 12 box edges against both caps and the lateral surface.
    fn edges_vs_caps_and_side(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        let hh2 = hh * 2.0;
        for k in 0..3 {
            let ak = a[k];
            let (t1, t2) = (T1[k], T2[k]);
            let c0ak = c[0] * ak;
            let rak = ak * r;
            let sak = ak.abs() * size[k];
            let c1ak = c[1] * ak;
            let c2ak = c[2] * ak;
            for e in 0..4i32 {
                let mut p = [0.0f64; 3];
                p[t1] = f64::from(2 * (e & 1) - 1) * size[t1];
                p[t2] = f64::from((e & 2) - 1) * size[t2];
                // caps: homogeneous point p·a_k whose k component is the position on the edge
                let v60 =
                    (((c[0] - p[0]) * a[0] + (c[1] - p[1]) * a[1]) + (c[2] - p[2]) * a[2]) - hh;
                let mut q = [p[0] * ak, p[1] * ak, p[2] * ak];
                for along in [v60, v60 + hh2] {
                    q[k] = along;
                    let e0 = q[0] - c0ak;
                    let e1 = q[1] - c1ak;
                    let e2 = q[2] - c2ak;
                    let x = e1 * a[2] - e2 * a[1];
                    let y = a[0] * e2 - e0 * a[2];
                    let z = e0 * a[1] - a[0] * e1;
                    let dist = ((y * y + x * x) + z * z) - rak * rak;
                    if both_neg(along.abs() - sak, dist) {
                        let inv = 1.0 / ak;
                        out.border
                            .push(box_to_world(b, inv * q[0], inv * q[1], inv * q[2]));
                    }
                }
                self.edge_side(b, out, k, &p);
            }
        }
    }

    /// The lateral-surface part of pass 1 (shared by box–cylinder and box–capsule): the edge
    /// `p + t·e_k` against the infinite cylinder `|(x − c) × a|² = r²`; both roots are kept
    /// when inside the edge (`|t| < size_k`) and between the caps.
    fn edge_side(&self, b: &[f32; 16], out: &mut PrimInters, k: usize, p: &[f64; 3]) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        let (t1, t2) = (T1[k], T2[k]);
        let w0 = p[0] - c[0];
        let w1 = p[1] - c[1];
        let w2 = p[2] - c[2];
        let v102 = w1 * a[2] - w2 * a[1];
        let v106 = a[0] * w2 - w0 * a[2];
        let v107 = w0 * a[1] - a[0] * w1;
        let mut ev = [0.0f64; 3];
        ev[t1] = a[t2];
        ev[t2] = -a[t1];
        let (n0, n1, n2) = (-ev[0], -ev[1], -ev[2]);
        let v108 = (n0 * v102 + n1 * v106) + n2 * v107;
        let v109 = (n0 * n0 + n1 * n1) + n2 * n2;
        let v110 = v108 * v108 - (((v102 * v102 + v106 * v106) + v107 * v107) - r * r) * v109;
        if !(v110 > 0.0) {
            return;
        }
        let sq = v110.sqrt();
        let sk = size[k] * v109;
        let c0v = c[0] * v109;
        let c1v = c[1] * v109;
        let c2v = c[2] * v109;
        let hv = v109 * hh;
        let mut q = [p[0] * v109, p[1] * v109, p[2] * v109];
        let mut along = -v108 - sq;
        for _ in 0..2 {
            q[k] = along;
            let h = (((q[1] - c1v) * a[1] + (q[0] - c0v) * a[0]) + (q[2] - c2v) * a[2]).abs() - hv;
            if both_neg(along.abs() - sk, h) {
                let inv = 1.0 / v109;
                out.border
                    .push(box_to_world(b, inv * q[0], inv * q[1], inv * q[2]));
            }
            along += sq * 2.0;
        }
    }

    /// Pass 2 of box–cylinder: both cap rims against the faces of every non-parallel box axis.
    fn rims_vs_faces(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        for k in 0..3 {
            let ak = a[k];
            if ak.abs() >= 0.999 {
                continue;
            }
            let (t1, t2) = (T1[k], T2[k]);
            let mut n = [ak * a[0], ak * a[1], ak * a[2]];
            n[k] -= 1.0;
            for e in 0..4i32 {
                let nk = n[k];
                let n2 = (n[0] * n[0] + n[1] * n[1]) + n[2] * n[2];
                let v165 = f64::from(2 * (e & 1) - 1) * hh;
                let v166 = f64::from((e & 2) - 1) * size[k];
                let v167 = (ak * v165 + c[k]) - v166;
                let lhs = ((v167 * v167) * n2) * nk.abs();
                let rhs = ((nk * r) * (nk * r)) * nk.abs();
                if lhs >= rhs {
                    continue;
                }
                if n2 > f64::from(1e-20f32) {
                    let inv = 1.0 / n2.sqrt();
                    n = [n[0] * inv, n[1] * inv, n[2] * inv];
                } else {
                    n = [0.0, 0.0, 1.0];
                }
                let q = [c[0] + v165 * a[0], v165 * a[1] + c[1], v165 * a[2] + c[2]];
                let nk = n[k];
                let v170 = v166 - q[k];
                let v172 = (nk * r) * (nk * r) - v170 * v170;
                let m = [
                    n[1] * a[2] - n[2] * a[1],
                    a[0] * n[2] - n[0] * a[2],
                    n[0] * a[1] - a[0] * n[1],
                ];
                let side = |t: usize| {
                    let v171 = (nk * size[t]) * (nk * size[t]);
                    let v178 = v170 * n[t] + nk * q[t];
                    let mt = m[t];
                    let v179 = (mt * mt) * v172;
                    let v180 = mt * v178;
                    let v181 = (v171 - v178 * v178) - v179;
                    let disc = ((v180 * 2.0) * (v180 * 2.0)) * v172 - v181 * v181;
                    rim_mask(
                        (disc as f32).to_bits() >> 31,
                        ((v180 * v181) as f32).to_bits() >> 31,
                        (v180 as f32).to_bits() >> 31,
                    )
                };
                let mask = side(t1) & side(t2);
                if mask == 0 {
                    continue;
                }
                let inv = 1.0 / nk;
                let v193 = (((-v172).abs() + v172) * 0.5).sqrt();
                if mask & 1 != 0 {
                    let w0 = inv * (n[0] * v170 - v193 * m[0]) + q[0];
                    let w1 = (n[1] * v170 - m[1] * v193) * inv + q[1];
                    let w2 = (n[2] * v170 - m[2] * v193) * inv + q[2];
                    out.border.push(box_to_world(b, w0, w1, w2));
                }
                if mask & 2 != 0 {
                    let w0 = inv * (n[0] * v170 + v193 * m[0]) + q[0];
                    let w1 = (n[1] * v170 + m[1] * v193) * inv + q[1];
                    let w2 = (n[2] * v170 + m[2] * v193) * inv + q[2];
                    out.border.push(box_to_world(b, w0, w1, w2));
                }
            }
        }
    }

    /// Pass 3 of box–cylinder: the axis segment `c + t·a`, `t ∈ [−hh, hh]`, clipped by the box
    /// slabs; emits the clipped entry and/or exit point.
    fn axis_vs_slabs(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, hh) = (&self.a, &self.c, &self.size, self.hh);
        let (mut n0, mut d0) = (-hh, 1.0f64);
        let (mut n1, mut d1) = (hh, 1.0f64);
        let mut any_min = 0u32;
        let mut any_max = 0u32;
        for j in 0..3 {
            let aj = a[j];
            let cj = c[j];
            let sj = size[j];
            let sgn = (aj as f32).to_bits() >> 31;
            let ad = aj.abs();
            let ds = f64::from(1 - 2 * sgn as i32);
            let lo = (-sj - cj) * ds;
            let hi = (sj - cj) * ds;
            let (e0n, e1n) = if sgn == 0 { (lo, hi) } else { (hi, lo) };
            let c1 = sbit_d(ad * n0 - e0n * d0);
            any_min |= c1;
            let (c1d, nc1d) = (f64::from(c1), f64::from(c1 ^ 1));
            n0 = nc1d * n0 + c1d * e0n;
            d0 = nc1d * d0 + c1d * ad;
            let c2 = sbit_d(d1 * e1n - ad * n1);
            any_max |= c2;
            let (c2d, nc2d) = (f64::from(c2), f64::from(c2 ^ 1));
            n1 = c2d * e1n + nc2d * n1;
            d1 = c2d * ad + nc2d * d1;
        }
        let x = (d1 * n0 - n1 * d0) + (n0 - n1) * QEPS;
        if x >= 0.0 {
            return;
        }
        if any_min != 0 {
            let t = qdiv(n0, d0);
            out.border.push(box_to_world(
                b,
                c[0] + a[0] * t,
                a[1] * t + c[1],
                a[2] * t + c[2],
            ));
        }
        if any_max != 0 {
            let t = qdiv(n1, d1);
            out.border.push(box_to_world(
                b,
                c[0] + a[0] * t,
                t * a[1] + c[1],
                a[2] * t + c[2],
            ));
        }
    }
}
// ------------------------------------------------------------------------------------------
// INTERSECT box–capsule 0x3515D8A0 (+ wrapper cap–box 0x3515F0A0)
// ------------------------------------------------------------------------------------------

/// INTERSECT box–capsule (0x3515D8A0). Same frame and border conventions as
/// [`intersect_box_cylinder`]:
///
/// 1. every box edge against both end spheres (roots kept when inside the edge and beyond the
///    cylinder part along the axis), then against the lateral surface (shared with the
///    cylinder);
/// 2. the axis segment extended by `r` at both ends (`t ∈ [−hh−r, hh+r]`) clipped by the box
///    slabs; this pass updates its quotients in the lerp form `x + (e − x)·c`;
/// 3. both end-sphere centres clamped into the box; the clamped point is kept when closer than
///    `r` (or when the distance is NaN).
///
/// On contact `pt[0] = pt[1] =` the first border point, `n = 0`, features `0x40` ×4.
///
/// Quirks kept: pass 2 clips the *extended* segment against the plain box, so its end points
/// can lie up to `r` outside the capsule surface along the axis; the sphere sign is
/// `sgnnz(float(a_k))`, so for `a_k == -0.0` the "near" sphere is the one at `c − hh·a`.
pub fn intersect_box_capsule(bx: &BoxPrim, cp: &CylinderPrim, out: &mut PrimInters) -> bool {
    let b = bw(bx);
    let geo = CylFrame::new(&b, cp);
    out.border.clear();
    geo.capsule_edges(&b, out);
    geo.capsule_axis_vs_slabs(&b, out);
    geo.capsule_ends_vs_box(&b, out);
    finish_border(out)
}

impl CylFrame {
    /// Pass 1 of box–capsule: every box edge against both end spheres and the lateral surface.
    fn capsule_edges(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        let r2 = r * r;
        for k in 0..3 {
            let ak = a[k];
            let (t1, t2) = (T1[k], T2[k]);
            let sg = f64::from(sgnf(ak as f32));
            let aak = ak.abs();
            let shh = sg * hh;
            let v49 = ak * shh;
            let v50 = -c[k] - v49;
            let o0 = a[0] * shh;
            let o1 = a[1] * shh;
            let o2 = a[2] * shh;
            for e in 0..4i32 {
                let mut p = [0.0f64; 3];
                p[t1] = f64::from(2 * (e & 1) - 1) * size[t1];
                p[t2] = f64::from((e & 2) - 1) * size[t2];
                let v53 =
                    (((c[1] - p[1]) * a[1] + (c[0] - p[0]) * a[0]) + (c[2] - p[2]) * a[2]) * sg;
                let v54 = v53 + hh;
                let v56 = v53 - hh;
                let sum_ = v56 + v54;
                let dif = (v56 - v54).abs();
                // a root t is kept when |t| < size_k and its axial position lies beyond the
                // cylinder part (sign bit of |sum − 2·t·|a_k|| − |dif| clear)
                let emit = |t: f64, out: &mut PrimInters, p: &[f64; 3]| {
                    let ax = ((sum_ - (t * aak) * 2.0).abs() - dif) as f32;
                    let inside = (t.abs() - size[k]) as f32;
                    if (!ax.to_bits() & inside.to_bits()) >> 31 != 0 {
                        let mut q = *p;
                        q[k] = t;
                        out.border.push(box_to_world(b, q[0], q[1], q[2]));
                    }
                };
                // sphere at c + sg·hh·a
                let x = (p[0] - c[0]) - o0;
                let y = (p[1] - c[1]) - o1;
                let z = (p[2] - c[2]) - o2;
                let v55 = v50 * v50 - (((x * x + y * y) + z * z) - r2);
                if v55 >= 0.0 {
                    let v57 = v55.sqrt();
                    emit(-v50 - v57, out, &p);
                    emit(v57 - v50, out, &p);
                }
                // sphere at c − sg·hh·a
                let v96 = v49 - c[k];
                let x = (p[0] - c[0]) + o0;
                let y = (p[1] - c[1]) + o1;
                let z = (p[2] - c[2]) + o2;
                let v97 = v96 * v96 - (((y * y + x * x) + z * z) - r2);
                if v97 >= 0.0 {
                    let v98 = v97.sqrt();
                    emit(-v96 - v98, out, &p);
                    emit(v98 - v96, out, &p);
                }
                self.edge_side(b, out, k, &p);
            }
        }
    }

    /// Pass 2 of box–capsule: the axis segment extended by `r` clipped by the box slabs.
    fn capsule_axis_vs_slabs(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        let (mut n0, mut d0) = (-hh - r, 1.0f64);
        let (mut n1, mut d1) = (hh + r, 1.0f64);
        let mut any_min = 0u32;
        let mut any_max = 0u32;
        for j in 0..3 {
            let aj = a[j];
            let cj = c[j];
            let sj = size[j];
            let sgn = (aj as f32).to_bits() >> 31;
            let ad = aj.abs();
            let ds = f64::from(1 - 2 * sgn as i32);
            let lo = (-sj - cj) * ds;
            let hi = (sj - cj) * ds;
            let (e0n, e1n) = if sgn == 0 { (lo, hi) } else { (hi, lo) };
            let c1 = sbit_d(ad * n0 - e0n * d0);
            any_min |= c1;
            let c1d = f64::from(c1);
            n0 += (e0n - n0) * c1d;
            d0 += (ad - d0) * c1d;
            let c2 = sbit_d(e1n * d1 - ad * n1);
            any_max |= c2;
            let c2d = f64::from(c2);
            n1 += (e1n - n1) * c2d;
            d1 += (ad - d1) * c2d;
        }
        let x = (n0 * d1 - n1 * d0) + (n0 - n1) * QEPS;
        if x >= 0.0 {
            return;
        }
        if any_min != 0 {
            let t = qdiv(n0, d0);
            out.border.push(box_to_world(
                b,
                c[0] + a[0] * t,
                a[1] * t + c[1],
                a[2] * t + c[2],
            ));
        }
        if any_max != 0 {
            let t = qdiv(n1, d1);
            out.border.push(box_to_world(
                b,
                c[0] + a[0] * t,
                t * a[1] + c[1],
                t * a[2] + c[2],
            ));
        }
    }

    /// Pass 3 of box–capsule: both end-sphere centres (`c − hh·a`, then `c + hh·a`) clamped
    /// into the box, as box–sphere does it (in double).
    fn capsule_ends_vs_box(&self, b: &[f32; 16], out: &mut PrimInters) {
        let (a, c, size, r, hh) = (&self.a, &self.c, &self.size, self.r, self.hh);
        let r2 = r * r;
        let hh2 = hh * 2.0;
        let step = [a[0] * hh2, a[1] * hh2, a[2] * hh2];
        let mut s = [c[0] - a[0] * hh, c[1] - a[1] * hh, c[2] - a[2] * hh];
        for _ in 0..2 {
            let clamp = |sz: f64, x: f64| {
                let t = sz - x.abs();
                ((t - (-t).abs()) * 0.5) * f64::from(sgnf(x as f32))
            };
            let o0 = clamp(size[0], s[0]);
            let o1 = clamp(size[1], s[1]);
            let o2 = clamp(size[2], s[2]);
            let d2 = (o1 * o1 + o0 * o0) + o2 * o2;
            if !(d2 >= r2) {
                out.border
                    .push(box_to_world(b, o0 + s[0], o1 + s[1], o2 + s[2]));
            }
            s = [s[0] + step[0], s[1] + step[1], s[2] + step[2]];
        }
    }
}
pub fn intersect_cylinder_box(c: &CylinderPrim, b: &BoxPrim, out: &mut PrimInters) -> bool {
    let r = intersect_box_cylinder(b, c, out);
    reverse_feat(out);
    neg_n(out);
    r
}
pub fn intersect_capsule_box(c: &CylinderPrim, b: &BoxPrim, out: &mut PrimInters) -> bool {
    let r = intersect_box_capsule(b, c, out);
    reverse_feat(out);
    neg_n(out);
    r
}

#[cfg(test)]
mod tests {
    //! Behavioural unit tests. Bit-exactness against the DLL is covered by
    //! `tests/p2a1_box_pairs.rs` (oracle probes, goldens).
    use super::*;
    use crate::math::{Mat33, Quat};

    /// Deterministic generator (SplitMix64) for the random configurations.
    struct Rng(u64);
    impl Rng {
        fn next(&mut self) -> u64 {
            self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
            let mut z = self.0;
            z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
            z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
            z ^ (z >> 31)
        }
        fn f(&mut self, lo: f32, hi: f32) -> f32 {
            lo + (hi - lo) * ((self.next() >> 40) as f32 / 16_777_216.0)
        }
        fn v(&mut self, lo: f32, hi: f32) -> Vec3 {
            Vec3::new(self.f(lo, hi), self.f(lo, hi), self.f(lo, hi))
        }
        fn rot(&mut self) -> Mat33 {
            let q = Quat::new(
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
            );
            Mat33::from_quat(q.normalized_xwyz())
        }
        fn boxp(&mut self) -> BoxPrim {
            BoxPrim {
                basis: self.rot(),
                oriented: 1,
                center: self.v(-1.0, 1.0),
                size: self.v(0.1, 1.0),
            }
        }
    }

    fn unit_box() -> BoxPrim {
        BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0))
    }

    /// `p` in the box (in its frame) with tolerance `eps`.
    fn inside_box(b: &BoxPrim, p: Vec3, eps: f32) -> bool {
        let l = b.basis.mul_vec(p - b.center);
        (0..3).all(|i| l[i].abs() <= b.size[i] + eps)
    }

    fn inside_cyl(c: &CylinderPrim, p: Vec3, capsule: bool, eps: f32) -> bool {
        let d = p - c.center;
        let h = d.dot(c.axis);
        if capsule {
            let t = h.clamp(-c.hh, c.hh);
            (d - c.axis * t).len() <= c.r + eps
        } else {
            h.abs() <= c.hh + eps && (d - c.axis * h).len() <= c.r + eps
        }
    }

    #[test]
    fn sphere_on_a_face() {
        let mut o = PrimInters::default();
        let s = SpherePrim {
            center: Vec3::new(0.25, -0.25, 1.375),
            r: 0.5,
        };
        assert!(intersect_box_sphere(&unit_box(), &s, &mut o));
        assert_eq!(o.pt[0], Vec3::new(0.25, -0.25, 1.0));
        assert_eq!(o.pt[1], o.pt[0]);
        assert_eq!(o.border, vec![o.pt[0]]);
        assert_eq!(o.n, Vec3::new(0.0, 0.0, 0.375));
        assert_eq!(o.i_feature, [[0x40, 0x40], [0x40, 0x40]]);
        let far = SpherePrim {
            center: Vec3::new(0.0, 0.0, 1.625),
            r: 0.5,
        };
        assert!(!intersect_box_sphere(&unit_box(), &far, &mut o));
        // fields are still written on a miss (as in the binary)
        assert_eq!(o.pt[0], Vec3::new(0.0, 0.0, 1.0));
        // the wrapper reverses the features and flips the normal
        let mut w = PrimInters::default();
        assert!(intersect_sphere_box(&s, &unit_box(), &mut w));
        assert_eq!(w.n, Vec3::new(-0.0, 0.0, -0.375));
        assert!(overlap_box_sphere(&unit_box(), &s));
        assert!(!overlap_box_sphere(&unit_box(), &far));
    }

    #[test]
    fn ray_entry_exit_and_misses() {
        let b = unit_box();
        let mut o = PrimInters::default();
        let down = RayPrim {
            origin: Vec3::new(0.5, 0.25, 3.0),
            dir: Vec3::new(0.0, 0.0, -4.0),
        };
        assert!(intersect_box_ray(&b, &down, &mut o));
        assert_eq!(o.pt[0], Vec3::new(0.5, 0.25, 1.0));
        assert_eq!(o.n, Vec3::new(0.0, 0.0, 1.0));
        assert_eq!(o.i_feature, [[0x40, 0x20], [0x40, 0x20]]);
        assert!(overlap_box_ray(&b, &down));
        // starting inside: the exit point is reported
        let out_x = RayPrim {
            origin: Vec3::ZERO,
            dir: Vec3::new(2.0, 0.0, 0.0),
        };
        assert!(intersect_box_ray(&b, &out_x, &mut o));
        assert_eq!(o.pt[0], Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(o.n, Vec3::new(1.0, 0.0, 0.0));
        // too short, parallel outside, wholly inside
        let short = RayPrim {
            origin: Vec3::new(0.0, 0.0, 3.0),
            dir: Vec3::new(0.0, 0.0, -1.0),
        };
        let parallel = RayPrim {
            origin: Vec3::new(0.0, 1.5, 3.0),
            dir: Vec3::new(0.0, 0.0, -6.0),
        };
        let inner = RayPrim {
            origin: Vec3::new(-0.5, 0.0, 0.0),
            dir: Vec3::new(1.0, 0.0, 0.0),
        };
        for r in [short, parallel, inner] {
            let mut o = PrimInters::default();
            assert!(!intersect_box_ray(&b, &r, &mut o), "{r:?}");
        }
        assert!(!overlap_box_ray(&b, &short));
        assert!(!overlap_box_ray(&b, &parallel));
        // the wrapper only reverses the features
        let mut w = PrimInters::default();
        assert!(intersect_ray_box(&down, &b, &mut w));
        assert_eq!(w.n, Vec3::new(0.0, 0.0, 1.0));
        assert_eq!(w.i_feature, [[0x20, 0x40], [0x20, 0x40]]);
    }

    #[test]
    fn box_box_border_lies_in_both_boxes() {
        let mut rng = Rng(1);
        let mut hits = 0;
        for _ in 0..3000 {
            let a = rng.boxp();
            let b = rng.boxp();
            let mut o = PrimInters::default();
            let hit = intersect_box_box(&a, &b, &mut o);
            assert_eq!(hit, !o.border.is_empty());
            if hit {
                hits += 1;
                assert_eq!(o.pt[0], o.border[0]);
                assert_eq!(o.n, Vec3::ZERO);
                for p in &o.border {
                    assert!(inside_box(&a, *p, 1e-4) && inside_box(&b, *p, 1e-4));
                }
                assert!(overlap_box_box(&a, &b), "intersecting boxes must overlap");
            }
        }
        assert!(hits > 300, "{hits}");
        // stacked, separated
        let lower = unit_box();
        let upper = BoxPrim::aligned(Vec3::new(0.3, 0.0, 1.9), Vec3::splat(1.0));
        let mut o = PrimInters::default();
        assert!(intersect_box_box(&lower, &upper, &mut o));
        assert!(o
            .border
            .iter()
            .all(|p| p.z >= 0.9 - 1e-6 && p.z <= 1.0 + 1e-6));
        let apart = BoxPrim::aligned(Vec3::new(0.0, 0.0, 2.5), Vec3::splat(1.0));
        assert!(!intersect_box_box(&lower, &apart, &mut o));
        assert!(o.border.is_empty());
        assert!(!overlap_box_box(&lower, &apart));
    }

    fn round_case(capsule: bool) {
        let mut rng = Rng(if capsule { 3 } else { 2 });
        let mut hits = 0;
        for _ in 0..3000 {
            let b = rng.boxp();
            let c = CylinderPrim {
                center: rng.v(-1.5, 1.5),
                axis: rng.v(-1.0, 1.0).normalized(),
                r: rng.f(0.1, 0.8),
                hh: rng.f(0.0, 1.0),
            };
            let mut o = PrimInters::default();
            let hit = if capsule {
                intersect_box_capsule(&b, &c, &mut o)
            } else {
                intersect_box_cylinder(&b, &c, &mut o)
            };
            assert_eq!(hit, !o.border.is_empty());
            if hit {
                hits += 1;
                assert_eq!(o.pt[0], o.border[0]);
                for p in &o.border {
                    assert!(inside_box(&b, *p, 1e-3), "{p:?} outside the box");
                    // capsule pass 2 clips the axis extended by r: those points can be up to
                    // r beyond the capsule along the axis
                    let slack = if capsule { c.r * 1.01 } else { 1e-3 };
                    assert!(inside_cyl(&c, *p, capsule, slack), "{p:?} outside {c:?}");
                }
                // wrapper: same border, reversed features, negated (zero) normal
                let mut w = PrimInters::default();
                let hw = if capsule {
                    intersect_capsule_box(&c, &b, &mut w)
                } else {
                    intersect_cylinder_box(&c, &b, &mut w)
                };
                assert!(hw);
                assert_eq!(w.border, o.border);
                assert_eq!(w.n.to_bits(), [0x8000_0000; 3]);
            }
        }
        assert!(hits > 300, "{hits}");
    }

    #[test]
    fn box_cylinder_border_lies_in_both() {
        round_case(false);
    }

    #[test]
    fn box_capsule_border_lies_in_both() {
        round_case(true);
    }

    #[test]
    fn upright_cylinder_on_a_box_face() {
        // axis parallel to a box axis: pass 2 skips that axis (|a_k| >= 0.999)
        let c = CylinderPrim {
            center: Vec3::new(0.125, 0.25, 1.25),
            axis: Vec3::Z,
            r: 0.375,
            hh: 0.375,
        };
        let mut o = PrimInters::default();
        assert!(intersect_box_cylinder(&unit_box(), &c, &mut o));
        assert!(o.border.iter().all(|p| p.z <= 1.0));
        let cap = CylinderPrim { hh: 0.0, ..c };
        let mut o2 = PrimInters::default();
        assert!(intersect_box_capsule(&unit_box(), &cap, &mut o2));
    }

    #[test]
    fn box_triangle_and_heightfield_overlaps() {
        let b = unit_box();
        let tri = |z: f32| Triangle {
            pt: [
                Vec3::new(-2.0, -2.0, z),
                Vec3::new(2.0, -2.0, z),
                Vec3::new(0.0, 2.0, z),
            ],
            n: Vec3::Z,
        };
        assert!(overlap_box_tri(&b, &tri(0.5)));
        assert!(overlap_box_tri(&b, &tri(-0.99)));
        assert!(!overlap_box_tri(&b, &tri(1.25)));
        fn h(ix: i32, iy: i32) -> f32 {
            ((ix + iy) & 1) as f32 * 0.5
        }
        let hf = HeightfieldPrim {
            size: [64, 64],
            get_height: Some(h),
            ..HeightfieldPrim::default()
        };
        // a small box above / into the terrain, both rectangle-size branches
        let above = BoxPrim::aligned(Vec3::new(10.25, 10.25, 2.0), Vec3::splat(0.375));
        let into = BoxPrim::aligned(Vec3::new(10.25, 10.25, 0.375), Vec3::splat(0.375));
        let big_into = BoxPrim::aligned(Vec3::new(10.25, 10.25, 0.125), Vec3::splat(3.0));
        assert!(!overlap_box_heightfield(&above, &hf));
        assert!(overlap_box_heightfield(&into, &hf));
        assert!(overlap_box_heightfield(&big_into, &hf));
    }

    #[test]
    fn tables_route_to_this_group() {
        let t = PairTables::build();
        let b = Primitive::Box(unit_box());
        let s = Primitive::Sphere(SpherePrim {
            center: Vec3::new(0.0, 0.0, 1.25),
            r: 0.5,
        });
        let mut o = PrimInters::default();
        assert!((t.intersect_fn(GeomType::Box, GeomType::Sphere))(
            &b, &s, &mut o
        ));
        assert!((t.intersect_fn(GeomType::Sphere, GeomType::Box))(
            &s, &b, &mut o
        ));
        let b2 = Primitive::Box(BoxPrim::aligned(
            Vec3::new(0.5, 0.25, 1.5),
            Vec3::splat(1.0),
        ));
        assert!((t.intersect_fn(GeomType::Box, GeomType::Box))(
            &b, &b2, &mut o
        ));
        // original quirk: two coincident boxes have every edge on the other's faces, nothing is
        // clipped, so no border point and no contact
        assert!(!(t.intersect_fn(GeomType::Box, GeomType::Box))(
            &b, &b, &mut o
        ));
        let far = Primitive::Sphere(SpherePrim {
            center: Vec3::new(0.0, 0.0, 9.0),
            r: 0.5,
        });
        assert!(!(t.overlap_fn(GeomType::Box, GeomType::Sphere))(&b, &far));
        assert!(!(t.overlap_fn(GeomType::Sphere, GeomType::Box))(&far, &b));
        // cylinder / capsule columns stay "maybe" in OVERLAP
        let c = Primitive::Cylinder(CylinderPrim::default());
        assert!((t.overlap_fn(GeomType::Box, GeomType::Cylinder))(&b, &c));
    }
}
