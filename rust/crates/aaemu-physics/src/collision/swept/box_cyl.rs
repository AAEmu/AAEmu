//! box–cylinder linear unprojection and its cyl–box swap wrapper.
//!
//! | function | x64 | x86 (1.2) | size |
//! |---|---|---|---|
//! | [`unproject_box_cyl`] (cell `[0][box][cyl]`) | 0x351801A0 | 0x35183AD0 | 22 777 B |
//! | cyl–box swap wrapper (cell `[0][cyl][box]`) | 0x35185AA0 | 0x3518A8F0 | 215 B |
//!
//! Callees: sub_35021020 ([`normalize_in_place`]), sub_3516E6C0 ([`clip_segment_slab`]); the
//! wrapper calls sub_3516E850 ([`super::common::flip_result_and_area`]) through
//! [`swap_linear_area`] (its disassembly — negate `dir`, call 0x351801A0 with
//! `(params, b, fb, a, fa, out, area)`, on a non-zero result call sub_3516E850
//! `(params, out, area)`, negate `dir` again — matches the shared helper).
//!
//! # Algorithm (all in the box frame, mostly in double precision)
//!
//! The cylinder centre `c`, axis `a` and the direction `d` are rotated into the box frame
//! (float products, converted to double). The function is a branch-free "separating feature"
//! search: every candidate computes a separation distance as a fraction `num/den` and is taken,
//! with a feature code, when `num > 0` and `num/den` is smaller than the best so far (initially
//! `tmax/1`). The comparisons are made on the sign bits of float-rounded values and the update
//! is a blend `keep·old + take·new` (with `take`, `keep` ∈ {0, 1}), mirrored literally. With
//! `dir = 0` (all bits of `|dir|²` but the sign zero) every candidate supplies its own direction
//! (`−sgn(·)`·axis), which is blended into the best direction as well.
//!
//! 1. box faces (3): code `4i | (s+1) | (s'+1)/2` (0..11);
//! 2. cylinder cap faces (skipped when `featB` is 0x43, the side surface): code `0x20 | …`;
//! 3. box vertices against the cylinder side (8; quadratic in `t`): code `0x40 | k`;
//! 4. box edge directions × axis (3): code `0x60 | …`;
//! 5. box edges against the cap rims (only with `featB ≠ 0x43`, `|a_i| > 0.01`; 3 × 8 quadratics,
//!    accepted as a group): code `0x80 | …`.
//!
//! No candidate (code −1) returns 0 without writing anything. With `dir = 0` the best direction
//! is normalised (if `||d|² − 1| > 0.001`) and written back to `params.dir` (world frame).
//! `t = num/den` (0 if `den` is 0 or NaN; edge cases compute `t` themselves) and the switch on
//! `code & 0xE0` writes `t`, `pt`, `n` and the feature ids (box feature first, cylinder second:
//! box face 0x40|i, edge 0x20|…, vertex 0x00..; cylinder side 0x40, caps 0x41/0x42,
//! rims 0x20/0x21) and sets the "point outside" flag `v815` used by the final check.
//!
//! **Contact area.** With an area (or with a NULL area and `params.flag ≠ 0`, see below):
//! * if the axis is (nearly, `> 0.998`) parallel to a box axis and the cap disc lies flat on the
//!   face: 8 points of the cap circle (`pt` stored even when rejected, `npt` counts the points
//!   inside the face) plus up to 2 × 4 points where the circle crosses the face edges;
//!   `iType = 0`, `n` = face normal, area `n` = ±axis;
//! * else, per box face whose normal is (nearly) perpendicular to the axis: a side line lying on
//!   the face, clipped to the face rectangle with sub_3516E6C0 (2 points, if longer than
//!   `minPtDist`), or a side line crossing a face edge (2 points); `iType = 1`;
//! * else the cap rim touching a face (2 points).
//!
//! If the area got points, the result takes `pt` and the feature ids of point 0. Finally a
//! branch-free check that `pt` lies on both surfaces (within `0.05·min(size, r)`), unless the
//! switch already found the point inside (`v815 = 0`), decides between returning
//! `1 | (flag ^ 1)` and 0. The return value is `flag ^ 1` when the point check is skipped.
//!
//! # Static state (`.bss` 0x35378E90..0x35378FF0)
//! With a NULL area and `params.flag ≠ 0` the function uses a **static** contact area: header
//! at 0x35378EE0 (`npt` 0x35378EE4 = 0, `nmaxpt` 0x35378EE8 = 16, `minEdge` 0x35378EEC = 0),
//! `pt` = 0x35378F30 (16 points) and **all four** int arrays (`piPrim[0..1]`, `piFeature[0..1]`
//! at 0x35378EF0..0x35378F08) pointing at the same 20-int buffer 0x35378E90. The pointers and
//! counters are re-initialised on every such call and nothing is read before it is written
//! within the same call, so no state leaks between calls; the aliasing does show: the feature
//! ids taken from point 0 are the **last value written** to that slot by any of the four arrays
//! (0, the box feature or the cylinder feature depending on the path). [`StaticArea`] models
//! exactly that.
//!
//! # Oddities kept
//! * `nmaxpt` is never read: up to 16 points are written into a caller's area regardless of its
//!   capacity (the port grows the `Vec`s instead of overrunning; the entries `< nmaxpt` and the
//!   counters are identical). The 8-point loop and the edge loop start at the caller's `npt`.
//! * Feature 0x43 for B (`fb == 0x43`, or `0x80000043` through the wrapping subtraction)
//!   disables the cap candidates (2, 5) and parts of the area code.
//! * The test `v572 − v573 < s || v572 + v573 > −s` of the edge area path (centre ± half chord
//!   against the face slab `[−s, s]`) is always true for `s ≥ 0` (both failing would need
//!   `v573 ≤ −s`); it was probably meant as an overlap test. Kept, with the `jb` NaN semantics.
//! * `tmin` is not used.

use super::common::{
    as_box, as_cyl, clip_segment_slab, le_or_nan_d, lt_or_nan_d, normalize_in_place,
    swap_linear_area, ucomisd_eq, DEC_MOD3, INC_MOD3,
};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive};
use crate::ids::GeomType;
use crate::math::Vec3;

// ------------------------------------------------------------------------------- bit idioms

