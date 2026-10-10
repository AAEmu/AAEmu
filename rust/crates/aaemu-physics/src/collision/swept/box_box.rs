//! box–box linear unprojection (x64 0x3517CD60, x86 0x35180410) and its polygon clipper
//! (x64 0x3516E400).
//!
//! # sub_3517CD60 — `UNPROJECT[0][box][box]`
//!
//! Everything happens in A's frame, mostly in double precision:
//!
//! * `S[0..6]` = half sizes of A and B, `CC[0..3]` = B's centre in A's frame, `CC[3..6]` = A's
//!   centre in B's frame, `D[0..3]` = `dir` in A's frame, `D[3..6]` = `−dir` in B's frame (all
//!   rotated in float, then widened), `R = B.basis · A.basisᵀ` (sub_35022380, float, widened):
//!   row `i` is B's axis `i` in A's frame. `dir = 0` (squared length with all bits but the sign
//!   zero) selects the least-penetration mode.
//! * The best candidate is kept as a fraction `num/den` (initially `tmax/1`) and replaced
//!   branch-free when a candidate has `numer > 0` and `numer/dd < num/den`, both tested on the
//!   **sign bit of the float-rounded** values (`−numer`, `den·numer − dd·num`), so `−0.0` counts
//!   as negative (a candidate with `numer = +0` is taken).
//! * **Face axes** (A's 3, then B's 3 with `R` transposed in place): `P` = own face vertex
//!   coordinate `−sgn(D_j)·S_j`, `Q` = the other box's support vertex along `sgn(D_j)·axis_j`;
//!   `numer = (Q_j − P_j)·sgn(D_j)`, `dd = |D_j|`. In `dir = 0` mode `D` (of that frame) is reset
//!   to the axis, pointing against the centre offset. Code = `outer<<5 | j<<3 | vertex sign bits`.
//! * **Edge axes** (`c = A_j × B_i`, skipped if `|c|² < 1e-8` or NaN): edge of A along `j` versus
//!   edge of B along `i`, `numer = (Q − P)·c·sgn(c·D)`, `dd = |c·D|`; in `dir = 0` mode
//!   `D = ±c` (unnormalised). Code = `0x100 | i<<6 | j<<2 | edge sign bits`.
//! * No candidate: return 0 without touching `out` or `params` (`tmin` is never read).
//! * **Edge–edge result**: closest points of the two edge lines after the move; `t = num/den`
//!   (computed as `(1/(|c|²·den))·num·|c|²`), `pt` on A's edge, `n = ±c` rotated to world
//!   (**not normalised**, `|n| = sin` of the edge angle), features `0x20 | axis<<2 | signs`. In
//!   `dir = 0` mode the written-back `dir` is `±c` in world space (also not normalised).
//! * **Face–vertex result**: `t = num/den` (`0` if `den` is 0 or NaN), `pt` = the other box's
//!   vertex (moved by `dir·t` if it belongs to A), `n` = the face normal (pointing towards A),
//!   face feature `0x40 | axis | (D_axis < 0)` (**sic**: the axis is not shifted, so axis 1 and the
//!   side bit overlap; the area code uses `0x40 | 2·axis | side`), vertex feature = sign bits
//!   (complemented when the face points the other way).
//! * **Area** (`parea != NULL`), with `t` = the result:
//!   1. Face–face: for each B face `k` whose largest `|R_k·|` exceeds `maxcos`, take the most
//!      aligned A axis `m`; if both faces touch after the move (`min(|gap_B|, |gap_A|) <
//!      0.003·(S_Bk + S_Am)`) and `|D_m| > 0.01`, clip B's face quad (in A's face plane, after
//!      the move) against A's face rectangle with sub_3516E400 (4 lines), and output the
//!      polygon (`i_type = 0`, area normal = B's face normal towards `dir`, `out.n` = A's face
//!      normal ×(−D_m), unnormalised by `|D_m|`; point features from the polygon edge ids:
//!      edge×edge, or vertex + face).
//!   2. Edge–face: for each `(i, j)` with `R_ij² < 2·(1 − maxcos)` (B edge `i` ⟂ A axis `j`, and
//!      transposed: A edge `j` ⟂ B axis `i`), if the edge lies on the face plane
//!      (`|gap| < 0.05·min(S_j, S_other)`) clip the edge segment to the face rectangle
//!      (sub_3516E6C0, twice) and, if longer than `minPtDist`, output its two ends
//!      (`i_type = 1`; for the transposed case the points are projected back onto A's edge).
//!      Normals: the face normal and the edge's in-plane perpendicular `e × (e × f)` normalised
//!      (`−f` if `|·|² ≤ 0.001`).
//!   3. If any points were produced: `out.pt = area.pt[0]`, `out.iFeature = area features[0]`.
//!
//!   The function returns 1 whenever a candidate was found.
//!
//! Original oddities kept (see `// Bug kept` comments): the face–vertex feature id, the
//! unnormalised `n`/`dir` of the edge–edge result, B's size used for the edge–face threshold in
//! both orientations, an area `npt` passed in non-zero short-circuits the area search and copies
//! `area.pt[0]` into the result, and the area arrays are written without checking `nmaxpt`
//! (up to 2 points for edge–face, up to the clipped polygon size for face–face).
//!
//! **Known divergence (non-finite input only):** the face–face clip uses fixed stack buffers of
//! 8 points / 8 ids (`rbp+0x1F0`/`+0x230`, `rbp+0x150`/`+0x170`; the second point buffer is
//! followed by the saved XMM registers). A convex quad clipped by 4 lines never exceeds 8 points,
//! but with `tmax = ±∞` the best fraction becomes `num = 0·∞ = NaN`, `t` is NaN, every clip step
//! counts as a crossing, and the binary overruns these buffers (corrupting its own locals and the
//! caller's saved XMM registers). The port uses growable buffers instead. Finite inputs never
//! reach this; the `den = 0 → t = 0` branch of the face–vertex result is likewise only
//! reachable with a non-finite `tmax`.
//!
//! No static state; nothing beyond `params+48` is read (`tmin`, `vel`, `flag` are unused).
//!
//! # sub_3516E400 — clip a 2-D polygon by one line
//!
//! See [`clip_polygon_line`].

