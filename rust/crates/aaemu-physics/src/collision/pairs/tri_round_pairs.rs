//! **P2a group 3 — triangle vs cylinder / capsule** (INTERSECT cells `[tri][cyl]`, `[tri][cap]`
//! and their swap wrappers). Ported from the x64 10.x `cryphysics_dedicate.dll`:
//!
//! | cell | x64 | x86 (1.2) | size | port |
//! |---|---|---|---|---|
//! | tri–cyl | 0x35164830 | 0x3516F230 | 14,789 B | [`tri_cylinder`] |
//! | tri–cap | 0x35168280 | 0x35172FC0 | 10,996 B | [`tri_capsule`] |
//! | cyl–tri | 0x35168200 | 0x35172F60 | 109 B | [`cylinder_tri`] |
//! | cap–tri | 0x3516AD80 | 0x351759A0 | 109 B | [`capsule_tri`] |
//!
//! The OVERLAP cells of these pairs are the default `ret1` and are not touched here.
//!
//! **Exactness.** Every arithmetic step keeps the precision the disassembly uses (Hex-Rays hides
//! several `cvtps2pd` promotions, e.g. the slab dot products of the edge loop are all double)
//! and the association of the decompile. Comparisons follow the branch the compiler emitted,
//! which matters for NaN and `-0.0`:
//!
//! * `comiss x, 0; jae skip` (Hex-Rays "`x < 0`", all triangle edge tests and the
//!   quotient `min`s) is `!(x >= 0.0)`, so NaN passes;
//! * `comiss a, b; jbe skip` ("`a > b`") is the ordered `a > b`;
//! * `x <= eps` normalisation guards are `!(x > eps)` (NaN takes the degenerate branch);
//! * `d == 0.0` division guards are `ucomisd; je`, true for NaN;
//! * Hex-Rays "`v >= 0.0`" on a float living in an integer register is a sign-bit test
//!   (`-0.0` counts as negative).
//!
//! A single operation on two float operands evaluated in double and rounded back to float
//! equals the float operation (53 ≥ 2·24 + 2, so double rounding is innocuous); only where a
//! double intermediate feeds a further operation does the port keep `f64`.
//!
//! Verified bit for bit against the DLL with `physoracle --pairfn` (`tests/p2a3_tri_round.rs`):
//! 4 × 1,000,000 random cases (edge / face / axis-parallel / flat paths, world-scale
//! coordinates, ±0, denormals, degenerate triangles and shapes, all border capacities) match
//! exactly. **Known deviation:** inputs containing NaN or ±inf (or values that overflow to
//! inf) can differ (~0.05 % of such cases): when both operands of an SSE `add`/`mul` are NaN
//! the result is the first operand's NaN, so the sign of a NaN — which the sign-bit tests
//! read — depends on operand order, and LLVM commutes those operands freely. Finite inputs of
//! sane magnitude never reach that state.
//!
//! **Quirks of the original, kept on purpose:**
//! * A triangle with all three vertices inside the shape produces **no contact** (the edge walk
//!   needs a vertex outside to start from).
//! * The edge walk picks, among the edge points where the triangle boundary enters the shape,
//!   the one **closest to the incoming `out.pt[1]`**, i.e. to whatever the caller's
//!   `prim_inters` held before the call (stale data from the previous pair in the original
//!   caller). The result therefore depends on the initial contents of `out`.
//! * Fields are written only on the paths that write them in the original (`n`, `n_alt`,
//!   `unk_84` are left untouched by the edge-contact path when no rim point qualifies).
//! * The edge walk runs `i → prev(i)` (table 0x35209F60 = {2,0,1}) and the triangle feature
//!   byte it stores is `prev(i) | 0xA0`, the far end of the edge.
//! * Border points are written to `ptborder[nborderpt]` *before* the capacity check, so with a
//!   full buffer the original writes one element past `nbordersz` (harmless in the original
//!   caller, whose buffer is larger than 15 points). The port only keeps counted points.
//! * The cylinder cap circle is sampled at 8 points, by an unrolled table in the face case and
//!   by a 45° rotation evaluated in double precision in the axis-parallel case; the two sets of
//!   points differ in the last bits.
//! * The capsule case clips the sphere-extended edge interval with the infinite cylinder
//!   (valid, the end spheres lie inside it), and replaces the slab interval with a sphere
//!   interval wholesale when the two do not overlap.

// The negated comparisons are deliberate: they reproduce the NaN behaviour of the compiled
// branches (see above).
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::PairTables;
use crate::collision::contact::PrimInters;
use crate::geometry::primitives::{CylinderPrim, Primitive, Triangle};
use crate::ids::GeomType;
use crate::math::Vec3;

type V = [f32; 3];

/// `dword_35209F50`: next vertex index.
const NEXT: [usize; 3] = [1, 2, 0];
/// `dword_35209F60`: previous vertex index.
const PREV: [usize; 3] = [2, 0, 1];

/// `cos`/`sin` of the unrolled 8-point cap circle (0x35166741…, `0x3F3504F3` = 0.70710677).
const H: f32 = f32::from_bits(0x3F35_04F3);
const CIRCLE_COS: [f32; 8] = [1.0, H, 0.0, -H, -1.0, -H, 0.0, H];
const CIRCLE_SIN: [f32; 8] = [0.0, H, 1.0, H, 0.0, -H, -1.0, -H];
/// The double rotation constant of the axis-parallel cap loop (0x351F2CE0, 0x3FE6A09E667F3BCD).
const SQRT1_2_D: f64 = std::f64::consts::FRAC_1_SQRT_2;
/// 1e-20 as stored in the binary (float 0x1E3CE508).
const EPS20_F: f32 = f32::from_bits(0x1E3C_E508);
/// 0.80999994 (float 0x3F4F5C28): "axis mostly along x" threshold for the cap basis.
const PERP_K: f32 = f32::from_bits(0x3F4F_5C28);
/// -0.2 (float 0xBE4CCCCD): plane-distance slack factor.
const SLACK: f32 = f32::from_bits(0xBE4C_CCCD);