/// Sign bit of a float as 0/1 (`shr 31`).
#[inline]
fn sb(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// `((bits >> 30) & ~1) + 1`: +1, or −1 when the sign bit is set (−0 counts as negative).
#[inline]
fn sgn(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// `−1 − ((bits >> 30) & ~1)`: −1, or +1 when the sign bit is set.
#[inline]
fn nsgn(x: f32) -> i32 {
    -1 - (((x.to_bits() as i32) >> 30) & !1)
}

/// `keep·old + take·new` in double (the branch-free selects of the original, kept literal:
/// `0·inf` and `−0 + 0` behave like the binary's).
#[inline]
fn blend(keep: f64, old: f64, take: f64, new: f64) -> f64 {
    keep * old + take * new
}

/// Feature-code update `code = code & (cond − 1) | id & −cond`.
#[inline]
fn pick(code: i32, cond: i32, id: i32) -> i32 {
    (code & cond.wrapping_sub(1)) | (id & cond.wrapping_neg())
}

/// `(f32 → f64) 1e-20` as the binary's double constant 9.999999682655225e-21.
const EPS20_D: f64 = 9.999_999_682_655_225e-21;
/// 1/√2 (`0x3FE6A09E667F3BCD`).
const SQRT_HALF: f64 = std::f64::consts::FRAC_1_SQRT_2;

// ------------------------------------------------------------------------------- frames

/// The box basis as the 9 floats of its binary layout (row major).
struct Frame {
    r: [f32; 9],
    c: Vec3,
}

impl Frame {
    fn new(b: &BoxPrim) -> Self {
        let m = &b.basis.m;
        Frame {
            r: [
                m[0][0], m[0][1], m[0][2], m[1][0], m[1][1], m[1][2], m[2][0], m[2][1], m[2][2],
            ],
            c: b.center,
        }
    }

    /// `Rᵀ·q` in double with the binary's grouping (box frame → world vector).
    fn rot_t(&self, q: [f64; 3]) -> [f64; 3] {
        let r = |k: usize| f64::from(self.r[k]);
        [
            (r(3) * q[1] + r(0) * q[0]) + r(6) * q[2],
            (r(1) * q[0] + r(4) * q[1]) + r(7) * q[2],
            (r(2) * q[0] + r(5) * q[1]) + r(8) * q[2],
        ]
    }

    /// `centre + Rᵀ·q`, rounded to float (box frame → world point).
    fn to_world(&self, q: [f64; 3]) -> Vec3 {
        let w = self.rot_t(q);
        Vec3::new(
            (f64::from(self.c.x) + w[0]) as f32,
            (f64::from(self.c.y) + w[1]) as f32,
            (f64::from(self.c.z) + w[2]) as f32,
        )
    }

    /// An area point: `(f64)(dir·(float)t) + (centre + Rᵀ·q)`, rounded to float.
    fn area_pt(&self, q: [f64; 3], dir: Vec3, t: f64) -> Vec3 {
        let tf = t as f32;
        let w = self.rot_t(q);
        Vec3::new(
            (f64::from(dir.x * tf) + (f64::from(self.c.x) + w[0])) as f32,
            (f64::from(dir.y * tf) + (f64::from(self.c.y) + w[1])) as f32,
            (f64::from(dir.z * tf) + (f64::from(self.c.z) + w[2])) as f32,
        )
    }

    /// Row `i` of the basis times a float.
    fn row_scaled(&self, i: usize, s: f32) -> Vec3 {
        Vec3::new(
            self.r[3 * i] * s,
            self.r[3 * i + 1] * s,
            self.r[3 * i + 2] * s,
        )
    }

    /// Row `i` of the basis.
    fn row(&self, i: usize) -> Vec3 {
        Vec3::new(self.r[3 * i], self.r[3 * i + 1], self.r[3 * i + 2])
    }
}

// ------------------------------------------------------------------------------- areas

/// The static contact area 0x35378EE0 used when the caller passes no area but `flag ≠ 0`:
/// 16 points at 0x35378F30 and one 20-int buffer (0x35378E90) shared by `piPrim[0]`,
/// `piPrim[1]`, `piFeature[0]` and `piFeature[1]`.
struct StaticArea {
    i_type: i32,
    npt: i32,
    buf: [i32; 20],
    pt: [Vec3; 16],
    n: Vec3,
}

/// Where the area writes go.
enum Area<'a> {
    User(&'a mut ContactArea),
    Static(Box<StaticArea>),
}

fn idx(i: i32) -> usize {
    usize::try_from(i).expect("contact-area index is negative (caller passed npt < 0)")
}

/// Grow a caller's area so that index `i` exists (the original writes past `nmaxpt`).
fn ensure(a: &mut ContactArea, i: usize) {
    let n = i + 1;
    if a.pt.len() < n {
        a.pt.resize(n, Vec3::ZERO);
    }
    for v in a.prim.iter_mut().chain(a.feature.iter_mut()) {
        if v.len() < n {
            v.resize(n, 0);
        }
    }
}

impl Area<'_> {
    fn npt(&self) -> i32 {
        match self {
            Area::User(a) => a.npt,
            Area::Static(s) => s.npt,
        }
    }
    fn set_npt(&mut self, v: i32) {
        match self {
            Area::User(a) => a.npt = v,
            Area::Static(s) => s.npt = v,
        }
    }
    fn set_type(&mut self, v: i32) {
        match self {
            Area::User(a) => a.i_type = v,
            Area::Static(s) => s.i_type = v,
        }
    }
    fn set_pt(&mut self, i: i32, v: Vec3) {
        let i = idx(i);
        match self {
            Area::User(a) => {
                ensure(a, i);
                a.pt[i] = v;
            }
            Area::Static(s) => s.pt[i] = v,
        }
    }
    fn pt(&self, i: i32) -> Vec3 {
        let i = idx(i);
        match self {
            Area::User(a) => a.pt[i],
            Area::Static(s) => s.pt[i],
        }
    }
    fn set_prim(&mut self, k: usize, i: i32, v: i32) {
        let i = idx(i);
        match self {
            Area::User(a) => {
                ensure(a, i);
                a.prim[k][i] = v;
            }
            Area::Static(s) => s.buf[i] = v,
        }
    }
    fn set_feat(&mut self, k: usize, i: i32, v: i32) {
        let i = idx(i);
        match self {
            Area::User(a) => {
                ensure(a, i);
                a.feature[k][i] = v;
            }
            Area::Static(s) => s.buf[i] = v,
        }
    }
    fn feat(&self, k: usize, i: i32) -> i32 {
        let i = idx(i);
        match self {
            Area::User(a) => a.feature[k][i],
            Area::Static(s) => s.buf[i],
        }
    }
    fn set_n(&mut self, v: Vec3) {
        match self {
            Area::User(a) => a.n = v,
            Area::Static(s) => s.n = v,
        }
    }
    /// sub_3516E6C0 on the feature arrays (entries 0 and 1). For the static area both id arrays
    /// are the same buffer; the routine writes `ids0[k]` then `ids1[k]` for each end it clips,
    /// so the buffer ends up with the `ids1` values.
    fn clip(&mut self, seg: &mut [f32; 4], h: f32, axis: usize, id0: i32, id1: i32) {
        match self {
            Area::User(a) => {
                ensure(a, 1);
                let [f0, f1] = &mut a.feature;
                clip_segment_slab(seg, h, axis, f0, f1, id0, id1);
            }
            Area::Static(s) => {
                let mut t0 = [s.buf[0], s.buf[1]];
                let mut t1 = [s.buf[0], s.buf[1]];
                clip_segment_slab(seg, h, axis, &mut t0, &mut t1, id0, id1);
                s.buf[0] = t1[0];
                s.buf[1] = t1[1];
            }
        }
    }
}

// ------------------------------------------------------------------------------- the cell

/// sub_351801A0: linear unprojection of box A against cylinder B (see the module docs).
/// `fb == 0x43` (the cylinder side) disables the cap candidates. `fa` is not used.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_box_cyl(
    p: &mut UnprojParams,
    b: &BoxPrim,
    _fa: i32,
    cyl: &CylinderPrim,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    let fr = Frame::new(b);
    let rr = fr.r;
    let s = [
        f64::from(b.size.x),
        f64::from(b.size.y),
        f64::from(b.size.z),
    ];
    let r = f64::from(cyl.r);
    let hh = f64::from(cyl.hh);
    let mpd = f64::from(p.min_pt_dist);
    let r2 = r * r;
    // (fb − 0x43 − 1) ^ (fb − 0x43) has the sign bit set only for fb − 0x43 ∈ {0, INT_MIN}
    let fbm = fb.wrapping_sub(0x43);
    let side = (fbm.wrapping_sub(1) ^ fbm) >= 0;

    // ---- into the box frame (float products, converted to double)
    let dxf = cyl.center.x - b.center.x;
    let dyf = cyl.center.y - b.center.y;
    let dzf = cyl.center.z - b.center.z;
    let c = [
        f64::from((dyf * rr[1] + dxf * rr[0]) + dzf * rr[2]),
        f64::from((dyf * rr[4] + dxf * rr[3]) + dzf * rr[5]),
        f64::from((dyf * rr[7] + dxf * rr[6]) + dzf * rr[8]),
    ];
    let ax = cyl.axis;
    let a = [
        f64::from((ax.x * rr[0] + ax.y * rr[1]) + ax.z * rr[2]),
        f64::from((ax.x * rr[3] + ax.y * rr[4]) + ax.z * rr[5]),
        f64::from((ax.x * rr[6] + ax.y * rr[7]) + ax.z * rr[8]),
    ];
    let pd = p.dir;
    let mut d = [
        f64::from((pd.x * rr[0] + pd.y * rr[1]) + pd.z * rr[2]),
        f64::from((pd.x * rr[3] + pd.y * rr[4]) + pd.z * rr[5]),
        f64::from((pd.x * rr[6] + pd.y * rr[7]) + pd.z * rr[8]),
    ];
    let l2 = (pd.x * pd.x + pd.y * pd.y) + pd.z * pd.z;
    let dir_zero = (l2.to_bits() & 0x7FFF_FFFF) == 0;

    let mut num = f64::from(p.tmax);
    let mut den = 1.0f64;
    let mut best = [0.0f64; 3];
    let mut code: i32 = -1;
    // stack scratch vectors of the original (v738.., v751.., v775.., v793.., v797..)
    let mut v738 = [0.0f64; 3];

    // sign of |c − a·hh|² − |c + a·hh|² (which cap is nearer), times 0.0005
    let k785 = {
        let ah = [a[0] * hh, a[1] * hh, a[2] * hh];
        let m = [c[0] - ah[0], c[1] - ah[1], c[2] - ah[2]];
        let q = [ah[0] + c[0], ah[1] + c[1], ah[2] + c[2]];
        let e = ((m[0] * m[0] + m[1] * m[1]) + m[2] * m[2])
            - ((q[0] * q[0] + q[1] * q[1]) + q[2] * q[2]);
        f64::from(sgn(e as f32)) * 0.0005
    };

    // ---- 1. box faces
    for i in 0..3usize {
        if dir_zero {
            let cf = c[i] as f32;
            d = [0.0; 3];
            d[i] = f64::from(nsgn(cf));
        }
        let ai = a[i];
        let di = d[i];
        let v61 = nsgn(di as f32);
        let v61d = f64::from(v61);
        let v63 = sgn((k785 - v61d * ai) as f32);
        let mut v751 = [ai * a[0], ai * a[1], ai * a[2]];
        v751[i] -= 1.0;
        v751 = [v751[0] * v61d, v751[1] * v61d, v751[2] * v61d];
        let v65 = (f64::from(v63) * hh * ai + c[i]) - v61d * s[i];
        let l = ((v751[0] * v751[0] + v751[1] * v751[1]) + v751[2] * v751[2]).sqrt();
        let m = ((0.001 - l).abs() + (l + 0.001)) * 0.5;
        let v69 = (v65 * m + r * v751[i]) * f64::from(-v61);
        let v68 = di.abs() * m;
        let fa = (-v69) as f32;
        let fbv = (den * v69 - v68 * num) as f32;
        let cond = sb(fa) & sb(fbv);
        let id = ((v63 + 1) >> 1) | (v61 + 1) | (4 * i as i32);
        code = pick(code, cond, id);
        let take = f64::from(cond);
        let keep = f64::from(cond ^ 1);
        num = blend(keep, num, take, v69);
        den = blend(keep, den, take, v68);
        for k in 0..3 {
            best[k] = blend(keep, best[k], take, d[k]);
        }
    }

    // ---- 2. cylinder caps
    if side {
        let cd = (a[0] * c[0] + a[1] * c[1]) + a[2] * c[2];
        if dir_zero {
            let sg = f64::from(nsgn(cd as f32));
            d = [sg * a[0], sg * a[1], sg * a[2]];
        }
        let dot = (a[0] * d[0] + d[1] * a[1]) + d[2] * a[2];
        let v80 = sgn(dot as f32);
        let v79 = dot.abs();
        let v81 = (a[1].abs() * s[1] + a[0].abs() * s[0]) + a[2].abs() * s[2];
        let v82 = ((cd * f64::from(v80) + hh) + v81)
            + ((v81 + hh) - (hh - v81).abs()) * 0.5 * (v79 - 0.999).abs();
        let fa = (-v82) as f32;
        let fbv = (den * v82 - v79 * num) as f32;
        let cond = sb(fa) & sb(fbv);
        code = pick(code, cond, ((v80 + 1) | 0x40) >> 1);
        let take = f64::from(cond);
        let keep = f64::from(cond ^ 1);
        num = blend(keep, num, take, v82);
        den = blend(keep, den, take, v79);
        for k in 0..3 {
            best[k] = blend(keep, best[k], take, d[k]);
        }
    }

    // ---- 3. box vertices against the side
    for k in 0..8i32 {
        let sz = f64::from(2 * (k & 1) - 1);
        let sx = f64::from(((k >> 1) & 2) - 1);
        let sy = f64::from((k & 2) - 1);
        let dz_ = sz * s[2] - c[2];
        let dx_ = sx * s[0] - c[0];
        let dy_ = sy * s[1] - c[1];
        v738 = [dx_, dy_, dz_];
        if dir_zero {
            let t91 = (a[1] * dy_ + a[0] * dx_) + a[2] * dz_;
            let v92 = dy_ - a[1] * t91;
            let v93 = dz_ - a[2] * t91;
            let v94 = dx_ - a[0] * t91;
            let l2 = (v94 * v94 + v92 * v92) + v93 * v93;
            let q = if l2 > EPS20_D {
                let inv = 1.0 / l2.sqrt();
                [v94 * inv, v92 * inv, v93 * inv]
            } else {
                [0.0, 0.0, 1.0]
            };
            let f = (c[0] * q[0] + q[1] * c[1]) + q[2] * c[2];
            let sg = f64::from(nsgn(f as f32));
            d = [sg * q[0], q[1] * sg, q[2] * sg];
        }
        let v102 = a[2] * dy_ - a[1] * dz_;
        let v103 = a[0] * dz_ - a[2] * dx_;
        let v104 = a[1] * dx_ - a[0] * dy_;
        let v105 = d[1] * a[2] - d[2] * a[1];
        let v106 = a[0] * d[2] - d[0] * a[2];
        let v107 = d[0] * a[1] - a[0] * d[1];
        let v755 = (v105 * v105 + v106 * v106) + v107 * v107;
        let v108 = (v105 * v102 + v106 * v103) + v107 * v104;
        let q = (((v102 * v102 + v103 * v103) + v104 * v104) - r2) * v755;
        let disc = v108 * v108 - q;
        let bf = v108 as f32;
        let df = disc as f32;
        let xf = (v108 * v108 - disc) as f32;
        if ((!df.to_bits()) & (xf.to_bits() | bf.to_bits())) as i32 >= 0 {
            continue;
        }
        let v113 = disc.sqrt() - v108;
        let x9 = dx_ * v755 + d[0] * v113;
        let y4 = dy_ * v755 + d[1] * v113;
        let z6 = dz_ * v755 + d[2] * v113;
        let v116 = (y4 * a[1] + a[0] * x9) + z6 * a[2];
        let e0 = x9 - a[0] * v116;
        let e1 = y4 - a[1] * v116;
        let e2 = z6 - a[2] * v116;
        v738 = [e0, e1, e2];
        let f771 = (sx * e0 - mpd) as f32;
        let f768 = (sy * e1 - mpd) as f32;
        let f769 = (sz * e2 - mpd) as f32;
        let f120 = (den * v113 - v755 * num) as f32;
        let cond = sb(f768) & sb(f120) & sb(f769) & sb(f771);
        code = pick(code, cond, k | 0x40);
        let take = f64::from(cond);
        let keep = f64::from(cond ^ 1);
        best[2] = blend(keep, best[2], take, d[2]);
        num = blend(keep, num, take, v113);
        den = blend(keep, den, take, v755);
        best[1] = blend(keep, best[1], take, d[1]);
        best[0] = blend(keep, best[0], take, d[0]);
    }

    // ---- 4. box edge directions × axis
    for j in 0..3usize {
        let inc = INC_MOD3[j] as usize;
        let dec = DEC_MOD3[j] as usize;
        let mut v = [0.0f64; 3];
        v[inc] = a[dec];
        v[dec] = -a[inc];
        let u = [-v[0], -v[1], -v[2]];
        let ul = ((u[0] * u[0] + u[1] * u[1]) + u[2] * u[2]).sqrt();
        let v755 = if dir_zero {
            let f = (c[0] * u[0] + u[1] * c[1]) + u[2] * c[2];
            let sg = f64::from(nsgn(f as f32));
            d = [sg * u[0], u[1] * sg, u[2] * sg];
            ul
        } else {
            1.0
        };
        v738[j] = 0.0;
        let dot = (d[1] * u[1] + d[0] * u[0]) + d[2] * u[2];
        let v143 = sgn(dot as f32);
        let adot = dot.abs();
        let v145 = (sgn(u[inc] as f32).wrapping_mul(v143)).wrapping_neg();
        let v146 = (sgn(u[dec] as f32).wrapping_mul(v143)).wrapping_neg();
        v738[inc] = f64::from(v145) * s[inc];
        v738[dec] = f64::from(v146) * s[dec];
        let v148 = ((((c[0] - v738[0]) * u[0] + (c[1] - v738[1]) * u[1])
            + (c[2] - v738[2]) * u[2])
            * f64::from(v143)
            + ul * r)
            * v755;
        let fa = (-v148) as f32;
        let fbv = (den * v148 - adot * num) as f32;
        let cond = sb(fa) & sb(fbv);
        let id = ((v145 + 1) >> 1) | (2 * v143 + 2) | (((j as i32) | 0xC) << 3) | (v146 + 1);
        code = pick(code, cond, id);
        let take = f64::from(cond);
        let keep = f64::from(cond ^ 1);
        num = blend(keep, num, take, v148);
        den = blend(keep, den, take, adot);
        best[0] = blend(keep, best[0], take, d[0]);
        best[1] = blend(keep, best[1], take, d[1]);
        best[2] = blend(keep, best[2], take, d[2]);
    }

    // ---- 5. box edges against the cap rims
    if side {
        if dir_zero {
            d = best;
        }
        let mut den2 = 1.0f64;
        let mut num2 = 0.0f64;
        let saved = code;
        for i in 0..3usize {
            let ai = a[i];
            if le_or_nan_d(ai.abs(), f64::from(0.01f32)) {
                continue;
            }
            let inc = INC_MOD3[i] as usize;
            let dec = DEC_MOD3[i] as usize;
            let ar2 = (ai * r) * (ai * r);
            for k in 0..8i32 {
                v738[i] = 0.0;
                let sk = 2 * (k & 1) - 1;
                let skd = f64::from(sk);
                let syd = f64::from((k & 2) - 1);
                let r9 = ((k >> 1) & 2) - 1;
                let r11 = (k & 2) - 1;
                let mut v797 = [0.0f64; 3];
                let mut v793 = [0.0f64; 3];
                v797[inc] = d[dec];
                v738[inc] = s[inc] * skd;
                v738[dec] = s[dec] * syd;
                let v167 = f64::from(r9) * hh;
                let px = c[0] + a[0] * v167;
                let py = a[1] * v167 + c[1];
                let pz = a[2] * v167 + c[2];
                v738[0] -= px;
                v738[2] -= pz;
                v738[1] -= py;
                let v786 = v738[0];
                let v791 = v738[2];
                let v796 = v738[1];
                v797[dec] = -d[inc];
                let v169 = v797[2] * a[1] - v797[1] * a[2];
                v793[inc] = v738[dec];
                let v171 = a[2] * v797[0] - a[0] * v797[2];
                let v172 = a[0] * v797[1] - a[1] * v797[0];
                v793[dec] = -v738[inc];
                let v175 = v793[2] * a[1] - v793[1] * a[2];
                let v178 = a[2] * v793[0] - a[0] * v793[2];
                let v176 = a[0] * v793[1] - a[1] * v793[0];
                let v785 = (v171 * v171 + v169 * v169) + v172 * v172;
                let v181 = (v178 * v171 + v175 * v169) + v176 * v172;
                let q = (((v178 * v178 + v175 * v175) + v176 * v176) - ar2) * v785;
                let disc = v181 * v181 - q;
                let bf = v181 as f32;
                let df = disc as f32;
                let xf = (v181 * v181 - disc) as f32;
                if ((!df.to_bits()) & (xf.to_bits() | bf.to_bits())) as i32 >= 0 {
                    continue;
                }
                let v187 = disc.sqrt() - v181;
                let mut v775 = [0.0f64; 3];
                let v189 = v796 * v785 + d[1] * v187;
                let v190 = v786 * v785 + d[0] * v187;
                let v191 = v791 * v785 + d[2] * v187;
                let v196 = -((a[0] * v190 + v189 * a[1]) + v191 * a[2]);
                let v195 = ai * v785;
                let sg = f64::from(sgn(v195 as f32));
                v775[inc] = a[dec];
                let v198 = v196 * sg;
                let f770 = (v198.abs() - (v195 * sg).abs() * s[i]) as f32;
                let aai = ai.abs();
                v738[1] = aai * v189;
                v738[2] = aai * v191;
                v738[0] = aai * v190;
                v738[i] += v198;
                v775[dec] = -a[inc];
                let v204 = a[2] * v738[1];
                let v205 = a[2] * v738[0];
                let v206 = a[1] * v738[0];
                let v208 = a[0] * v738[2];
                let v209 = a[1] * v738[2];
                let v210 = a[0] * v738[1];
                let w =
                    ((v204 - v209) * v775[0] + (v208 - v205) * v775[1]) + (v206 - v210) * v775[2];
                v738 = [v209 - v204, v205 - v208, v210 - v206];
                let f746 = ((w * ai) * f64::from(-r9)) as f32;
                let f747 = (skd * v738[inc]) as f32;
                let f742 = (syd * v738[dec]) as f32;
                let f756 = (num2 * v785 - den2 * v187) as f32;
                let cond = ((f747.to_bits() ^ f742.to_bits()) >> 31) as i32
                    & sb(f756)
                    & sb(f746)
                    & sb(f770);
                let id = ((sk + 1) >> 1) | (2 * r9 + 2) | (((i as i32) | 0x10) << 3) | (r11 + 1);
                code = pick(code, cond, id);
                let take = f64::from(cond);
                let keep = f64::from(cond ^ 1);
                num2 = blend(keep, num2, take, v187);
                den2 = blend(keep, den2, take, v785);
            }
        }
        let nz = i32::from((num2 as f32).to_bits() & 0x7FFF_FFFF != 0);
        let g = (num2 * den - den2 * num) as f32;
        let cond = nz & sb(g);
        code = (saved & cond.wrapping_sub(1)) | (code & cond.wrapping_neg());
        let take = f64::from(cond);
        let keep = f64::from(cond ^ 1);
        den = blend(keep, den, take, den2);
        num = blend(keep, num, take, num2);
    }

    if code == -1 {
        return 0;
    }

    // ---- dir = 0: normalise the chosen direction and write it back
    if dir_zero {
        let l2 = (best[1] * best[1] + best[0] * best[0]) + best[2] * best[2];
        d = best;
        if (l2 - 1.0).abs() > 0.001 {
            if l2 > EPS20_D {
                let inv = 1.0 / l2.sqrt();
                d = [inv * best[0], inv * best[1], inv * best[2]];
            } else {
                d = [0.0, 0.0, 1.0];
            }
        }
        let w = fr.rot_t(d);
        p.dir = Vec3::new(w[0] as f32, w[1] as f32, w[2] as f32);
    }

    // ---- the contact for the chosen feature
    let mut v815: i32 = 0;
    let ratio = |num: f64, den: f64| if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
    match code & 0xE0 {
        0x80 => {
            // box edge against a cap rim
            let i = ((code >> 3) & 3) as usize;
            let inc = INC_MOD3[i] as usize;
            let dec = DEC_MOD3[i] as usize;
            let sxr = ((code >> 1) & 2) - 1;
            let syr = (code & 2) - 1;
            let szr = 2 * (code & 1) - 1;
            v738[i] = 0.0;
            let v229 = f64::from(sxr) * hh;
            let v230 = a[1] * v229 + c[1];
            let v231 = a[2] * v229 + c[2];
            v738[inc] = f64::from(szr) * s[inc];
            v738[dec] = f64::from(syr) * s[dec];
            let x5 = ((v231 - v738[2]) * den - d[2] * num) * a[2];
            let x4 = ((v230 - v738[1]) * den - d[1] * num) * a[1];
            let v233 = c[0] + a[0] * v229;
            let x3 = (v233 - v738[0]) * den - d[0] * num;
            let v234 = 1.0 / (den * a[i]);
            let v235 = ((a[0] * x3 + x4) + x5) * v234;
            let t = (v234 * num) * a[i];
            v738[i] += v235;
            out.t = t;
            let (v238, v239, v237) = (v738[0], v738[1], v738[2]);
            let e244 = v237 + d[2] * t;
            let e245 = v239 + d[1] * t;
            let e246 = v238 + d[0] * t;
            v815 = sb(((v235.abs() - s[i]) - mpd) as f32);
            v738 = [e246, e245, e244];
            out.pt = fr.to_world(v738);
            let v252 = e246 - v233;
            let v255 = e245 - v230;
            let v257 = e244 - v231;
            let v793 = [
                v255 * a[2] - v257 * a[1],
                a[0] * v257 - v252 * a[2],
                v252 * a[1] - a[0] * v255,
            ];
            let mut v775 = [0.0f64; 3];
            v775[inc] = v793[dec];
            v775[dec] = -v793[inc];
            let g = (v775[0] * v238 + v775[1] * v239) + v775[2] * v237;
            let sg = f64::from(sgn(g as f32));
            let n = fr.rot_t([sg * v775[0], v775[1] * sg, v775[2] * sg]);
            out.n = Vec3::new(n[0] as f32, n[1] as f32, n[2] as f32);
            out.i_feature[0] = ((szr + 1) >> 1) | (((i as i32) | 8) << 2) | (syr + 1);
            out.i_feature[1] = ((sxr + 1) | 0x40) >> 1;
        }
        0x60 => {
            // box edge against the side
            let j = ((code >> 3) & 3) as usize;
            let inc = INC_MOD3[j] as usize;
            let dec = DEC_MOD3[j] as usize;
            let s274 = 2 * (code & 1) - 1;
            let s276 = (code & 2) - 1;
            let v277 = d[0] * num;
            let v279 = d[1] * num;
            let v278 = d[2] * num;
            let mut v775 = [0.0f64; 3];
            v775[inc] = a[dec];
            v775[dec] = -a[inc];
            v738[j] = 0.0;
            let v286 = -v775[0];
            let v285 = -v775[2];
            let v287 = -v775[1];
            v738[inc] = f64::from(s274) * s[inc];
            v738[dec] = f64::from(s276) * s[dec];
            let y6 = (c[1] - v738[1]) * den - v279;
            let v288 = (c[2] - v738[2]) * den - v278;
            let v289 = den * (c[0] - v738[0]) - v277;
            let x2 = (y6 * a[2] - v288 * a[1]) * v286;
            let x3 = (a[0] * v288 - a[2] * v289) * v287;
            let x1 = (a[1] * v289 - a[0] * y6) * v285;
            let v291 = (v287 * v287 + v286 * v286) + v285 * v285;
            let v292 = (x3 + x2) + x1;
            let v294 = 1.0 / (v291 * den);
            let v295 = v292 * v294;
            let t = (v294 * num) * v291;
            v738[j] = v295;
            let f283 = ((v295.abs() - s[j]) - mpd) as f32;
            out.t = t;
            let e = [v738[0] + d[0] * t, v738[1] + d[1] * t, v738[2] + d[2] * t];
            let g = ((((e[1] - c[1]) * a[1] + a[0] * (e[0] - c[0])) + (e[2] - c[2]) * a[2]).abs()
                - hh)
                - mpd;
            v815 = sb(g as f32) & sb(f283);
            v738 = e;
            out.pt = fr.to_world(v738);
            let nf = cyl.center - out.pt;
            let dotf = ((f64::from(ax.x) * f64::from(nf.x) + f64::from(ax.y) * f64::from(nf.y))
                + f64::from(ax.z) * f64::from(nf.z)) as f32;
            out.n = Vec3::new(
                (f64::from(nf.x) - f64::from(ax.x * dotf)) as f32,
                (f64::from(nf.y) - f64::from(ax.y * dotf)) as f32,
                (f64::from(nf.z) - f64::from(ax.z * dotf)) as f32,
            );
            if v815 == 0 {
                v738[j] = 0.0;
                out.pt = fr.to_world(v738);
            }
            out.i_feature[1] = 0x40;
            out.i_feature[0] = ((s274 + 1) >> 1) | (((j as i32) | 8) << 2) | (s276 + 1);
        }
        0x40 => {
            // box vertex against the side
            let sz = 2 * (code & 1) - 1;
            let sx = ((code >> 1) & 2) - 1;
            let sy = (code & 2) - 1;
            let qz = f64::from(sz) * s[2];
            let qx = f64::from(sx) * s[0];
            let qy = f64::from(sy) * s[1];
            let t = ratio(num, den);
            out.t = t;
            let e2 = qz + t * d[2];
            let e1 = qy + t * d[1];
            let e0 = qx + t * d[0];
            let g =
                ((((e0 - c[0]) * a[0] + (e1 - c[1]) * a[1]) + (e2 - c[2]) * a[2]).abs() - hh) - mpd;
            v815 = sb(g as f32);
            v738 = [e0, e1, e2];
            out.pt = fr.to_world(v738);
            let mut n = cyl.center - out.pt;
            let dot = (ax.y * n.y + ax.x * n.x) + ax.z * n.z;
            n.x -= ax.x * dot;
            n.z -= ax.z * dot;
            n.y -= ax.y * dot;
            out.n = n;
            out.i_feature[0] = ((sx + 1) >> 1) | (2 * sz + 2) | (sy + 1);
            out.i_feature[1] = 0x40;
        }
        0x20 => {
            // cap face against the box
            let s337 = 2 * (code & 1) - 1;
            let mut ix = [nsgn(d[0] as f32), nsgn(d[1] as f32), nsgn(d[2] as f32)];
            let t = ratio(num, den);
            out.t = t;
            let aa = [a[0].abs(), a[1].abs(), a[2].abs()];
            let e = sb((aa[0] - aa[1]) as f32);
            let f = sb((aa[e as usize] - aa[2]) as f32);
            let cc = (2 * f) | e;
            let id = (cc & (((cc ^ 2) | 4) >> 1)) as usize;
            let mut v364 = 0;
            if a[id].abs() > 0.999 {
                let inc = INC_MOD3[id] as usize;
                let dec = DEC_MOD3[id] as usize;
                v738 = [0.0; 3];
                v738[id] = f64::from(ix[id]) * s[id];
                let v354 = f64::from(s337) * hh;
                let v350 = t * d[1] - c[1];
                let v351 = t * d[2] - c[2];
                let v352 = t * d[0] - c[0];
                v738[0] += v352 - a[0] * v354;
                v738[1] += v350 - v354 * a[1];
                v738[2] += v351 - v354 * a[2];
                ix[inc] = nsgn(v738[inc] as f32);
                ix[dec] = nsgn(v738[dec] as f32);
                let g = ((((t * d[0] + f64::from(ix[0]) * s[0]) - c[0]) - v354 * a[0]).powi(2)
                    + (((f64::from(ix[1]) * s[1] + t * d[1]) - c[1]) - v354 * a[1]).powi(2))
                    + (((f64::from(ix[2]) * s[2] + t * d[2]) - c[2]) - v354 * a[2]).powi(2)
                    - (mpd + r) * (mpd + r);
                v364 = sb(g as f32);
                v815 = v364;
                ix[inc] &= v364.wrapping_neg();
                ix[dec] &= v364.wrapping_neg();
            }
            let v366 = t * d[0] + f64::from(ix[0]) * s[0];
            let v367 = f64::from(ix[1]) * s[1] + t * d[1];
            let v368 = f64::from(ix[2]) * s[2] + t * d[2];
            v738 = [v366, v367, v368];
            if v364 == 0 {
                let v370 = f64::from(s337) * hh;
                let g = (((v366 - c[0]) - v370 * a[0]).powi(2)
                    + ((v367 - c[1]) - v370 * a[1]).powi(2))
                    + ((v368 - c[2]) - v370 * a[2]).powi(2)
                    - (mpd + r) * (mpd + r);
                v815 = sb(g as f32);
            }
            out.pt = fr.to_world(v738);
            let nf = (f64::from(-s337) * hh) as f32;
            out.n = Vec3::new(ax.x * nf, ax.y * nf, ax.z * nf);
            out.i_feature[0] = ((ix[0] + 1) >> 1) | (2 * ix[2] + 2) | (ix[1] + 1);
            out.i_feature[1] = ((s337 + 1) >> 1) + 0x41;
        }
        0 => {
            // box face against the side
            let i = (code >> 2) as usize;
            let s382 = (code & 2) - 1;
            let s9 = 2 * (code & 1) - 1;
            let t = ratio(num, den);
            out.t = t;
            let ai = a[i];
            let mut v751 = [a[0] * ai, ai * a[1], ai * a[2]];
            v751[i] -= 1.0;
            let sd = f64::from(s382);
            let mut w = [v751[0] * sd, v751[1] * sd, v751[2] * sd];
            let mut l = ((w[0] * w[0] + w[1] * w[1]) + w[2] * w[2]).sqrt();
            if lt_or_nan_d(l, 0.001) {
                let x5 = t * d[1] - c[1];
                let x4 = t * d[0] - c[0];
                let x3 = t * d[2] - c[2];
                let v390 = (x5 * a[1] + a[0] * x4) + x3 * a[2];
                w = [x4 - a[0] * v390, x5 - v390 * a[1], x3 - v390 * a[2]];
                let l2 = ((w[0] * w[0] + w[1] * w[1]) + w[2] * w[2]).sqrt();
                l = ((1e-10 - l2).abs() + (l2 + 1e-10)) * 0.5;
            }
            let v392 = r / l;
            let v394 = f64::from(s9) * hh;
            v738 = [
                (c[0] + a[0] * v394) + w[0] * v392,
                (v394 * a[1] + c[1]) + v392 * w[1],
                (v394 * a[2] + c[2]) + v392 * w[2],
            ];
            let inc = INC_MOD3[i] as usize;
            let dec = DEC_MOD3[i] as usize;
            let fi = (((v738[inc] - t * d[inc]).abs() - s[inc]) - mpd) as f32;
            let fd = (((v738[dec] - t * d[dec]).abs() - s[dec]) - mpd) as f32;
            v815 = sb(fd) & sb(fi);
            out.pt = fr.to_world(v738);
            out.n = fr.row_scaled(i, s382 as f32);
            out.i_feature[0] = (2 * ((i as i32) | 0x20)) | ((s382 + 1) >> 1);
            out.i_feature[1] = ((s9 + 1) | 0x40) >> 1;
        }
        _ => {}
    }

    // ---- contact area
    let mut ar = match area {
        Some(a) => Area::User(a),
        None if p.flag == 0 => return finish(p, &fr, cyl, b, out, v815, None),
        None => Area::Static(Box::new(StaticArea {
            i_type: 0,
            npt: 0,
            buf: [0; 20],
            pt: [Vec3::ZERO; 16],
            n: Vec3::ZERO,
        })),
    };
    area_contact(p, &fr, cyl, &a, &c, &d, &s, r, hh, side, out, v815, &mut ar);
    finish(p, &fr, cyl, b, out, v815, Some(&ar))
}