use super::common::{
    as_box, clip_segment_slab, le_or_nan_d, lt_or_nan, lt_or_nan_d, mat_mul, ucomisd_eq, DEC_MOD3,
    INC_MOD3,
};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{BoxPrim, Primitive};
use crate::ids::GeomType;
use crate::math::Vec3;

/// `INC_MOD3[i]` as an index.
#[inline]
fn inc(i: usize) -> usize {
    INC_MOD3[i] as usize
}

/// `DEC_MOD3[i]` as an index.
#[inline]
fn dec(i: usize) -> usize {
    DEC_MOD3[i] as usize
}

/// `((bits >> 30) & ~1) + 1`: +1 if the sign bit of `x` is clear, −1 if set.
#[inline]
fn psgn(x: f32) -> i32 {
    (((x.to_bits() as i32) >> 30) & !1) + 1
}

/// `−1 − ((bits >> 30) & ~1)`: −1 if the sign bit of `x` is clear, +1 if set.
#[inline]
fn nsgn(x: f32) -> i32 {
    -1 - (((x.to_bits() as i32) >> 30) & !1)
}

/// The sign bit of `x` (0 or 1).
#[inline]
fn sbit(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// A box as the flat float words the binary indexes (`m` = basis rows, `c` centre, `s` size).
struct Bx {
    m: [f32; 9],
    c: [f32; 3],
    s: [f32; 3],
}

impl Bx {
    fn of(b: &BoxPrim) -> Self {
        Bx {
            m: b.basis.to_array(),
            c: b.center.to_array(),
            s: b.size.to_array(),
        }
    }
    fn row(&self, i: usize) -> [f32; 3] {
        [self.m[3 * i], self.m[3 * i + 1], self.m[3 * i + 2]]
    }
}

/// Transpose the 3×3 double matrix in place (the swaps at the end of the binary's loops).
fn transpose(r: &mut [f64; 9]) {
    r.swap(1, 3);
    r.swap(2, 6);
    r.swap(5, 7);
}

/// sub_3516E400: clip the closed 2-D polygon `src` (`n` points `x, y`, with `src_ids[k]` the
/// id of the edge `k → k+1`) by one line of the rectangle `|coord| ≤ half`:
/// `code & 1` = coordinate `i`, the line is `coord_i = h = ((code & 2) − 1)·half[i]`, kept side
/// `coord_i > h` for `code & 2 == 0`, `coord_i ≤ h` otherwise (tested on the sign bit of
/// `h − coord_i`). The walk starts at the **last** kept point; a crossing (product of the
/// offsets of consecutive points `< 0` or NaN) toggles the kept state and inserts the
/// intersection point; its id is `edge_id` when leaving, the crossed edge's id when entering.
/// Points visited while outside are written to `dst` but not counted. Returns the number of
/// points written to `dst`/`dst_ids` (0 and nothing written if no point is kept).
#[allow(clippy::too_many_arguments)]
pub fn clip_polygon_line(
    src: &[f32],
    src_ids: &[i32],
    n: i32,
    half: [f32; 2],
    code: i32,
    edge_id: i32,
    dst: &mut Vec<f32>,
    dst_ids: &mut Vec<i32>,
) -> i32 {
    let i = (code & 1) as usize;
    let side = (code >> 1) & 1;
    let h = ((code & 2) - 1) as f32 * half[i];
    let mut start: i32 = -1;
    for k in 0..n.max(0) {
        let d = h - src[2 * k as usize + i];
        if (side ^ sbit(d)) != 0 {
            start = k;
        }
    }
    if start == -1 {
        return 0;
    }
    let need = (2 * n.max(0) + 2) as usize;
    if dst_ids.len() < need {
        dst_ids.resize(need, 0);
    }
    if dst.len() < 2 * need {
        dst.resize(2 * need, 0.0);
    }
    let mut cnt: i32 = 0;
    let mut inside: i32 = 1;
    let mut cur = start;
    for _ in 0..n {
        let cu = cur as usize;
        let c = cnt as usize;
        dst[2 * c] = src[2 * cu];
        dst[2 * c + 1] = src[2 * cu + 1];
        dst_ids[c] = src_ids[cu];
        cnt += inside;
        let next = (cur + 1) & ((cur - n + 1) >> 31);
        let nx = next as usize;
        let prod = (src[2 * nx + i] - h) * (src[2 * cu + i] - h);
        if lt_or_nan(prod, 0.0) {
            inside ^= 1;
            let delta = [src[2 * nx] - src[2 * cu], src[2 * nx + 1] - src[2 * cu + 1]];
            let c = cnt as usize;
            dst[2 * c + i] = h;
            dst[2 * c + (i ^ 1)] =
                ((h - src[2 * cu + i]) * delta[i ^ 1]) / delta[i] + src[2 * cu + (i ^ 1)];
            dst_ids[c] = ((inside - 1) & edge_id) | (dst_ids[c] & -inside);
            cnt += 1;
        }
        cur = next;
    }
    cnt
}

/// Writable view of the caller's contact area. The binary writes the area arrays without
/// looking at `nmaxpt`; entries past the caller's capacity go to scratch space here (the probe
/// gives the DLL 1024-entry buffers, so they are invisible there too) and are dropped at the
/// end.
struct AreaBuf<'a> {
    a: &'a mut ContactArea,
    len: [usize; 5],
}