#[inline]
fn v3(v: Vec3) -> V {
    [v.x, v.y, v.z]
}

#[inline]
fn vec(v: V) -> Vec3 {
    Vec3::new(v[0], v[1], v[2])
}

#[inline]
fn sbit(x: f32) -> u32 {
    x.to_bits() >> 31
}

#[inline]
fn sub(a: V, b: V) -> V {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

#[inline]
fn d64(v: V) -> [f64; 3] {
    [f64::from(v[0]), f64::from(v[1]), f64::from(v[2])]
}

/// `(a.x·b.x + a.y·b.y) + a.z·b.z` in single precision.
#[inline]
fn dotf(a: V, b: V) -> f32 {
    (a[0] * b[0] + a[1] * b[1]) + a[2] * b[2]
}

/// `±1` from the sign bit of a float: `((bits >> 30) & ~1) + 1` (`-0.0` → -1).
#[inline]
fn sgn1(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// `!(x >= 0)` — the `comiss; jae skip` test (NaN passes).
#[inline]
fn not_ge0(x: f32) -> bool {
    !(x >= 0.0)
}

/// Division guarded by `ucomisd d, 0; je` (true for NaN): `d == 0 → 0`, else `n / d`.
#[inline]
fn guarded_div(n: f64, d: f64) -> f64 {
    if d == 0.0 || d.is_nan() {
        0.0
    } else {
        n / d
    }
}

/// `((pt − v)×e)·n` for the triangle edge `v → v + e`, in single precision with the binary's
/// association `(cx·nx + cy·ny) + cz·nz`.
#[inline]
fn edge_side(pt: V, v: V, e: V, n: V) -> f32 {
    let d = sub(pt, v);
    let cx = d[1] * e[2] - d[2] * e[1];
    let cy = d[2] * e[0] - d[0] * e[2];
    let cz = d[0] * e[1] - d[1] * e[0];
    (cx * n[0] + cy * n[1]) + cz * n[2]
}

/// The three edge tests the rim / border points use (edges `p0→p1`, `p1→p2`, `p2→p0`), each
/// `!(side >= 0)`.
#[inline]
fn inside_tri(pt: V, p: &[V; 3], n: V) -> bool {
    not_ge0(edge_side(pt, p[0], sub(p[1], p[0]), n))
        && not_ge0(edge_side(pt, p[1], sub(p[2], p[1]), n))
        && not_ge0(edge_side(pt, p[2], sub(p[0], p[2]), n))
}

/// The "within slack of the plane" test written as
/// `isneg(|k·-0.2 − 2·((pt − p0)·n)| − |k·-0.2|)`, i.e. `(pt − p0)·n ∈ (k·-0.2, 0)` (float).
#[inline]
fn near_below_plane(pt: V, p0: V, n: V, k: f32) -> bool {
    let lim = k * SLACK;
    let d = dotf(sub(pt, p0), n) * 2.0;
    sbit((lim - d).abs() - lim.abs()) != 0
}

#[inline]
fn border_room(out: &PrimInters) -> bool {
    (out.border.len() as i64) < i64::from(out.border_max)
}

/// `if (nborderpt < nbordersz) ptborder[nborderpt++] = pt`.
#[inline]
fn push_border(out: &mut PrimInters, pt: V) {
    if border_room(out) {
        out.border.push(vec(pt));
    }
}

/// Write `ptborder[nborderpt] = pt; nborderpt += (nborderpt < nbordersz) & cond` (the write
/// happens unconditionally in the original; only counted points are kept here).
#[inline]
fn push_border_if(out: &mut PrimInters, pt: V, cond: bool) {
    if cond && border_room(out) {
        out.border.push(vec(pt));
    }
}

/// Unit vector perpendicular to the axis (the `0.81·|a|² − a.x²` index trick with the
/// NEXT/PREV tables), normalised with the `1e-20` guard (`(0,0,1)` when degenerate).
fn perp_unit(a: V) -> V {
    let i = sbit(((a[1] * a[1] + a[0] * a[0]) + a[2] * a[2]) * PERP_K - a[0] * a[0]) as usize;
    let mut u = [0.0f32; 3];
    u[i] = 0.0;
    u[NEXT[i]] = a[PREV[i]];
    u[PREV[i]] = -a[NEXT[i]];
    let l2 = (u[0] * u[0] + u[1] * u[1]) + u[2] * u[2];
    if !(l2 > EPS20_F) {
        [0.0, 0.0, 1.0]
    } else {
        let inv = 1.0 / l2.sqrt();
        [u[0] * inv, u[1] * inv, u[2] * inv]
    }
}

/// Second rim point along the axis (shared by both pairs): from `base` (a point on the shape
/// closest to the plane) go along `sgn·a` by `2·hh`, or only to the triangle plane when it is
/// closer, and add the point if it lies in the plane slab and inside the triangle.
#[allow(clippy::too_many_arguments)]
fn axis_border_point(
    out: &mut PrimInters,
    p: &[V; 3],
    n: V,
    a: V,
    hh: f32,
    r: f32,
    base: V,
    sgn: i32,
) {
    let dist = f64::from(dotf(sub(p[0], base), n));
    let hh2 = hh * 2.0;
    let na = f64::from(dotf(n, a).abs());
    let full = f64::from(hh2) * na;
    let test = ((full - dist * 2.0).abs() - (-full).abs()) as f32;
    let f = if sbit(test) == 0 {
        sgn as f32 * hh2
    } else {
        (f64::from(sgn) * guarded_div(dist, na)) as f32
    };
    let s = [base[0] + a[0] * f, base[1] + a[1] * f, base[2] + a[2] * f];
    if near_below_plane(s, p[0], n, hh + r) && inside_tri(s, p, n) {
        push_border(out, s);
    }
}

/// Unpacked triangle.
struct Tri {
    p: [V; 3],
    n: V,
}

impl Tri {
    fn new(t: &Triangle) -> Self {
        Tri {
            p: [v3(t.pt[0]), v3(t.pt[1]), v3(t.pt[2])],
            n: v3(t.n),
        }
    }
}

/// Slab/sphere/cylinder clip intervals of the edge walk: per edge `[t0.num, t0.den, t1.num,
/// t1.den]` as doubles (`v626` / `v293`).
type Quots = [f64; 12];

/// The entry/exit tracking of the edge walk (identical in both pairs).
struct Walk {
    /// Best (closest-to-`out.pt[1]`) distance as a quotient `best_num / best_den` inverted:
    /// `v44 / v40`.
    q_den2: f64,
    q_dist2: f64,
    /// `v42`: exit preceding the chosen entry.
    exit_before: i32,
    /// `v45`: chosen entry `edge*2 + end`.
    entry: i32,
    /// `v46`: latest exit.
    exit_last: i32,
    /// `v47`: inside toggle.
    inside: u32,
}

impl Walk {
    fn new() -> Self {
        Walk {
            q_den2: 0.0,
            q_dist2: 1.0,
            exit_before: -1,
            entry: -1,
            exit_last: -1,
            inside: 0,
        }
    }

    /// The two interval ends of edge `i` (from `p[i]` towards `p[prev(i)]`).
    fn visit(&mut self, p: &[V; 3], i: usize, q: &Quots, ref_pt: V) {
        let j = PREV[i];
        let e = sub(p[j], p[i]);
        let dx = f64::from(p[i][0] - ref_pt[0]);
        let dy = f64::from(p[i][1] - ref_pt[1]);
        let dz = f64::from(p[i][2] - ref_pt[2]);
        let edge2 = 2 * i as i32;
        for k in 0..2usize {
            let num = q[4 * i + 2 * k];
            let den = q[4 * i + 2 * k + 1];
            // 0 < t < 1 as the sign of |2·num − den| − den
            let s = sbit(((num * 2.0 - den).abs() - den) as f32);
            let idx = k as i32 | edge2;
            let m = self.inside & s;
            self.exit_last = (idx & (m as i32).wrapping_neg()) | ((m as i32 - 1) & self.exit_last);
            let numf = num as f32;
            let den2 = den * den;
            let tx = f64::from(e[0] * numf) + den * dx;
            let ty = f64::from(e[1] * numf) + den * dy;
            let tz = f64::from(e[2] * numf) + den * dz;
            let dist2 = (ty * ty + tx * tx) + tz * tz;
            let closer = (self.q_den2 * dist2 - den2 * self.q_dist2) as f32;
            let cond = s & (self.inside ^ 1) & sbit(closer);
            let keep = f64::from(1 - cond as i32);
            let take = f64::from(cond as i32);
            self.q_den2 = keep * self.q_den2 + take * den2;
            self.q_dist2 = keep * self.q_dist2 + take * dist2;
            let mask = (cond as i32).wrapping_neg();
            let nmask = cond as i32 - 1;
            self.inside ^= s;
            self.exit_before = (self.exit_last & mask) | (nmask & self.exit_before);
            self.entry = (self.entry & nmask) | (mask & idx);
        }
    }

    /// `v361 = v42 < 0 ? v46 : v42`.
    fn exit(&self) -> i32 {
        let sh = self.exit_before >> 31;
        (self.exit_last & sh) | (self.exit_before - sh)
    }
}

/// Point `edge*2 + end` of the walk: `p[e] + t·(p[prev(e)] − p[e])`, `t = num/den` (double,
/// guarded), and its feature byte `prev(e) | 0xA0`.
fn walk_point(p: &[V; 3], q: &Quots, idx: i32) -> (V, u8) {
    let e = (idx >> 1) as usize;
    let k = (idx & 1) as usize;
    let t = guarded_div(q[4 * e + 2 * k], q[4 * e + 1 + 2 * k]) as f32;
    let j = PREV[e];
    let a = p[e];
    let b = p[j];
    (
        [
            (b[0] - a[0]) * t + a[0],
            t * (b[1] - a[1]) + a[1],
            t * (b[2] - a[2]) + a[2],
        ],
        j as u8 | 0xA0,
    )
}

/// Narrow `[t0, t1]` of one edge by the infinite cylinder `|(d + t·e)×a| ≤ r`
/// (`e×a`, `d×a` in double). Returns the value whose sign bit says "non-empty".
fn clip_infinite_cylinder(
    q: &mut Quots,
    i: usize,
    a: [f64; 3],
    e: [f64; 3],
    d: [f64; 3],
    r2: f64,
) -> f32 {
    let (ax, ay, az) = (a[0], a[1], a[2]);
    let (ex, ey, ez) = (e[0], e[1], e[2]);
    let (dx, dy, dz) = (d[0], d[1], d[2]);
    let cx = az * ey - ay * ez;
    let cz = ex * ay - ax * ey;
    let cy = ax * ez - ex * az;
    let wx = az * dy - ay * dz;
    let wy = ax * dz - dx * az;
    let wz = dx * ay - ax * dy;
    let ee = (cx * cx + cy * cy) + cz * cz;
    let we = (wx * cx + wy * cy) + wz * cz;
    let ww = ((wx * wx + wy * wy) + wz * wz) - r2;
    let disc = we * we - ww * ee;
    if disc >= 0.0 && ee > 1.0e-10 {
        let s = disc.sqrt();
        let r0 = -we - s;
        let r1 = s - we;
        let b = 4 * i;
        // t0 = max(t0, r0/ee)
        if !(q[b] * ee - r0 * q[b + 1] + (q[b] - r0) * 1.0e-80 > 0.0) {
            q[b] = r0;
            q[b + 1] = ee;
        }
        // t1 = min(t1, r1/ee)
        if q[b + 2] * ee - r1 * q[b + 3] + (q[b + 2] - r1) * 1.0e-80 >= 0.0 {
            q[b + 2] = r1;
            q[b + 3] = ee;
        }
        (q[b] * q[b + 3] - q[b + 1] * q[b + 2]) as f32
    } else {
        ww as f32
    }
}

/// Slab `|a·(d + t·e)| ≤ hh` of edge `i`: writes `[t0, t1]` with positive denominators.
/// Returns the float `a·e` (whose sign selects the ends) for the capsule.
fn slab(q: &mut Quots, i: usize, a: [f64; 3], e: [f64; 3], d: [f64; 3], hh: f32) -> f32 {
    let b = 4 * i;
    let ad = (a[0] * -d[0] + a[1] * -d[1]) + a[2] * -d[2];
    q[b] = ad;
    q[b + 2] = ad;
    let ae = (a[0] * e[0] + a[1] * e[1]) + a[2] * e[2];
    q[b + 1] = ae;
    q[b + 3] = ae;
    let aef = ae as f32;
    let base = ((aef.to_bits() as i32) >> 30) & !1;
    let sel = (base + 2) as i64;
    let ia = b + 2 * (sel >> 1) as usize;
    let ib = b + 2 * ((sel ^ 2) >> 1) as usize;
    let hh = f64::from(hh);
    q[ia] += hh;
    q[ib] -= hh;
    let sg = f64::from(base + 1);
    q[b + 1] *= sg;
    q[b + 3] *= sg;
    q[b] *= sg;
    q[b + 2] *= sg;
    aef
}

/// Triangle vs cylinder, `sub_35164830` (INTERSECT cell `[tri][cyl]`).
///
/// Output convention: `pt[0]` / `pt[1]` are the entry / exit points of the triangle boundary
/// (edge contact, features `0xA0|v, 0x40, 0xA0|v, 0x40`) or both the axis–plane point (face
/// contact, features `0x40` ×4); `n` and `n_alt` are the triangle normal and the deepest point
/// when written; border points are appended to `out.border`.
#[allow(clippy::too_many_lines)]
pub fn tri_cylinder(tri: &Triangle, cyl: &CylinderPrim, out: &mut PrimInters) -> bool {
    let Tri { p, n } = Tri::new(tri);
    let c = v3(cyl.center);
    let a = v3(cyl.axis);
    let (r, hh) = (cyl.r, cyl.hh);
    let ad = d64(a);
    let r2 = f64::from(r * r);

    // 1. vertices outside the cylinder
    let mut outside = [0u32; 3];
    for (i, o) in outside.iter_mut().enumerate() {
        let d = d64(sub(p[i], c));
        let t1 = ad[0] * d[2] - d[0] * ad[2];
        let t2 = ad[2] * d[1] - ad[1] * d[2];
        let t3 = d[0] * ad[1] - ad[0] * d[1];
        let in_r = (r2 - ((t1 * t1 + t2 * t2) + t3 * t3)) as f32;
        let in_h = (f64::from(hh) - ((ad[0] * d[0] + ad[1] * d[1]) + ad[2] * d[2]).abs()) as f32;
        *o = (in_r.to_bits() | in_h.to_bits()) >> 31;
    }
    let start = last_outside(outside);
    if start < 0 {
        return false;
    }

    // 2. walk the edges i → prev(i), clipping each with the slab and the infinite cylinder
    let mut q: Quots = [0.0; 12];
    let mut walk = Walk::new();
    let mut i = start as usize;
    loop {
        let e = d64(sub(p[PREV[i]], p[i]));
        let d = d64(sub(p[i], c));
        slab(&mut q, i, ad, e, d, hh);
        let nonempty = clip_infinite_cylinder(&mut q, i, ad, e, d, r2);
        if sbit(nonempty) != 0 {
            walk.visit(&p, i, &q, v3(out.pt[1]));
        }
        i = PREV[i];
        if i as i32 == start {
            break;
        }
    }

    if walk.entry < 0 {
        return tri_cylinder_face(&p, n, c, a, r, hh, out);
    }
    let exit = walk.exit();
    if exit < 0 {
        return false;
    }
    let (p0, f0) = walk_point(&p, &q, walk.entry);
    out.pt[0] = vec(p0);
    out.i_feature[1][1] = 0x40;
    out.i_feature[0][1] = 0x40;
    out.i_feature[0][0] = f0;
    let (p1, f1) = walk_point(&p, &q, exit);
    out.pt[1] = vec(p1);
    out.i_feature[1][0] = f1;

    // 3. rim points of the cap facing the triangle
    let sgn = sgn1((n[1] * a[1] + n[0] * a[0]) + n[2] * a[2]);
    let sh = sgn as f32 * hh;
    let cap = [c[0] - a[0] * sh, c[1] - a[1] * sh, c[2] - a[2] * sh];
    let x1 = n[0] * a[2] - n[2] * a[0];
    let x2 = n[2] * a[1] - n[1] * a[2];
    let x3 = n[1] * a[0] - n[0] * a[1];
    if !((x1 * x1 + x2 * x2) + x3 * x3 > 0.001) {
        // axis ∥ normal: 8 points of the cap circle
        if dotf(sub(cap, p[0]), n) > (hh + r) * SLACK {
            out.n_alt = vec(cap);
            out.n = vec(n);
            out.unk_84 = 1000;
            let u = perp_unit(a);
            let ru = [r * u[0], r * u[1], r * u[2]];
            let w = [
                ru[1] * a[2] - ru[2] * a[1],
                ru[2] * a[0] - ru[0] * a[2],
                ru[0] * a[1] - ru[1] * a[0],
            ];
            let (mut co, mut si) = (1.0f32, 0.0f32);
            for _ in 0..8 {
                let pt = [
                    (cap[0] + co * ru[0]) + si * w[0],
                    (cap[1] + ru[1] * co) + w[1] * si,
                    (cap[2] + ru[2] * co) + w[2] * si,
                ];
                let below = sbit(dotf(sub(pt, p[0]), n))
                    & sbit(edge_side(pt, p[0], sub(p[1], p[0]), n))
                    & sbit(edge_side(pt, p[1], sub(p[2], p[1]), n))
                    & sbit(edge_side(pt, p[2], sub(p[0], p[2]), n));
                push_border_if(out, pt, below != 0);
                let c0 = co;
                co = (f64::from(co - si) * SQRT1_2_D) as f32;
                si = (f64::from(c0 + si) * SQRT1_2_D) as f32;
            }
        }
    } else {
        // the rim point deepest along -n: direction (n×a)×a
        let w = [
            a[2] * n[1] - a[1] * n[2],
            n[2] * a[0] - n[0] * a[2],
            n[0] * a[1] - n[1] * a[0],
        ];
        let v = [
            a[2] * w[1] - a[1] * w[2],
            w[2] * a[0] - w[0] * a[2],
            w[0] * a[1] - w[1] * a[0],
        ];
        let l2 = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
        let dir = if !(l2 > EPS20_F) {
            [0.0, 0.0, 1.0]
        } else {
            let inv = 1.0 / l2.sqrt();
            [v[0] * inv, inv * v[1], inv * v[2]]
        };
        let rim = [
            r * dir[0] + cap[0],
            r * dir[1] + cap[1],
            r * dir[2] + cap[2],
        ];
        if dotf(n, sub(rim, p[0])) > (r + hh) * SLACK && inside_tri(rim, &p, n) && border_room(out)
        {
            out.n_alt = vec(rim);
            out.n = vec(n);
            out.unk_84 = 1000;
            out.border.push(vec(rim));
        }
        axis_border_point(out, &p, n, a, hh, r, rim, sgn);
    }
    true
}

/// `v37`: index of the last vertex outside the shape, or -1 (branch-free in the original).
fn last_outside(o: [u32; 3]) -> i32 {
    let v24 = o[0] as i32 - 1;
    let v28 = o[1] as i32;
    let o2 = o[2] as i32;
    (o2.wrapping_neg() & 2) | ((v28.wrapping_neg() & 1) | (v24 & (v28 - 1))) & (o2 - 1)
}

/// Face case of [`tri_cylinder`] (no edge enters the cylinder): the cylinder's deepest rim
/// point, the axis segment through it crossing the triangle plane inside the triangle.
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
fn tri_cylinder_face(p: &[V; 3], n: V, c: V, a: V, r: f32, hh: f32, out: &mut PrimInters) -> bool {
    let na = (n[0] * a[0] + n[1] * a[1]) + n[2] * a[2];
    let wy = f64::from(n[1] - a[1] * na);
    let wx = f64::from(n[0] - a[0] * na);
    let wz = f64::from(n[2] - a[2] * na);
    let l2 = (wy * wy + wx * wx) + wz * wz;
    let (ux, uy, uz) = if !(l2 > f64::from(1.0e-4f32)) {
        (0.0, 0.0, 0.0)
    } else if !(l2 > f64::from(EPS20_F)) {
        (0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / l2.sqrt();
        (inv * wx, wy * inv, wz * inv)
    };
    let na2 = (a[1] * n[1] + a[0] * n[0]) + a[2] * n[2];
    let sg = sgn1(na2) as f32;
    let rd = f64::from(r);
    let ox = f64::from(sg * (a[0] * hh)) + rd * ux;
    let oy = f64::from(sg * (a[1] * hh)) + uy * rd;
    let oz = f64::from(sg * (a[2] * hh)) + uz * rd;
    // P: the deepest point c − sgn·hh·a − r·w
    let px = (f64::from(c[0]) - ox) as f32;
    let py = (f64::from(c[1]) - oy) as f32;
    let pz = (f64::from(c[2]) - oz) as f32;
    let (pxd, pyd, pzd) = (f64::from(px), f64::from(py), f64::from(pz));
    // the segment P → P + 2·o
    let sx = (f64::from(c[0]) + ox) - pxd;
    let sy = (f64::from(c[1]) + oy) - pyd;
    let sz = (f64::from(c[2]) + oz) - pzd;
    let dist = f64::from((n[0] * (p[0][0] - px) + (p[0][1] - py) * n[1]) + (p[0][2] - pz) * n[2]);
    let den = (f64::from(n[0]) * sx + f64::from(n[1]) * sy) + f64::from(n[2]) * sz;
    let hy = pyd * den + sy * dist;
    let crosses = sbit(((dist * 2.0 - den).abs() - den) as f32);
    let hx = den * pxd + dist * sx;
    let denf = den as f32;
    let hz = pzd * den + sz * dist;
    let h = [hx, hy, hz];
    let mut ok = crosses;
    for (k, &j) in NEXT.iter().enumerate() {
        ok &= sbit(face_edge_side_cyl(p[k], p[j], h, denf, n));
    }
    if ok == 0 {
        return false;
    }
    let inv = 1.0 / den;
    let xy = (hy * inv) as f32;
    let xz = (hz * inv) as f32;
    let xx = (inv * hx) as f32;
    let x = Vec3::new(xx, xy, xz);
    out.pt[1] = x;
    out.pt[0] = x;
    out.i_feature = [[0x40; 2]; 2];
    out.n_alt = x;
    out.n = vec(n);
    out.unk_84 = 1000;

    let pp = [px, py, pz];
    if near_below_plane(pp, p[0], n, r + hh) && inside_tri(pp, p, n) {
        push_border(out, pp);
    }
    axis_border_point(out, p, n, a, hh, r, pp, sgn1(na2));

    // 8 points of the cap circle at the far end −sgn·hh, kept when below the plane
    let s2 = sg * hh;
    let cc = [c[0] - a[0] * s2, c[1] - a[1] * s2, c[2] - a[2] * s2];
    let u = perp_unit(a);
    let ru = [r * u[0], r * u[1], r * u[2]];
    let w = [
        ru[1] * a[2] - ru[2] * a[1],
        ru[2] * a[0] - ru[0] * a[2],
        ru[0] * a[1] - ru[1] * a[0],
    ];
    for k in 0..8 {
        let (co, si) = (CIRCLE_COS[k], CIRCLE_SIN[k]);
        let pt = [
            (ru[0] * co + cc[0]) + w[0] * si,
            (ru[1] * co + cc[1]) + w[1] * si,
            (ru[2] * co + cc[2]) + w[2] * si,
        ];
        let below =
            sbit((n[1] * (pt[1] - p[0][1]) + n[0] * (pt[0] - p[0][0])) + n[2] * (pt[2] - p[0][2]));
        push_border_if(out, pt, below != 0);
    }
    true
}

/// Side of the homogeneous point `h / f` relative to edge `v → w` (tri–cyl face case): the
/// differences `h − v·f` are double, the edge vector float, the sum `(ny·A + B·nx) + nz·C`
/// double.
fn face_edge_side_cyl(v: V, w: V, h: [f64; 3], f: f32, n: V) -> f32 {
    let ex = f64::from(w[0] - v[0]);
    let ey = f64::from(w[1] - v[1]);
    let ez = f64::from(w[2] - v[2]);
    let qx = h[0] - f64::from(v[0] * f);
    let qy = h[1] - f64::from(v[1] * f);
    let qz = h[2] - f64::from(v[2] * f);
    let ya = ex * qz - ez * qx;
    let xb = ez * qy - ey * qz;
    let zc = ey * qx - ex * qy;
    ((f64::from(n[1]) * ya + xb * f64::from(n[0])) + f64::from(n[2]) * zc) as f32
}

/// Triangle vs capsule, `sub_35168280` (INTERSECT cell `[tri][cap]`). Same structure as
/// [`tri_cylinder`] with the end spheres added to the vertex test and the edge intervals, the
/// deepest point taken on the end sphere, no cap circle, and the axis border point only when
/// the capsule lies nearly flat (`|n·a| < 0.1`).
#[allow(clippy::too_many_lines)]
pub fn tri_capsule(tri: &Triangle, cap: &CylinderPrim, out: &mut PrimInters) -> bool {
    let Tri { p, n } = Tri::new(tri);
    let c = v3(cap.center);
    let a = v3(cap.axis);
    let (r, hh) = (cap.r, cap.hh);
    let ad = d64(a);
    let r2 = f64::from(r * r);

    // 1. vertices outside the cylinder part and the end sphere on their side
    let mut last = -1i32;
    for (i, &pi) in p.iter().enumerate() {
        let d = d64(sub(pi, c));
        let t1 = ad[0] * d[2] - d[0] * ad[2];
        let t2 = ad[2] * d[1] - ad[1] * d[2];
        let t3 = d[0] * ad[1] - ad[0] * d[1];
        let in_r = (r2 - ((t1 * t1 + t2 * t2) + t3 * t3)) as f32;
        let ax_d = (ad[1] * d[1] + ad[0] * d[0]) + ad[2] * d[2];
        let in_h = (f64::from(hh) - ax_d.abs()) as f32;
        let sh = sgn1(ax_d as f32) as f32 * hh;
        let sx = d[0] - f64::from(a[0] * sh);
        let sy = d[1] - f64::from(a[1] * sh);
        let sz = d[2] - f64::from(a[2] * sh);
        let in_s = (r2 - ((sx * sx + sy * sy) + sz * sz)) as f32;
        let o = sbit(in_s) & ((in_r.to_bits() | in_h.to_bits()) >> 31);
        last = (i as i32 & (o as i32).wrapping_neg()) | ((o as i32 - 1) & last);
    }
    if last < 0 {
        return false;
    }

    // 2. edge walk
    let mut q: Quots = [0.0; 12];
    let mut walk = Walk::new();
    let mut i = last as usize;
    loop {
        let ef = sub(p[PREV[i]], p[i]);
        let e = d64(ef);
        let d = d64(sub(p[i], c));
        let ae = slab(&mut q, i, ad, e, d, hh);
        let b = 4 * i;
        let s = sgn1(ae) as f32 * hh;
        let ee = (e[0] * e[0] + e[1] * e[1]) + e[2] * e[2];
        // sphere at the +sgn end: union with [t0, t1]
        let qy = f64::from(a[1] * s) + d[1];
        let qx = f64::from(a[0] * s) + d[0];
        let qz = f64::from(a[2] * s) + d[2];
        let b1 = (qy * e[1] + e[0] * qx) + qz * e[2];
        let disc = b1 * b1 - (((qy * qy + qx * qx) + qz * qz) - r2) * ee;
        if disc >= 0.0 {
            let sq = disc.sqrt();
            let r1 = sq - b1;
            let r0 = -b1 - sq;
            if r1 * q[b + 1] - ee * q[b] + (r1 - q[b]) * 1.0e-80 >= 0.0 {
                // t0 = min(t0, r0)
                if ee * q[b] - r0 * q[b + 1] + (q[b] - r0) * 1.0e-80 >= 0.0 {
                    q[b] = r0;
                    q[b + 1] = ee;
                }
            } else {
                q[b] = r0;
                q[b + 1] = ee;
                q[b + 2] = r1;
                q[b + 3] = ee;
            }
        }
        // sphere at the −sgn end: union with [t0, t1]
        let my = d[1] - f64::from(a[1] * s);
        let mx = d[0] - f64::from(a[0] * s);
        let mz = d[2] - f64::from(a[2] * s);
        let b2 = (my * e[1] + e[0] * mx) + mz * e[2];
        let disc = b2 * b2 - (((my * my + mx * mx) + mz * mz) - r2) * ee;
        if disc >= 0.0 {
            let t1n = q[b + 2];
            let sq = disc.sqrt();
            let t1x = q[b + 2] * ee;
            let r0 = -b2 - sq;
            let r1 = sq - b2;
            if !(r0 * q[b + 3] - t1x + (r0 - q[b + 2]) * 1.0e-80 > 0.0) {
                // t1 = max(t1, r1)
                if !(t1x - r1 * q[b + 3] + (t1n - r1) * 1.0e-80 > 0.0) {
                    q[b + 2] = r1;
                    q[b + 3] = ee;
                }
            } else {
                q[b] = r0;
                q[b + 1] = ee;
                q[b + 2] = r1;
                q[b + 3] = ee;
            }
        }
        let nonempty = clip_infinite_cylinder(&mut q, i, ad, e, d, r2);
        if sbit(nonempty) != 0 {
            walk.visit(&p, i, &q, v3(out.pt[1]));
        }
        i = PREV[i];
        if i as i32 == last {
            break;
        }
    }

    let na_sum = (n[1] * a[1] + n[0] * a[0]) + n[2] * a[2];
    if walk.entry < 0 {
        return tri_capsule_face(&p, n, c, a, r, hh, out);
    }
    let exit = walk.exit();
    if exit < 0 {
        return false;
    }
    let (p0, f0) = walk_point(&p, &q, walk.entry);
    out.pt[0] = vec(p0);
    out.i_feature[1][1] = 0x40;
    out.i_feature[0][1] = 0x40;
    out.i_feature[0][0] = f0;
    let (p1, f1) = walk_point(&p, &q, exit);
    out.pt[1] = vec(p1);
    out.i_feature[1][0] = f1;

    // 3. deepest point of the end sphere facing the triangle
    let sgn = sgn1(na_sum);
    let sh = sgn as f32 * hh;
    let rim = [
        (c[0] - a[0] * sh) - n[0] * r,
        (c[1] - a[1] * sh) - n[1] * r,
        (c[2] - a[2] * sh) - n[2] * r,
    ];
    if dotf(n, sub(rim, p[0])) > (r + hh) * SLACK && inside_tri(rim, &p, n) && border_room(out) {
        out.n_alt = vec(rim);
        out.n = vec(n);
        out.unk_84 = 1000;
        out.border.push(vec(rim));
    }
    if !(((a[1] * n[1] + a[0] * n[0]) + a[2] * n[2]).abs() >= 0.1) {
        axis_border_point(out, &p, n, a, hh, r, rim, sgn);
    }
    true
}

/// Face case of [`tri_capsule`]: the deepest point of the end sphere and the axis segment
/// through it crossing the triangle plane inside the triangle.
fn tri_capsule_face(p: &[V; 3], n: V, c: V, a: V, r: f32, hh: f32, out: &mut PrimInters) -> bool {
    let sgn = sgn1((a[1] * n[1] + a[0] * n[0]) + a[2] * n[2]);
    let sg = sgn as f32;
    let s = sg * hh;
    let ox = n[0] * r + a[0] * s;
    let oy = n[1] * r + a[1] * s;
    let oz = n[2] * r + a[2] * s;
    let py = c[1] - oy;
    let px = c[0] - ox;
    let pz = c[2] - oz;
    let (pxd, pyd, pzd) = (f64::from(px), f64::from(py), f64::from(pz));
    let sx = (f64::from(c[0]) + f64::from(ox)) - pxd;
    let sy = (f64::from(c[1]) + f64::from(oy)) - pyd;
    let sz = (f64::from(c[2]) + f64::from(oz)) - pzd;
    let den = (f64::from(n[0]) * sx + f64::from(n[1]) * sy) + f64::from(n[2]) * sz;
    let dist = f64::from((n[0] * (p[0][0] - px) + n[1] * (p[0][1] - py)) + n[2] * (p[0][2] - pz));
    let crosses = sbit(((dist * 2.0 - den).abs() - den) as f32);
    let hx = den * pxd + sx * dist;
    let hy = den * pyd + dist * sy;
    let hz = den * pzd + dist * sz;
    let denf = den as f32;
    let h = [hx, hy, hz];
    let mut ok = crosses;
    for (k, &j) in NEXT.iter().enumerate() {
        ok &= sbit(face_edge_side_cap(p[k], p[j], h, denf, n));
    }
    if ok == 0 {
        return false;
    }
    let inv = 1.0 / den;
    let x = Vec3::new((inv * hx) as f32, (inv * hy) as f32, (inv * hz) as f32);
    out.pt[1] = x;
    out.pt[0] = x;
    out.i_feature = [[0x40; 2]; 2];
    out.n_alt = x;
    out.n = vec(n);
    out.unk_84 = 1000;

    let pp = [px, py, pz];
    if near_below_plane(pp, p[0], n, r + hh) && inside_tri(pp, p, n) {
        push_border(out, pp);
    }
    if !(((a[1] * n[1] + a[0] * n[0]) + a[2] * n[2]).abs() >= 0.1) {
        axis_border_point(out, p, n, a, hh, r, pp, sgn);
    }
    true
}

/// Side of the homogeneous point `h / f` relative to edge `v → w` (tri–cap face case): as
/// [`face_edge_side_cyl`] but the sum is `(B·nx + ny·A) + nz·C` with float normal products.
fn face_edge_side_cap(v: V, w: V, h: [f64; 3], f: f32, n: V) -> f32 {
    let ex = f64::from(w[0] - v[0]);
    let ey = f64::from(w[1] - v[1]);
    let ez = f64::from(w[2] - v[2]);
    let qx = h[0] - f64::from(v[0] * f);
    let qy = h[1] - f64::from(v[1] * f);
    let qz = h[2] - f64::from(v[2] * f);
    let ya = ex * qz - ez * qx;
    let xb = ez * qy - ey * qz;
    let zc = ey * qx - ex * qy;
    ((xb * f64::from(n[0]) + f64::from(n[1]) * ya) + f64::from(n[2]) * zc) as f32
}

/// The swap-wrapper fix-up of 0x35168200 / 0x3516AD80: swap `pt[0]`/`pt[1]` and reverse the
/// four feature bytes (`36↔39`, `37↔38`); `n` is left as the callee wrote it.
fn swap_fixup(out: &mut PrimInters) {
    out.pt.swap(0, 1);
    let [[a0, a1], [b0, b1]] = out.i_feature;
    out.i_feature = [[b1, b0], [a1, a0]];
}

/// Cylinder vs triangle, `sub_35168200`: [`tri_cylinder`] with swapped arguments, then
/// [`swap_fixup`] (applied whatever the result, as in the original).
pub fn cylinder_tri(cyl: &CylinderPrim, tri: &Triangle, out: &mut PrimInters) -> bool {
    let r = tri_cylinder(tri, cyl, out);
    swap_fixup(out);
    r
}

/// Capsule vs triangle, `sub_3516AD80`: [`tri_capsule`] with swapped arguments, then
/// [`swap_fixup`].
pub fn capsule_tri(cap: &CylinderPrim, tri: &Triangle, out: &mut PrimInters) -> bool {
    let r = tri_capsule(tri, cap, out);
    swap_fixup(out);
    r
}

fn cell_tri_cyl(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    match (a, b) {
        (Primitive::Triangle(t), Primitive::Cylinder(c)) => tri_cylinder(t, c, out),
        _ => false,
    }
}

fn cell_tri_cap(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    match (a, b) {
        (Primitive::Triangle(t), Primitive::Capsule(c)) => tri_capsule(t, c, out),
        _ => false,
    }
}

fn cell_cyl_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    match (a, b) {
        (Primitive::Cylinder(c), Primitive::Triangle(t)) => cylinder_tri(c, t, out),
        _ => false,
    }
}

fn cell_cap_tri(a: &Primitive, b: &Primitive, out: &mut PrimInters) -> bool {
    match (a, b) {
        (Primitive::Capsule(c), Primitive::Triangle(t)) => capsule_tri(c, t, out),
        _ => false,
    }
}

/// Write this group's INTERSECT cells: `[tri][cyl]`, `[tri][cap]`, `[cyl][tri]`, `[cap][tri]`.
pub fn fill(t: &mut PairTables) {
    let tri = GeomType::TriMesh.id() as usize;
    let cyl = GeomType::Cylinder.id() as usize;
    let cap = GeomType::Capsule.id() as usize;
    t.intersect[tri][cyl] = cell_tri_cyl;
    t.intersect[tri][cap] = cell_tri_cap;
    t.intersect[cyl][tri] = cell_cyl_tri;
    t.intersect[cap][tri] = cell_cap_tri;
}