/// The contact-area part (from 0x351834EE up to LABEL_133). `x * -1.0` is kept as the binary's
/// `mulsd` by −1.0 (it differs from a negation for NaN).
#[allow(
    clippy::neg_multiply,
    clippy::too_many_arguments,
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
fn area_contact(
    p: &UnprojParams,
    fr: &Frame,
    cyl: &CylinderPrim,
    a: &[f64; 3],
    c: &[f64; 3],
    d: &[f64; 3],
    s: &[f64; 3],
    r: f64,
    hh: f64,
    side: bool,
    out: &mut UnprojResult,
    v815: i32,
    ar: &mut Area,
) {
    let mpd = f64::from(p.min_pt_dist);
    let r2 = r * r;
    let ax = cyl.axis;
    let mut v738 = [0.0f64; 3];
    let mut v751;
    let mut v742: i32 = -1;

    // ---- the axis (nearly) parallel to a box axis: the cap disc on a face
    let aa = [a[0].abs(), a[1].abs(), a[2].abs()];
    let m1 = ((aa[0] - aa[1]).abs() + (aa[0] + aa[1])) * 0.5;
    let m = ((m1 - aa[2]).abs() + (m1 + aa[2])) * 0.5;
    if m > f64::from(p.max_cos) {
        let s0 = sb((0.998 - aa[0]) as f32);
        let s1 = sb((0.998 - aa[1]) as f32);
        let s2 = sb((0.998 - aa[2]) as f32);
        let id: i32 = if s2 != 0 {
            2
        } else if s1 != 0 {
            1
        } else if s0 != 0 {
            0
        } else {
            -1
        };
        if id >= 0 {
            let i = id as usize;
            let inc = INC_MOD3[i] as usize;
            let dec = DEC_MOD3[i] as usize;
            let dot = (a[0] * d[0] + d[1] * a[1]) + d[2] * a[2];
            let v419 = sgn(dot as f32);
            let t = out.t;
            let v420 = d[1] * t;
            let v421 = d[0] * t;
            let v422 = d[2] * t;
            let v423 = f64::from(v419) * hh;
            v751 = [
                (c[0] - v421) + a[0] * v423,
                (c[1] - v420) + v423 * a[1],
                (c[2] - v422) + v423 * a[2],
            ];
            let v427 = nsgn(d[i] as f32);
            let v428 = v751[inc];
            let v429 = v751[i].abs();
            let v430 = v428.abs() - s[inc];
            let v431 = ((-v430).abs() + v430) * 0.5;
            let x0 = v751[dec].abs() - s[dec];
            let v432 = ((-x0).abs() + x0) * 0.5;
            let lhs = (v429 - s[i]).abs();
            let rhs = ((s[i] + hh) - (s[i] - hh).abs()) * 0.5 * 0.1;
            if lhs >= rhs || v432 * v432 + v431 * v431 >= r2 {
                v742 = id;
            } else if side {
                // 8 points of the cap circle
                let v437 = ((v419 + 1) | 0x40) >> 1;
                let v438 = ((v427 + 1) >> 1) | (2 * (id | 0x20));
                let v463 = v427 + 1;
                v738[i] = f64::from(v427) * s[i];
                let mut v433 = r;
                let mut v434 = 0.0f64;
                for _ in 0..8 {
                    v738[inc] = v428 + v433;
                    v738[dec] = v751[dec] + v434;
                    let n = ar.npt();
                    ar.set_pt(n, fr.area_pt(v738, p.dir, out.t));
                    ar.set_prim(1, n, 0);
                    ar.set_prim(0, n, 0);
                    ar.set_feat(0, n, v438);
                    ar.set_feat(1, n, v437);
                    let fi = (v738[inc].abs() - s[inc]) as f32;
                    let fd = (v738[dec].abs() - s[dec]) as f32;
                    let nv = (v433 - v434) * SQRT_HALF;
                    ar.set_npt(n + (sb(fd) & sb(fi)));
                    v434 = (v433 + v434) * SQRT_HALF;
                    v433 = nv;
                }
                // where the circle crosses the face edges
                for mm in 0..4i32 {
                    let rc = if mm & 1 == 0 { inc } else { dec };
                    let v468 = f64::from((mm & 2) - 1) * s[rc];
                    v738[rc] = v468;
                    let v469 = r2 - (v468 - v751[rc]) * (v468 - v751[rc]);
                    if le_or_nan_d(v469, 0.0) {
                        continue;
                    }
                    let sq = v469.sqrt();
                    let even = i32::from(mm & 1 == 0);
                    let other = if even == 1 { dec } else { inc };
                    let f0 = (((other as i32) | 8) << 2) | (v463 >> even) | ((mm & 2) >> (mm & 1));
                    let so = s[other];
                    let nso = -so;
                    v738[other] = v751[other] - sq;
                    let v474 = sq * 2.0;
                    for _ in 0..2 {
                        let v475 = v738[other];
                        let v478 = ((so + v475) - (so - v475).abs()) * 0.5;
                        let v479 = (nso - v478).abs() + (nso + v478);
                        v738[other] = v479 * 0.5;
                        let n = ar.npt();
                        ar.set_pt(n, fr.area_pt(v738, p.dir, out.t));
                        v738[other] = v474 + v475;
                        ar.set_prim(1, n, 0);
                        ar.set_prim(0, n, 0);
                        ar.set_feat(0, n, f0);
                        ar.set_feat(1, n, v437);
                        ar.set_npt(n + 1);
                    }
                }
                ar.set_type(0);
                out.n = fr.row_scaled(i, v427 as f32);
                let f = v419 as f32;
                ar.set_n(Vec3::new(ax.x * f, ax.y * f, ax.z * f));
                return;
            }
        }
    }

    // ---- the side parallel to a box face
    let thr = f64::from((1.0f32 - p.max_cos) * 2.0f32);
    for i in 0..3usize {
        if a[i] * a[i] >= thr {
            continue;
        }
        let t = out.t;
        let si = s[i];
        let inc = INC_MOD3[i] as usize;
        let dec = DEC_MOD3[i] as usize;
        let v514 = c[0] - t * d[0];
        let v752 = c[1] - t * d[1];
        let v753 = c[2] - t * d[2];
        v751 = [v514, v752, v753];
        let v517 = nsgn(d[i] as f32);
        let lhs = ((v751[i].abs() - si) - r).abs();
        let rhs = ((si + r) - (si - r).abs()) * 0.5 * 0.1;
        if lt_or_nan_d(lhs, rhs) {
            // a side line lying on face i: clip it to the face rectangle
            let v518 = v751[inc];
            let v519 = v751[dec];
            ar.set_feat(1, 1, 0x40);
            ar.set_feat(1, 0, 0x40);
            let v522 = hh * a[inc];
            let v523 = hh * a[dec];
            let v526 = (v517 + 1) >> 1;
            let v528 = (2 * ((i as i32) | 0x20)) | v526;
            let mut seg = [0.0f32; 4];
            seg[0] = (v518 - v522) as f32;
            ar.set_feat(0, 1, v528);
            ar.set_feat(0, 0, v528);
            ar.set_prim(1, 1, 0);
            ar.set_prim(1, 0, 0);
            seg[1] = (v519 - v523) as f32;
            ar.set_prim(0, 1, 0);
            ar.set_prim(0, 0, 0);
            seg[2] = (v518 + v522) as f32;
            seg[3] = (v519 + v523) as f32;
            v738[i] = f64::from(v517) * si;
            ar.clip(
                &mut seg,
                s[inc] as f32,
                0,
                (((dec as i32) | 8) << 2) | v526,
                0x40,
            );
            ar.clip(
                &mut seg,
                s[dec] as f32,
                1,
                (((inc as i32) | 8) << 2) | (v517 + 1),
                0x40,
            );
            let len2 =
                (seg[1] - seg[3]) * (seg[1] - seg[3]) + (seg[0] - seg[2]) * (seg[0] - seg[2]);
            if f64::from(len2) > mpd * mpd {
                ar.set_type(1);
                v738[inc] = f64::from(seg[0]);
                v738[dec] = f64::from(seg[1]);
                ar.set_pt(0, fr.area_pt(v738, p.dir, out.t));
                v738[inc] = f64::from(seg[2]);
                v738[dec] = f64::from(seg[3]);
                ar.set_pt(1, fr.area_pt(v738, p.dir, out.t));
                let n = fr.row_scaled(i, v517 as f32);
                out.n = n;
                let v604 = n.z * ax.y - n.y * ax.z;
                let v595 = ax.x * n.y - n.x * ax.y;
                let v579 = n.x * ax.z - ax.x * n.z;
                let an = Vec3::new(
                    v595 * ax.y - v579 * ax.z,
                    v604 * ax.z - v595 * ax.x,
                    v579 * ax.x - v604 * ax.y,
                );
                ar.set_n(an);
                ar.set_n(normalized_or(an, -out.n));
                ar.set_npt(2);
                return;
            }
        }
        if !side {
            continue;
        }
        // a side line crossing an edge of face i
        v738[i] = 0.0;
        let s543 = s[dec];
        let s544 = s[inc];
        v738[inc] = s544;
        let dt = (a[0] * d[0] + d[1] * a[1]) + d[2] * a[2];
        let v546 = sgn(dt as f32);
        v738[dec] = s543 * -1.0;
        let v547 = f64::from(v546) * hh;
        let v548 = a[0] * v547 + v514;
        let v549 = v547 * a[1] + v752;
        let v550 = v547 * a[2] + v753;
        v751 = [v548, v549, v550];
        let dd =
            |q: &[f64; 3]| ((q[0] - v548) * a[0] + (q[1] - v549) * a[1]) + (q[2] - v550) * a[2];
        let d1 = dd(&v738);
        v738[inc] = s544 * -1.0;
        v738[dec] = s543 * -1.0;
        let d2 = dd(&v738);
        v738[inc] = s544 * -1.0;
        v738[dec] = s543;
        let c1 = sb((d1.abs() - d2.abs()) as f32);
        let d3 = dd(&v738).abs();
        v738[inc] = f64::from(2 * (c1 & 1) - 1) * s544;
        v738[dec] = f64::from((c1 & 2) - 1) * s543;
        let d4 = dd(&v738).abs();
        v738[inc] = s544;
        v738[dec] = s543;
        let c2 = sb((d3 - d4) as f32);
        let e = (c2.wrapping_sub(1) & c1) | (c2.wrapping_neg() & 2);
        let d5 = dd(&v738).abs();
        v738[inc] = f64::from(2 * (e & 1) - 1) * s544;
        v738[dec] = f64::from((e & 2) - 1) * s543;
        let d6 = dd(&v738).abs();
        let c3 = sb((d5 - d6) as f32);
        let g = (c3.wrapping_sub(1) & e) | (c3.wrapping_neg() & 3);
        let v565 = (g & 2) - 1;
        let v806 = 2 * (g & 1) - 1;
        let x1 = f64::from(v565) * s543;
        v738[inc] = f64::from(v806) * s544;
        v738[dec] = x1;
        let v569 =
            (v751[dec] - x1) * (v751[dec] - x1) + (v751[inc] - v738[inc]) * (v751[inc] - v738[inc]);
        let v570 = ((v548 - v738[0]) * a[0] + (v549 - v738[1]) * a[1]) + (v550 - v738[2]) * a[2];
        if v570.abs() >= hh * 0.1 || v569 >= r2 {
            continue;
        }
        let v573 = (r2 - v569).sqrt();
        let v572 = v751[i];
        // Bug kept: always true for s ≥ 0 (see the module docs).
        if lt_or_nan_d(v572 - v573, si) || v572 + v573 > -si {
            let nsi = -si;
            for (k, e) in [-1.0f64, 1.0].into_iter().enumerate() {
                let x = e * v573 + v572;
                let v619 = ((nsi - x).abs() + (nsi + x)) * 0.5;
                let v620 = ((si + v619) - (si - v619).abs()) * 0.5;
                v738[i] = v620;
                ar.set_pt(k as i32, fr.area_pt(v738, p.dir, out.t));
            }
            ar.set_type(1);
            ar.set_prim(1, 1, 0);
            ar.set_prim(1, 0, 0);
            ar.set_prim(0, 1, 0);
            ar.set_prim(0, 0, 0);
            let f0 = ((v806 + 1) >> 1) | (((i as i32) | 8) << 2) | (v565 + 1);
            ar.set_feat(0, 1, f0);
            ar.set_feat(0, 0, f0);
            let f1 = (((v546 + 1) >> 1) + 1) | 0x40;
            ar.set_feat(1, 1, f1);
            ar.set_feat(1, 0, f1);
            let fs = v546 as f32;
            let an = Vec3::new(ax.x * fs, ax.y * fs, ax.z * fs);
            ar.set_n(an);
            let n = fr.row(i);
            let v636 = an.x * n.y;
            let v637 = an.z * n.y - an.y * n.z;
            let v638 = an.x * n.z - n.x * an.z;
            let v639 = n.x * an.y - v636;
            let cn = Vec3::new(
                n.y * v639 - n.z * v638,
                v637 * n.z - n.x * v639,
                n.x * v638 - v637 * n.y,
            );
            out.n = normalized_or(cn, -an);
            ar.set_npt(2);
            return;
        }
    }

    // ---- the cap rim touching a face
    if v742 < 0 {
        return;
    }
    let i = v742 as usize;
    let t = out.t;
    let inc = INC_MOD3[i] as usize;
    let dec = DEC_MOD3[i] as usize;
    v751 = [c[0] - d[0] * t, c[1] - d[1] * t, c[2] - d[2] * t];
    let v656 = s[i];
    let v657 = s[dec];
    let v658 = s[inc];
    let x0 = v751[inc].abs() - v658;
    let v659 = ((-x0).abs() + x0) * 0.5;
    let x1 = v751[dec].abs() - v657;
    let v660 = ((-x1).abs() + x1) * 0.5;
    if v751[i].abs() - v656 >= hh {
        return;
    }
    let rt = r * 0.1;
    if ((v660 * v660 + v659 * v659) - r2).abs() >= rt * rt {
        return;
    }
    ar.set_type(1);
    let v663 = a[i].abs() * hh;
    let v664 = (c[i] - t * d[i]) - v663;
    v751[i] = v664;
    let e = sgn(v751[inc] as f32);
    let v667 = sgn(v751[dec] as f32);
    v738[inc] = f64::from(e) * v658;
    v738[dec] = f64::from(v667) * v657;
    let ns = -v656;
    v738[i] = ((v664 - ns).abs() + (v664 + ns)) * 0.5;
    ar.set_pt(0, fr.area_pt(v738, p.dir, out.t));
    let x = v663 * 2.0 + v664;
    v738[i] = ((x + v656) - (x - v656).abs()) * 0.5;
    ar.set_pt(1, fr.area_pt(v738, p.dir, out.t));
    let f0 = (((i as i32) | 8) << 2) | ((e + 1) >> 1) | (v667 + 1);
    ar.set_feat(0, 1, f0);
    ar.set_feat(0, 0, f0);
    ar.set_feat(1, 1, 0x40);
    ar.set_feat(1, 0, 0x40);
    ar.set_prim(1, 1, 0);
    ar.set_prim(1, 0, 0);
    ar.set_prim(0, 1, 0);
    ar.set_prim(0, 0, 0);
    if v815 == 0 {
        let p0 = ar.pt(0);
        let mut n = cyl.center - p0;
        let dot = (ax.y * n.y + ax.x * n.x) + ax.z * n.z;
        n.x -= ax.x * dot;
        n.z -= ax.z * dot;
        n.y -= ax.y * dot;
        normalize_in_place(&mut n);
        out.n = n;
    }
    ar.set_n(-out.n);
    ar.set_npt(2);
}