impl<'a> AreaBuf<'a> {
    fn new(a: &'a mut ContactArea) -> Self {
        let len = [
            a.pt.len(),
            a.prim[0].len(),
            a.prim[1].len(),
            a.feature[0].len(),
            a.feature[1].len(),
        ];
        AreaBuf { a, len }
    }
    fn grow(&mut self, i: usize) {
        let n = i + 1;
        if self.a.pt.len() < n {
            self.a.pt.resize(n, Vec3::ZERO);
        }
        for v in self.a.prim.iter_mut().chain(self.a.feature.iter_mut()) {
            if v.len() < n {
                v.resize(n, 0);
            }
        }
    }
    fn set_pt(&mut self, i: usize, p: Vec3) {
        self.grow(i);
        self.a.pt[i] = p;
    }
    fn set_prim(&mut self, k: usize, i: usize, v: i32) {
        self.grow(i);
        self.a.prim[k][i] = v;
    }
    fn set_feat(&mut self, k: usize, i: usize, v: i32) {
        self.grow(i);
        self.a.feature[k][i] = v;
    }
    fn feat(&self, k: usize, i: usize) -> i32 {
        self.a.feature[k].get(i).copied().unwrap_or(0)
    }
}

impl Drop for AreaBuf<'_> {
    fn drop(&mut self) {
        self.a.pt.truncate(self.len[0]);
        self.a.prim[0].truncate(self.len[1]);
        self.a.prim[1].truncate(self.len[2]);
        self.a.feature[0].truncate(self.len[3]);
        self.a.feature[1].truncate(self.len[4]);
    }
}