/// The float normalisation used for the area / result normals: if `|v|² > 0.001`, `v/|v|`
/// (with `|v|² ≤ 1e-20` → (0,0,1), never reached), else `fallback`.
fn normalized_or(v: Vec3, fallback: Vec3) -> Vec3 {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 > 0.001f32 {
        let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
        if l2 > 1e-20f32 {
            let inv = 1.0f32 / l2.sqrt();
            Vec3::new(inv * v.x, inv * v.y, inv * v.z)
        } else {
            Vec3::new(0.0, 0.0, 1.0)
        }
    } else {
        fallback
    }
}

/// LABEL_133..: take point 0 of the area (if any), then the final on-both-surfaces check.
fn finish(
    p: &UnprojParams,
    fr: &Frame,
    cyl: &CylinderPrim,
    b: &BoxPrim,
    out: &mut UnprojResult,
    v815: i32,
    ar: Option<&Area>,
) -> i32 {
    let flag1 = p.flag ^ 1;
    let has_pts = match ar {
        Some(ar) if ar.npt() != 0 => {
            out.pt = ar.pt(0);
            out.i_feature[0] = ar.feat(0, 0);
            out.i_feature[1] = ar.feat(1, 0);
            true
        }
        _ => false,
    };
    if !has_pts && v815 == 0 {
        return flag1;
    }
    // LABEL_136: is pt (moved back by dir·t) on the box surface and on the cylinder surface?
    let s = [
        f64::from(b.size.x),
        f64::from(b.size.y),
        f64::from(b.size.z),
    ];
    let r = f64::from(cyl.r);
    let m1 = ((s[0] + s[1]) - (s[0] - s[1]).abs()) * 0.5;
    let m2 = ((s[2] + m1) - (m1 - s[2]).abs()) * 0.5;
    let eps = (((m2 + r) - (m2 - r).abs()) * 0.5) * f64::from(0.05f32);
    let tf = out.t as f32;
    let rr = fr.r;
    let px = (out.pt.x - b.center.x) - p.dir.x * tf;
    let py = (out.pt.y - b.center.y) - p.dir.y * tf;
    let pz = (out.pt.z - b.center.z) - p.dir.z * tf;
    let lx = (py * rr[1] + px * rr[0]) + pz * rr[2];
    let ly = (py * rr[4] + px * rr[3]) + pz * rr[5];
    let lz = (px * rr[6] + py * rr[7]) + pz * rr[8];
    let dx = f64::from(lx.abs()) - s[0];
    let dy = f64::from(ly.abs()) - s[1];
    let dz = f64::from(lz.abs()) - s[2];
    let ne = -eps;
    let ma = ((ne - dx).abs() + (dx + ne)) * 0.5;
    let mb = ((ne - dy).abs() + (ne + dy)) * 0.5;
    let mc = ((ne - dz).abs() + (ne + dz)) * 0.5;
    let k1 = eps * f64::from(-2.99f32);
    let k2 = eps * 3.0;
    let g1 = (((k1 + k2) - ((ma + mb) + mc) * 2.0).abs() - (k1 - k2).abs()) as f32;

    let ax = cyl.axis;
    let q = out.pt - cyl.center;
    let proj = (f64::from(ax.x) * f64::from(q.x) + f64::from(ax.y) * f64::from(q.y))
        + f64::from(ax.z) * f64::from(q.z);
    let v728 = (proj.abs() - f64::from(cyl.hh)) * r;
    let pf = proj as f32;
    let ex = f64::from(q.x) - f64::from(ax.x * pf);
    let ey = f64::from(q.y) - f64::from(ax.y * pf);
    let ez = f64::from(q.z) - f64::from(ax.z * pf);
    let v733 = ((ey * ey + ex * ex) + ez * ez) - f64::from(cyl.r * cyl.r);
    let nre = -(r * eps);
    let n1 = ((nre - v728).abs() + (nre + v728)) * 0.5;
    let n2 = ((nre - v733).abs() + (nre + v733)) * 0.5;
    let k3 = eps * f64::from(-1.99f32) * r;
    let k4 = eps * 2.0 * r;
    let g2 = (((k3 + k4) - (n1 + n2) * 2.0).abs() - (k3 - k4).abs()) as f32;
    let ok = sb(g1) & sb(g2);
    if ok == 0 {
        0
    } else {
        flag1 | ok
    }
}

fn cell_box_cyl(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_box_cyl(p, as_box(a), fa, as_cyl(b), fb, out, area)
}

/// 0x35185AA0: cyl–box, the 215-byte swap wrapper around [`unproject_box_cyl`].
fn cell_cyl_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    let cy = as_cyl(a);
    let bx = as_box(b);
    swap_linear_area(p, out, area, |p, out, area| {
        unproject_box_cyl(p, bx, fb, cy, fa, out, area)
    })
}

/// Fill the cells this group owns: `[0][box][cyl]` and `[0][cyl][box]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(MODE_LINEAR, GeomType::Box, GeomType::Cylinder, cell_box_cyl);
    t.set(MODE_LINEAR, GeomType::Cylinder, GeomType::Box, cell_cyl_box);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scene() -> (BoxPrim, CylinderPrim) {
        let b = BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0));
        let c = CylinderPrim {
            center: Vec3::new(1.25, 0.0, 0.0),
            axis: Vec3::Z,
            r: 0.5,
            hh: 0.5,
        };
        (b, c)
    }

    /// Side of an upright cylinder pushed 0.25 into the +x face of a unit box.
    #[test]
    fn side_line_on_face() {
        let (b, c) = scene();
        let mut p = UnprojParams {
            max_cos: 0.1,
            min_pt_dist: 0.01,
            tmax: 10.0,
            ..Default::default()
        };
        let mut out = UnprojResult::default();
        let mut area = crate::collision::swept::new_area(16);
        let r = unproject_box_cyl(&mut p, &b, -1, &c, -1, &mut out, Some(&mut area));
        assert_eq!(r, 1);
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
        assert_eq!(out.t, 0.25);
        assert_eq!(out.n, Vec3::X);
        assert_eq!(out.i_feature, [0x41, 0x40]);
        assert_eq!(area.npt, 2);
        assert_eq!(area.i_type, 1);
        assert_eq!(area.pt[0], Vec3::new(0.75, 0.0, -0.5));
        assert_eq!(area.pt[1], Vec3::new(0.75, 0.0, 0.5));
        assert_eq!(out.pt, area.pt[0]);
        assert_eq!(area.n, Vec3::new(-1.0, 0.0, 0.0));
    }

    /// Without an area but with `flag = 1` the static area is used; its four id arrays share one
    /// buffer, so the features read back are the last value written (0 on this path).
    #[test]
    fn static_area_aliasing() {
        let (b, c) = scene();
        let mut p = UnprojParams {
            dir: Vec3::new(-1.0, 0.0, 0.0),
            flag: 1,
            max_cos: 0.1,
            min_pt_dist: 0.01,
            tmax: 10.0,
            ..Default::default()
        };
        let mut out = UnprojResult::default();
        let r = unproject_box_cyl(&mut p, &b, -1, &c, -1, &mut out, None);
        assert_eq!(r, 1);
        assert_eq!(out.t, 0.25);
        assert_eq!(out.pt, Vec3::new(0.75, 0.0, -0.5));
        assert_eq!(out.i_feature, [0, 0]);
        // flag = 0: no area work at all, the switch's feature ids stand
        p.flag = 0;
        let mut out2 = UnprojResult::default();
        assert_eq!(
            unproject_box_cyl(&mut p, &b, -1, &c, -1, &mut out2, None),
            1
        );
        assert_eq!(out2.t, 0.25);
        assert_ne!(out2.i_feature, [0, 0]);
    }

    /// Deep overlap with a small `tmax`: no candidate below `tmax`, 0 without any write.
    #[test]
    fn no_candidate_returns_zero_without_writes() {
        let (b, mut c) = scene();
        c.center = Vec3::ZERO;
        let mut p = UnprojParams {
            dir: Vec3::new(1.0, 0.0, 0.0),
            max_cos: 0.1,
            tmax: 0.1,
            ..Default::default()
        };
        let mut out = UnprojResult::default();
        assert_eq!(unproject_box_cyl(&mut p, &b, -1, &c, -1, &mut out, None), 0);
        assert_eq!(out, UnprojResult::default());
    }
}