/// sub_3517CD60: move box A along `params.dir` by the smallest `t` that separates it from box
/// B (or, with `dir = 0`, along the least-penetration direction, which is written back). See
/// the module docs for the algorithm, outputs and feature ids.
#[allow(
    clippy::too_many_lines,
    clippy::similar_names,
    clippy::many_single_char_names
)]
pub fn unproject_box_box(
    p: &mut UnprojParams,
    a: &BoxPrim,
    _fa: i32,
    b: &BoxPrim,
    _fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    let ba = Bx::of(a);
    let bb = Bx::of(b);
    let bx: [&Bx; 2] = [&ba, &bb];
    let am = &ba.m;
    let bm = &bb.m;

    let mut s = [0f64; 6];
    for k in 0..3 {
        s[k] = f64::from(ba.s[k]);
        s[3 + k] = f64::from(bb.s[k]);
    }
    let mut num = f64::from(p.tmax);
    let mut den = 1.0f64;

    // B's centre in A's frame, A's centre in B's frame (float, widened)
    let mut cc = [0f64; 6];
    {
        let dx = bb.c[0] - ba.c[0];
        let dy = bb.c[1] - ba.c[1];
        let dz = bb.c[2] - ba.c[2];
        cc[0] = f64::from((dy * am[1] + dx * am[0]) + dz * am[2]);
        cc[1] = f64::from((dy * am[4] + dx * am[3]) + dz * am[5]);
        cc[2] = f64::from((dy * am[7] + dx * am[6]) + dz * am[8]);
        let ex = ba.c[0] - bb.c[0];
        let ey = ba.c[1] - bb.c[1];
        let ez = ba.c[2] - bb.c[2];
        cc[3] = f64::from((ex * bm[0] + ey * bm[1]) + ez * bm[2]);
        cc[4] = f64::from((ex * bm[3] + ey * bm[4]) + ez * bm[5]);
        cc[5] = f64::from((ex * bm[6] + ey * bm[7]) + ez * bm[8]);
    }
    // dir in A's frame, −dir in B's frame
    let mut d = [0f64; 6];
    {
        let (dx, dy, dz) = (p.dir.x, p.dir.y, p.dir.z);
        d[0] = f64::from((dx * am[0] + dy * am[1]) + dz * am[2]);
        d[1] = f64::from((dy * am[4] + dx * am[3]) + dz * am[5]);
        d[2] = f64::from((dy * am[7] + dx * am[6]) + dz * am[8]);
        let (nx, ny, nz) = (-dx, -dy, -dz);
        d[3] = f64::from((nx * bm[0] + ny * bm[1]) + nz * bm[2]);
        d[4] = f64::from((nx * bm[3] + ny * bm[4]) + nz * bm[5]);
        d[5] = f64::from((nx * bm[6] + ny * bm[7]) + nz * bm[8]);
    }
    // R = B.basis · A.basisᵀ (sub_35022380)
    let at = b_transpose(am);
    let r32 = mat_mul(bm, &at);
    let mut r = r32.map(f64::from);

    let l2 = (p.dir.y * p.dir.y + p.dir.x * p.dir.x) + p.dir.z * p.dir.z;
    let zero = (l2.to_bits() & 0x7FFF_FFFF) == 0;

    let mut code: i32 = -1;

    // ---- face axes: A's (outer 0), then B's (outer 1, R transposed)
    for outer in 0..2usize {
        let o = outer ^ 1;
        for j in 0..3usize {
            let idx = 3 * outer + j;
            if zero {
                d[3 * outer] = 0.0;
                d[3 * outer + 1] = 0.0;
                d[3 * outer + 2] = 0.0;
                d[idx] = f64::from(nsgn(cc[idx] as f32));
            }
            let dj = d[idx];
            let v94 = f64::from(nsgn(dj as f32));
            let s0 = psgn(r[j] as f32);
            let s1 = psgn(r[3 + j] as f32);
            let s2 = psgn(r[6 + j] as f32);
            let pj = v94 * s[idx];
            let v1 = f64::from(s1) * s[3 * o + 1];
            let v2 = f64::from(s2) * s[3 * o + 2];
            let v0 = f64::from(s0) * s[3 * o];
            let q = [
                cc[3 * outer] - ((r[0] * v0 + r[3] * v1) + r[6] * v2) * v94,
                cc[3 * outer + 1] - ((r[1] * v0 + r[4] * v1) + r[7] * v2) * v94,
                cc[3 * outer + 2] - ((r[2] * v0 + r[5] * v1) + r[8] * v2) * v94,
            ];
            let sd = f64::from(psgn(dj as f32));
            let numer = (q[j] - pj) * sd;
            let dd = dj * sd;
            let acc = sbit((-numer) as f32) & sbit((den * numer - dd * num) as f32);
            let c = (((s0 + 1) >> 1) + s1 + 2 * s2 + (8 * j as i32 + 32 * outer as i32) + 3) & -acc;
            code = c | ((acc - 1) & code);
            let fa = f64::from(acc);
            let fna = f64::from(acc ^ 1);
            den = fna * den + fa * dd;
            num = fna * num + fa * numer;
        }
        transpose(&mut r);
    }

    // ---- edge axes: c = A_j × B_i (in A's frame)
    let mut c = [0f64; 3];
    for i in 0..3usize {
        let (ii, di) = (inc(i), dec(i));
        for j in 0..3usize {
            let (ij, dj) = (inc(j), dec(j));
            c[j] = 0.0;
            c[ij] = -r[3 * i + dj];
            c[dj] = r[3 * i + ij];
            let cl2 = (c[1] * c[1] + c[0] * c[0]) + c[2] * c[2];
            if lt_or_nan_d(cl2, 1e-8) {
                continue;
            }
            if zero {
                let sg = f64::from(nsgn(((c[1] * cc[1] + c[0] * cc[0]) + c[2] * cc[2]) as f32));
                d[1] = c[1] * sg;
                d[2] = c[2] * sg;
                d[0] = sg * c[0];
            }
            let dotd = (c[1] * d[1] + c[0] * d[0]) + c[2] * d[2];
            let v145 = psgn(dotd as f32);
            let v150 = -(psgn(c[ij] as f32) * v145);
            let v152 = -(psgn(c[dj] as f32) * v145);
            let v151 = dotd * f64::from(v145);
            let v154 =
                psgn(((c[1] * r[3 * ii + 1] + c[0] * r[3 * ii]) + c[2] * r[3 * ii + 2]) as f32)
                    * v145;
            let v156 =
                psgn(((c[1] * r[3 * di + 1] + c[0] * r[3 * di]) + c[2] * r[3 * di + 2]) as f32)
                    * v145;
            let mut pp = [0f64; 3];
            pp[j] = -s[j];
            pp[ij] = f64::from(v150) * s[ij];
            pp[dj] = f64::from(v152) * s[dj];
            let v158 = f64::from(v156) * s[3 + di];
            let v159 = f64::from(v154) * s[3 + ii];
            let sbi = s[3 + i];
            let term = |k: usize| {
                (((cc[k] - r[3 * i + k] * sbi) + r[3 * ii + k] * v159) + r[3 * di + k] * v158)
                    - pp[k]
            };
            let v162 = ((term(1) * c[1] + term(0) * c[0]) + term(2) * c[2]) * f64::from(v145);
            let acc = sbit((-v162) as f32) & sbit((den * v162 - v151 * num) as f32);
            let cv = (v152
                + 16 * (v156 + ((v154 + 1) >> 1))
                + ((v150 + 1) >> 1)
                + (0x100 + 64 * i as i32 + 4 * j as i32)
                + 0x11)
                & -acc;
            code = cv | ((acc - 1) & code);
            let fa = f64::from(acc);
            let fna = f64::from(acc ^ 1);
            den = fna * den + fa * v151;
            num = fna * num + fa * v162;
        }
    }

    if code == -1 {
        return 0;
    }

    if code & 0x100 != 0 {
        // ---- edge–edge
        let v171 = 2 * (code & 1) - 1;
        let i = ((code >> 6) & 3) as usize;
        let j = ((code >> 2) & 3) as usize;
        let (ii, di) = (inc(i), dec(i));
        let (ij, dj) = (inc(j), dec(j));
        let v178 = (code & 2) - 1;
        let v179 = ((code >> 4) & 2) - 1;
        let v180 = ((code >> 3) & 2) - 1;
        c[j] = 0.0;
        c[ij] = -r[3 * i + dj];
        c[dj] = r[3 * i + ij];
        let cl2 = (c[1] * c[1] + c[0] * c[0]) + c[2] * c[2];
        let (d0, d1, d2);
        if zero {
            let sg = f64::from(nsgn(((c[1] * cc[1] + c[0] * cc[0]) + c[2] * cc[2]) as f32));
            d2 = sg * c[2];
            d1 = sg * c[1];
            d0 = sg * c[0];
            d[2] = d2;
            d[0] = d0;
            d[1] = d1;
            let m = |k: usize| f64::from(am[k]);
            // Bug kept: the written-back direction is ±c, not normalised.
            let wx = ((m(3) * d1 + m(0) * d0) + m(6) * d2) as f32;
            let wy = ((m(4) * d1 + m(1) * d0) + m(7) * d2) as f32;
            let wz = ((m(2) * d0 + m(5) * d1) + m(8) * d2) as f32;
            p.dir = Vec3::new(wx, wy, wz);
            let (nx, ny, nz) = (-wx, -wy, -wz);
            d[3] = f64::from((nx * bm[0] + ny * bm[1]) + nz * bm[2]);
            d[4] = f64::from((nx * bm[3] + ny * bm[4]) + nz * bm[5]);
            d[5] = f64::from((nx * bm[6] + ny * bm[7]) + nz * bm[8]);
        } else {
            d0 = d[0];
            d1 = d[1];
            d2 = d[2];
        }
        let mut pp = [0f64; 3];
        pp[j] = -s[j];
        pp[ij] = f64::from(v171) * s[ij];
        pp[dj] = f64::from(v178) * s[dj];
        let sbi = s[3 + i];
        let sbd = s[3 + di];
        let sbn = s[3 + ii];
        let f179 = f64::from(v179);
        let f180 = f64::from(v180);
        let w = |k: usize| {
            (((cc[k] - r[3 * i + k] * sbi) + (r[3 * ii + k] * sbn) * f180)
                + (r[3 * di + k] * sbd) * f179)
                - pp[k]
        };
        let w0 = w(0) * den - d0 * num;
        let w1 = w(1) * den - d1 * num;
        let w2 = w(2) * den - d2 * num;
        let (bi0, bi1, bi2) = (r[3 * i], r[3 * i + 1], r[3 * i + 2]);
        let x = w1 * bi2 - w2 * bi1;
        let y = w2 * bi0 - w0 * bi2;
        let z = w0 * bi1 - w1 * bi0;
        let inv = 1.0 / (cl2 * den);
        pp[j] += ((x * c[0] + y * c[1]) + z * c[2]) * inv;
        let t = (inv * num) * cl2;
        out.t = t;
        let tf = t as f32;
        let m = |k: usize| f64::from(am[k]);
        let ptx = (f64::from(ba.c[0]) + ((m(3) * pp[1] + m(0) * pp[0]) + m(6) * pp[2])) as f32;
        let pty = (f64::from(ba.c[1]) + ((m(4) * pp[1] + m(1) * pp[0]) + m(7) * pp[2])) as f32;
        let ptz = (f64::from(ba.c[2]) + ((m(5) * pp[1] + m(2) * pp[0]) + m(8) * pp[2])) as f32;
        out.pt = Vec3::new(ptx + p.dir.x * tf, pty + p.dir.y * tf, ptz + p.dir.z * tf);
        let sg = f64::from(psgn(((c[1] * cc[1] + c[0] * cc[0]) + c[2] * cc[2]) as f32));
        // Bug kept: n = ±c in world space, not normalised.
        let nx = (m(3) * c[1] + m(0) * c[0]) + m(6) * c[2];
        let ny = (m(4) * c[1] + m(1) * c[0]) + m(7) * c[2];
        let nz = (m(2) * c[0] + m(5) * c[1]) + m(8) * c[2];
        out.n = Vec3::new((nx * sg) as f32, (sg * ny) as f32, (sg * nz) as f32);
        out.i_feature[0] = ((v171 + 1) >> 1) | (((j as i32) | 8) << 2) | (v178 + 1);
        out.i_feature[1] = ((v180 + 1) >> 1) | (((i as i32) | 8) << 2) | (v179 + 1);
    } else {
        // ---- face–vertex
        let k = (code >> 5) as usize;
        let axis = ((code >> 3) & 3) as usize;
        let o = k ^ 1;
        if zero {
            d[3 * k] = 0.0;
            d[3 * k + 1] = 0.0;
            d[3 * k + 2] = 0.0;
            d[3 * k + axis] = f64::from(nsgn(cc[3 * k + axis] as f32));
            let sk = f64::from(1 - 2 * k as i32);
            let e0 = d[3 * k] * sk;
            let e1 = d[3 * k + 1] * sk;
            let e2 = d[3 * k + 2] * sk;
            let km = &bx[k].m;
            let m = |q: usize| f64::from(km[q]);
            let wx = ((m(3) * e1 + m(0) * e0) + m(6) * e2) as f32;
            let wy = ((m(1) * e0 + m(4) * e1) + m(7) * e2) as f32;
            let wz = ((m(2) * e0 + m(5) * e1) + m(8) * e2) as f32;
            p.dir = Vec3::new(wx, wy, wz);
            let s2 = (2 * k as i32 - 1) as f32;
            let (x, y, z) = (wx * s2, wy * s2, wz * s2);
            let om = &bx[o].m;
            d[3 * o] = f64::from((y * om[1] + x * om[0]) + z * om[2]);
            d[3 * o + 1] = f64::from((x * om[3] + y * om[4]) + z * om[5]);
            d[3 * o + 2] = f64::from((x * om[6] + y * om[7]) + z * om[8]);
        }
        let v251 = nsgn(d[3 * k + axis] as f32);
        let t = if ucomisd_eq(den, 0.0) { 0.0 } else { num / den };
        out.t = t;
        let ob = bx[o];
        let fv = v251 as f32;
        let vx = fv * ((2 * (code & 1) - 1) as f32 * ob.s[0]);
        let vy = ((code & 2) - 1) as f32 * ob.s[1] * fv;
        let vz = (((code >> 1) & 2) - 1) as f32 * ob.s[2] * fv;
        let om = &ob.m;
        let rx = (vy * om[3] + vx * om[0]) + vz * om[6];
        let ry = (vy * om[4] + vx * om[1]) + vz * om[7];
        let rz = (vy * om[5] + vx * om[2]) + vz * om[8];
        let tf = t as f32;
        let kf = k as f32;
        out.pt = Vec3::new(
            ((p.dir.x * tf) * kf + ob.c[0]) - rx,
            ((p.dir.y * tf) * kf + ob.c[1]) - ry,
            ((p.dir.z * tf) * kf + ob.c[2]) - rz,
        );
        let sn = ((1 - 2 * k as i32) * v251) as f32;
        let row = bx[k].row(axis);
        out.n = Vec3::new(row[0] * sn, row[1] * sn, row[2] * sn);
        // Bug kept: the face id is 0x40 | axis | side (axis not shifted).
        out.i_feature[k] = (((v251 + 1) | 0x80) >> 1) | axis as i32;
        out.i_feature[o] = (!(v251 >> 31) ^ code) & 7;
    }

    let Some(area) = area else { return 1 };
    let mut ar = AreaBuf::new(area);

    // ---- area 1: face–face (B face k against A face m)
    for k in 0..3usize {
        let a0 = r[3 * k].abs();
        let a1 = r[3 * k + 1].abs();
        let a2 = r[3 * k + 2].abs();
        let m1 = ((a0 - a1).abs() + (a0 + a1)) * 0.5;
        let mx = ((m1 - a2).abs() + (a2 + m1)) * 0.5;
        // comisd + jbe: skipped when <= or unordered
        if le_or_nan_d(mx, f64::from(p.max_cos)) {
            continue;
        }
        let abc = [a0, a1, a2];
        let sbk = bb.s[k];
        let e1 = sbit((a0 - a1) as f32);
        let e2 = sbit((abc[e1 as usize] - a2) as f32);
        let x = e1 | (e2 << 1);
        let m = ((((x ^ 2) | 4) >> 1) & x) as usize;
        let t = out.t;
        let pa = ((f64::from(ba.s[0]) * a0 + f64::from(sbk)) + f64::from(ba.s[1]) * a1)
            + f64::from(ba.s[2]) * a2;
        let v290 = ((cc[3 + k] - t * d[3 + k]).abs() - pa) as f32;
        let dmv = d[m];
        let sam = ba.s[m];
        let pb = ((r[m].abs() * f64::from(bb.s[0]) + f64::from(sam))
            + r[3 + m].abs() * f64::from(bb.s[1]))
            + r[6 + m].abs() * f64::from(bb.s[2]);
        let v291 = ((cc[m] - dmv * t).abs() - pb) as f32;
        let g1 = v291.abs();
        let g0 = v290.abs();
        let mn = ((g1 + g0) - (g1 - g0).abs()) * 0.5;
        if !lt_or_nan(mn, (sbk + sam) * 0.003) {
            continue;
        }
        if le_or_nan_d(dmv.abs(), f64::from(0.01f32)) {
            continue;
        }
        ar.a.i_type = 0;
        let bn = bb.row(k);
        let nd = (p.dir.x * bn[0] + p.dir.y * bn[1]) + p.dir.z * bn[2];
        let fs = psgn(nd) as f32;
        ar.a.n = Vec3::new(fs * bn[0], fs * bn[1], fs * bn[2]);
        let negd = (-dmv) as f32;
        let arow = ba.row(m);
        out.n = Vec3::new(arow[0] * negd, arow[1] * negd, arow[2] * negd);
        let (im, dm) = (inc(m), dec(m));
        let cx = (cc[im] - t * d[im]) as f32;
        let cy = (cc[dm] - t * d[dm]) as f32;
        let half = [ba.s[im], ba.s[dm]];
        let (ik, dk) = (inc(k), dec(k));
        let r7 = r[3 * ik + im] as f32;
        let r9 = r[3 * ik + dm] as f32;
        let r11 = r[3 * dk + im] as f32;
        let r13 = r[3 * dk + dm] as f32;
        let ebx3 = ((-psgn(nd)) >> 31) & 3;
        let v315 = ebx3 & 1;
        let bface = ((k as i32 | 0x20) * 2) | v315;
        let r15 = nsgn(dmv as f32);
        let mut plane = [0f32; 3];
        plane[m] = r15 as f32 * ba.s[m];
        let v545 = ((-r15) >> 31) & 1;
        let aface = ((m as i32 | 0x20) * 2) | v545;
        let faces = [aface, bface];
        let v319 = v315 << k;
        let sbd = bb.s[dk];
        let sbi = bb.s[ik];
        let mut pts1 = vec![0f32; 16];
        let mut ids1 = vec![0i32; 8];
        for vi in 0..4i32 {
            let next = (vi + 1) & 3;
            let v324 = ((((next ^ ebx3) >> 1) & 1) << dk) | ((((next >> 1) ^ next) & 1) << ik);
            let v325 = ((vi ^ ebx3) >> 1) & 1;
            let v326 = ((vi >> 1) ^ vi) & 1;
            let f327 = (2 * v325 - 1) as f32 * sbd;
            let v329 = (v325 << dk) | (v326 << ik) | v319;
            let xx = (v319 | v324) ^ v329;
            let e = ((((3 - xx) as u32) >> 31) + (((1 - xx) as u32) >> 31)) as usize;
            let f332 = (2 * v326 - 1) as f32 * sbi;
            let py = ((r9 * f332) + cy) + r13 * f327;
            let px = ((f332 * r7) + cx) + f327 * r11;
            let vu = vi as usize;
            pts1[2 * vu] = px;
            pts1[2 * vu + 1] = py;
            ids1[vu] =
                (((((e as i32) | 4) * 2) | ((v329 >> dec(e)) & 1)) * 2) | ((v329 >> inc(e)) & 1);
        }
        let ea = ((dm as i32) * 4) | v545;
        let eb = (((im as i32) * 2) | v545) * 2;
        let mut pts2 = Vec::new();
        let mut ids2 = Vec::new();
        let n1 = clip_polygon_line(&pts1, &ids1, 4, half, 0, ea, &mut pts2, &mut ids2);
        ar.a.npt = n1;
        let n2 = clip_polygon_line(&pts2, &ids2, n1, half, 1, eb, &mut pts1, &mut ids1);
        ar.a.npt = n2;
        let n3 = clip_polygon_line(&pts1, &ids1, n2, half, 2, ea | 2, &mut pts2, &mut ids2);
        ar.a.npt = n3;
        let n4 = clip_polygon_line(&pts2, &ids2, n3, half, 3, eb | 1, &mut pts1, &mut ids1);
        ar.a.npt = n4;
        let mut pi: i32 = 0;
        while pi < ar.a.npt {
            let tf = out.t as f32;
            let prev = (((pi - 1) >> 31) & (ar.a.npt - 1)) | (((-pi) >> 31) & (pi - 1));
            let pu = pi as usize;
            plane[im] = pts1[2 * pu];
            plane[dm] = pts1[2 * pu + 1];
            let wx = (((plane[0] * am[0] + plane[1] * am[3]) + plane[2] * am[6]) + ba.c[0])
                + p.dir.x * tf;
            let wy = (((plane[0] * am[1] + plane[1] * am[4]) + plane[2] * am[7]) + ba.c[1])
                + p.dir.y * tf;
            let wz = (((plane[0] * am[2] + plane[1] * am[5]) + plane[2] * am[8]) + ba.c[2])
                + p.dir.z * tf;
            ar.set_pt(pu, Vec3::new(wx, wy, wz));
            ar.set_prim(1, pu, 0);
            ar.set_prim(0, pu, 0);
            let g = ids1[pu];
            let h = ids1[prev as usize];
            if (g ^ h) & !0xF != 0 {
                ar.set_feat((h >> 4) as usize, pu, (h & 0xF) | 0x20);
                ar.set_feat((g >> 4) as usize, pu, (g & 0xF) | 0x20);
            } else {
                let hl = h & 0xF;
                let gl = g & 0xF;
                let e1 = (hl >> 2) as usize;
                let dd = e1 as i32 - DEC_MOD3[(gl >> 2) as usize];
                let sh = -(((dd - 1) ^ dd) >> 31);
                let vtx = (((gl >> sh) & 1) << e1)
                    | (((hl >> 1) & 1) << DEC_MOD3[e1])
                    | ((hl & 1) << INC_MOD3[e1]);
                ar.set_feat((g >> 4) as usize, pu, vtx);
                let other = ((g ^ 0x10) >> 4) as usize;
                ar.set_feat(other, pu, faces[other]);
            }
            pi += 1;
        }
        if ar.a.npt != 0 {
            return finish(out, &ar);
        }
    }

    // ---- area 2: edge–face
    for idx in 0..9usize {
        let mut i = idx / 3;
        let mut j = idx - 3 * i;
        let thr = f64::from((1.0f32 - p.max_cos) * 2.0f32);
        let rv = r[3 * i + j];
        if !lt_or_nan_d(rv * rv, thr) {
            continue;
        }
        for sb in 0..2usize {
            let bs = bx[sb];
            let ob = sb ^ 1;
            let bo = bx[ob];
            let (ii, di) = (inc(i), dec(i));
            let t = out.t;
            let sj = bs.s[j];
            // Bug kept: B's size is used here for both orientations.
            let thr_s = bo.s[di] + bb.s[ii];
            let r_inc = r[3 * ii + j];
            let r_dec = r[3 * di + j];
            let gap = (cc[3 * sb + j] - t * d[3 * sb + j]).abs();
            let proj = (r_inc.abs() * f64::from(bo.s[ii]) + f64::from(sj))
                + r_dec.abs() * f64::from(bo.s[di]);
            let lim = ((sj + thr_s) - (sj - thr_s).abs()) * 0.5 * 0.05;
            if lt_or_nan_d((gap - proj).abs(), f64::from(lim)) {
                let cp = [
                    (cc[3 * sb] - d[3 * sb] * t) as f32,
                    (cc[3 * sb + 1] - d[3 * sb + 1] * t) as f32,
                    (cc[3 * sb + 2] - d[3 * sb + 2] * t) as f32,
                ];
                ar.a.i_type = 1;
                let (ij, dj) = (inc(j), dec(j));
                let v523 = psgn(cp[j]);
                let v395 = -(psgn(r_inc as f32) * v523);
                let v397 = -(psgn(r_dec as f32) * v523);
                let f395 = v395 as f32;
                let f397 = v397 as f32;
                let so_d = bo.s[di];
                let so_i = bo.s[ii];
                let x2 = so_d * ((r[3 * di + ij] as f32) * f397);
                let x3 = ((r[3 * di + dj] as f32) * f397) * so_d;
                let x6 = ((r[3 * ii + dj] as f32) * f395) * so_i;
                let x0 = so_i * ((r[3 * ii + ij] as f32) * f395);
                let y0 = cp[dj] + (x6 + x3);
                let xa = cp[ij] + (x0 + x2);
                let so = bo.s[i];
                let e2 = (r[3 * i + ij] as f32) * so;
                let e3 = (r[3 * i + dj] as f32) * so;
                let mut seg = [xa - e2, y0 - e3, e2 + xa, e3 + y0];
                let v400 = (v523 + 1) >> 1;
                let fid = ((j as i32 | 0x20) * 2) | v400;
                ar.set_feat(sb, 1, fid);
                ar.set_feat(sb, 0, fid);
                let v404 = v397 + 1;
                let v405 = (v395 + 1) >> 1;
                let f0 = ((v404 >> 1) << di) | (v405 << ii);
                ar.set_feat(ob, 0, f0);
                ar.set_feat(ob, 1, (1 << i) | f0);
                let id1 = ((i as i32 | 8) << 2) | v405 | v404;
                {
                    let [fa0, fa1] = &mut ar.a.feature;
                    let (ids0, ids1) = if sb == 0 { (fa0, fa1) } else { (fa1, fa0) };
                    clip_segment_slab(
                        &mut seg,
                        bs.s[ij],
                        0,
                        ids0,
                        ids1,
                        ((dj as i32 | 8) << 2) | v400,
                        id1,
                    );
                    clip_segment_slab(
                        &mut seg,
                        bs.s[dj],
                        1,
                        ids0,
                        ids1,
                        ((ij as i32 | 8) << 2) | (v523 + 1),
                        id1,
                    );
                }
                let ddx = seg[0] - seg[2];
                let ddy = seg[1] - seg[3];
                if (ddx * ddx + ddy * ddy) > p.min_pt_dist * p.min_pt_dist {
                    let v413 = v523 as f32;
                    let mut pv = [0f32; 3];
                    pv[j] = v413 * bs.s[j];
                    pv[ij] = seg[0];
                    pv[dj] = seg[1];
                    let tf = (f64::from((sb ^ 1) as i32) * out.t) as f32;
                    let bmx = &bs.m;
                    let world = |v: &[f32; 3]| {
                        Vec3::new(
                            (((v[1] * bmx[3] + v[0] * bmx[0]) + v[2] * bmx[6]) + bs.c[0])
                                + p.dir.x * tf,
                            (((v[1] * bmx[4] + v[0] * bmx[1]) + v[2] * bmx[7]) + bs.c[1])
                                + p.dir.y * tf,
                            (((v[0] * bmx[2] + v[1] * bmx[5]) + v[2] * bmx[8]) + bs.c[2])
                                + p.dir.z * tf,
                        )
                    };
                    let w0 = world(&pv);
                    ar.set_pt(0, w0);
                    ar.set_prim(1, 0, 0);
                    ar.set_prim(0, 0, 0);
                    pv[ij] = seg[2];
                    pv[dj] = seg[3];
                    let w1 = world(&pv);
                    ar.set_pt(1, w1);
                    ar.set_prim(1, 1, 0);
                    ar.set_prim(0, 1, 0);
                    ar.a.npt = 2;
                    if sb != 0 {
                        // project the points onto A's edge line
                        let f438 = f397 * ba.s[di];
                        let f439 = f395 * ba.s[ii];
                        let rd = ba.row(di);
                        let ri = ba.row(ii);
                        let bxv = (ri[0] * f439 + ba.c[0]) + rd[0] * f438;
                        let byv = (ri[1] * f439 + ba.c[1]) + rd[1] * f438;
                        let bzv = (ri[2] * f439 + ba.c[2]) + rd[2] * f438;
                        let tf2 = out.t as f32;
                        let base = [
                            f64::from(p.dir.x * tf2) + f64::from(bxv),
                            f64::from(p.dir.y * tf2) + f64::from(byv),
                            f64::from(p.dir.z * tf2) + f64::from(bzv),
                        ];
                        let ax = ba.row(i).map(f64::from);
                        for q in 0..2usize {
                            let pt = ar.a.pt[q];
                            let dxf = (f64::from(pt.x) - base[0]) as f32;
                            let dyf = (f64::from(pt.y) - base[1]) as f32;
                            let dzf = (f64::from(pt.z) - base[2]) as f32;
                            let dot = (ax[1] * f64::from(dyf) + f64::from(dxf) * ax[0])
                                + ax[2] * f64::from(dzf);
                            ar.a.pt[q] = Vec3::new(
                                (dot * ax[0] + base[0]) as f32,
                                (ax[1] * dot + base[1]) as f32,
                                (ax[2] * dot + base[2]) as f32,
                            );
                        }
                    }
                    let mut nn = [0f32; 6];
                    let fr = bs.row(j);
                    nn[3 * sb] = v413 * fr[0];
                    nn[3 * sb + 1] = v413 * fr[1];
                    nn[3 * sb + 2] = v413 * fr[2];
                    let er = bo.row(i);
                    let (ex, ey, ez) = (er[0], er[1], er[2]);
                    let (fx, fy, fz) = (nn[3 * sb], nn[3 * sb + 1], nn[3 * sb + 2]);
                    let gx = ey * fz - ez * fy;
                    let gy = ez * fx - ex * fz;
                    let gz = ex * fy - ey * fx;
                    let hz = gy * ex - gx * ey;
                    let hy = gx * ez - gz * ex;
                    let hx = gz * ey - gy * ez;
                    let hl2 = (hx * hx + hy * hy) + hz * hz;
                    let nv = if hl2 > 0.001 {
                        if hl2 > 1e-20 {
                            let inv = 1.0f32 / hl2.sqrt();
                            [inv * hx, inv * hy, inv * hz]
                        } else {
                            [0.0, 0.0, 1.0]
                        }
                    } else {
                        [-fx, -fy, -fz]
                    };
                    nn[3 * ob] = nv[0];
                    nn[3 * ob + 1] = nv[1];
                    nn[3 * ob + 2] = nv[2];
                    out.n = Vec3::new(nn[0], nn[1], nn[2]);
                    ar.a.n = Vec3::new(nn[3], nn[4], nn[5]);
                }
                if ar.a.npt != 0 {
                    return finish(out, &ar);
                }
            }
            transpose(&mut r);
            std::mem::swap(&mut i, &mut j);
        }
    }
    if ar.a.npt != 0 {
        return finish(out, &ar);
    }
    1
}

/// `Aᵀ` of a row-major 3×3 float matrix (the binary builds it on the stack for sub_35022380).
fn b_transpose(m: &[f32; 9]) -> [f32; 9] {
    [m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]]
}

/// The common exit when the area has points: `out.pt = area.pt[0]`, features = entry 0.
fn finish(out: &mut UnprojResult, ar: &AreaBuf) -> i32 {
    out.pt = ar.a.pt.first().copied().unwrap_or(Vec3::ZERO);
    out.i_feature[0] = ar.feat(0, 0);
    out.i_feature[1] = ar.feat(1, 0);
    1
}

fn cell_box_box(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    area: Option<&mut ContactArea>,
) -> i32 {
    unproject_box_box(p, as_box(a), fa, as_box(b), fb, out, area)
}

/// Fill the cells this group owns: `[0][box][box]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(MODE_LINEAR, GeomType::Box, GeomType::Box, cell_box_box);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn bx(c: Vec3, s: Vec3) -> BoxPrim {
        BoxPrim::aligned(c, s)
    }

    #[test]
    fn min_distance_face() {
        // unit boxes overlapping by 0.25 along x
        let mut p = UnprojParams {
            tmax: 1e10,
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        let a = bx(Vec3::ZERO, Vec3::splat(1.0));
        let b = bx(Vec3::new(1.75, 0.0, 0.0), Vec3::splat(1.0));
        assert_eq!(unproject_box_box(&mut p, &a, -1, &b, -1, &mut out, None), 1);
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
        assert_eq!(out.t, 0.25);
    }

    #[test]
    fn directed_and_separated() {
        let a = bx(Vec3::ZERO, Vec3::splat(1.0));
        let b = bx(Vec3::new(1.5, 0.0, 0.0), Vec3::splat(1.0));
        let mut p = UnprojParams {
            dir: Vec3::new(-1.0, 0.0, 0.0),
            tmax: 10.0,
            ..UnprojParams::default()
        };
        let mut out = UnprojResult::default();
        assert_eq!(unproject_box_box(&mut p, &a, -1, &b, -1, &mut out, None), 1);
        assert_eq!(out.t, 0.5);
        // already separated: no positive t
        let b = bx(Vec3::new(3.0, 0.0, 0.0), Vec3::splat(1.0));
        let mut out = UnprojResult::default();
        assert_eq!(unproject_box_box(&mut p, &a, -1, &b, -1, &mut out, None), 0);
    }

    #[test]
    fn clip_square_by_line() {
        // square [-2,2]², clip by x > -1
        let src = [-2.0f32, -2.0, 2.0, -2.0, 2.0, 2.0, -2.0, 2.0];
        let ids = [10, 11, 12, 13];
        let (mut dst, mut dids) = (Vec::new(), Vec::new());
        let n = clip_polygon_line(&src, &ids, 4, [1.0, 1.0], 0, 99, &mut dst, &mut dids);
        assert_eq!(n, 4);
        assert_eq!(&dst[..8], &[2.0, 2.0, -1.0, 2.0, -1.0, -2.0, 2.0, -2.0]);
        assert_eq!(&dids[..4], &[12, 99, 10, 11]);
    }
}
