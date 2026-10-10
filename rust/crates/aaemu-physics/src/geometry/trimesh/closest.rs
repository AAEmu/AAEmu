//! Slot 11 `FindClosestPoint(gwd, iPrim&, iFeature&, ptdst0, ptdst1, ptres[2], nMaxIters)` of
//! `CTriMesh` (0x351C6780, shared by `CVoxelGeom`) and of `CHeightfield` (0x35155CB0), plus
//! the heightfield's empty slot 12 (0x35154CB0).
//!
//! **Mesh semantics** (`mesh_find_closest_point`). A local descent over the mesh topology, not
//! a global search: it starts at triangle `iPrim` and feature `iFeature` (`0x40` the face,
//! `0x20 | e` edge `e` = vertices `e → inc(e)`, `0x00 | v` vertex `v`) and moves to
//! neighbouring features until the current one is a local optimum for the query, which is
//! the segment `ptdst0..ptdst1` (the "segment mode", when `|ptdst1 − ptdst0|² > 1e-4`, sign of
//! `1e-4 − |Δ|²`) or the point `ptdst0`. All in the world frame (vertices `(R·v)·s + offset`,
//! normals `R·n`). On return `ptres[0]` is the point on the mesh, `ptres[1]` the point of the
//! query, `iPrim`/`iFeature` the final feature, and the result is the number of iterations
//! left (`nMaxIters` minus the moves; see below). Every value is single precision.
//!
//! Points are carried in homogeneous form with a common denominator `den` (starts at 1); the
//! final step (`sub_351DB3A0`) divides both points by `den` unless `den == 1` (or NaN) or
//! `|den| <= 1e-20`.
//!
//! The function is ported statement by statement (see the step functions below); the
//! original bugs it keeps are documented where they happen:
//! * the face step's open-edge exit mixes a scaled (homogeneous) triangle with the unscaled
//!   `ptdst0` and replaces the denominator instead of multiplying it;
//! * the vertex step compares `|p − V|⁴·|Δ|²` (distance squared, squared) against
//!   `dist²(V, line)·|Δ|²` and does not clamp the projection to the segment;
//! * the face step treats the triangle as two-sided (it projects onto the plane whatever the
//!   side), so the descent can stop on a back face.
//!
//! **Heightfield semantics** (`heightfield_find_closest_point`): only `ptdst0` is used; the
//! closest point among the two triangles, three edges and one vertex of the grid cell under
//! the point, each candidate accepted only if strictly better and (for triangles) seen from
//! above; see the function.

use super::fluid::{MeshView, DEC_MOD3, INC_MOD3};
use super::TriMesh;
use crate::geometry::primitives::HeightfieldPrim;
use crate::geometry::GeomWorldData;
use crate::math::{round_magic, Vec3};

/// `1e10` (0x501502F9), `ptres[0]` before anything is found.
const FAR: f32 = f32::from_bits(0x5015_02F9);
/// `1e-4` (0x38D1B717): segment-mode threshold on `|Δ|²` and the face step's minimum `|n·Δ|`.
const EPS4: f32 = f32::from_bits(0x38D1_B717);
/// `1e-6` (0x358637BD): the edge step's "segment parallel to the edge" threshold on `|Δ×u|²`.
const EPS6: f32 = f32::from_bits(0x3586_37BD);
/// `1e-7` (0x33D6BF95): bias of the vertex step's convexity test.
const EPS7: f32 = f32::from_bits(0x33D6_BF95);
/// `1e-20` (0x1E3CE508): the finalisation guard.
const EPS20: f32 = f32::from_bits(0x1E3C_E508);

/// Sign bit of a float as 0/1.
#[inline]
fn sb(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// `x ^ (x − 1)` has its sign bit set iff `x == 0` (or `x == i32::MIN`); the binary's
/// "equals" idiom, as 0/1.
#[inline]
fn is_zero_idiom(x: i32) -> i32 {
    (x ^ x.wrapping_sub(1)) >> 31 & 1
}

/// The edge of triangle `t` (in `topology`) that leads back to triangle `from`: 2 if
/// `topo[t][2] == from`, else 1 if `topo[t][1] == from`, else 0 (the binary's
/// `((z2)&2) | (z1 & 1 & ~z2)`).
#[inline]
fn back_edge(topo: &[[i32; 3]], t: usize, from: i32) -> usize {
    let z1 = is_zero_idiom(topo[t][1].wrapping_sub(from));
    let z2 = is_zero_idiom(topo[t][2].wrapping_sub(from));
    (((z2 << 1) & 2) | (z1 & 1 & (z2 ^ 1))) as usize
}

/// `max(x, 0)` as `(|−x| + x)·0.5`.
#[inline]
fn pos_part(x: f32) -> f32 {
    ((-x).abs() + x) * 0.5
}

/// `min(a, b)` as `((a + b) − |b − a|)·0.5`.
#[inline]
fn min_ab(a: f32, b: f32) -> f32 {
    ((a + b) - (b - a).abs()) * 0.5
}

/// The 0/1/2 selection of the edge and vertex steps between `ptdst0` (0), `ptdst1` (1) and the
/// interior point (2): `s = isneg(h1 − h0)`, `k = 2·isneg(hm − h_s) | s`,
/// `k & (((k ^ 2) | 4) >> 1)`.
#[inline]
fn select3(h0: f32, h1: f32, hm: f32) -> usize {
    let s = sb(h1 - h0);
    let hs = if s != 0 { h1 } else { h0 };
    let k = (2 * sb(hm - hs)) | s;
    (k & (((k ^ 2) | 4) >> 1)) as usize
}

/// The mesh state the descent reads.
struct Ctx<'a> {
    m: MeshView<'a>,
    topo: &'a [[i32; 3]],
    gwd: &'a GeomWorldData,
}

impl Ctx<'_> {
    /// World vertex `j` of triangle `t`: `(R·v)·s + offset`.
    #[inline]
    fn vtx(&self, t: usize, j: usize) -> Vec3 {
        self.gwd.r.mul_vec(self.m.vtx(t, j)) * self.gwd.scale + self.gwd.offset
    }

    /// World normal of triangle `t`: `R·n`.
    #[inline]
    fn nrm(&self, t: usize) -> Vec3 {
        self.gwd.r.mul_vec(self.m.normals[t])
    }
}

/// The query: end points, `Δ`, `|Δ|²` and the segment-mode flag.
struct Query {
    p0: Vec3,
    p1: Vec3,
    d: Vec3,
    dd: f32,
    seg: bool,
}

/// What a step decided.
enum Step {
    /// Local optimum: finalise and return the iteration count.
    Done,
    /// `iPrim` / `iFeature` were updated: record and continue.
    Move,
}

/// The mutable outputs.
struct Out<'a> {
    i_prim: &'a mut i32,
    i_feature: &'a mut i32,
    ptres: &'a mut [Vec3; 2],
    den: f32,
}

/// `ptres[k] *= f` per component.
#[inline]
fn scale(p: &mut Vec3, f: f32) {
    *p = Vec3::new(f * p.x, f * p.y, f * p.z);
}

/// The triple product `((V − U) × (P − U))·n` with the binary's operand order (face step).
#[inline]
fn edge_side(u: Vec3, v: Vec3, p: Vec3, n: Vec3) -> f32 {
    ((((v.y - u.y) * (p.z - u.z)) - ((v.z - u.z) * (p.y - u.y))) * n.x
        + (((p.x - u.x) * (v.z - u.z)) - ((v.x - u.x) * (p.z - u.z))) * n.y)
        + (((v.x - u.x) * (p.y - u.y)) - ((p.x - u.x) * (v.y - u.y))) * n.z
}

/// Face step (`iFeature == 0x40`).
///
/// Triangle `A, B, C`, normal `n`. In segment mode with the end points on strictly opposite
/// sides (`!(d1·d0 >= 0)`, NaN included) and `|n·Δ| > 1e-4`, the query point is the plane
/// crossing in homogeneous form, `sgn(n·Δ)·(p0·(n·Δ) + Δ·(n·(A − p0)))` over `|n·Δ|`, and the
/// triangle is scaled by `|n·Δ|`; otherwise the end point with the smaller `n·(p − A)`
/// (`p1` when `d1 − d0` is negative) — or `p0` in point mode — with denominator 1. The point
/// `P` is projected onto the plane (`ptres[0]`); if it is inside all three edges (sign bits of
/// the triple products clear) the step is done. Otherwise the first edge it is outside of is
/// crossed into the neighbour (`iFeature = 0x20 | back edge`); an open edge ends the descent
/// with the point of the edge closest to `ptdst0`: `E0·|u|² + u·clamp(u·(p0 − E0), 0, |u|²)`,
/// `ptres[1] *= |u|²`, `den = |u|²`, `iFeature = 0x20 | e`. **Original bug kept**: after a
/// plane crossing the edge is in scaled units while `p0` is not, and `den` is replaced, not
/// multiplied, so `ptres[1]` keeps a stray factor `|n·Δ|`.
fn face_step(c: &Ctx, q: &Query, o: &mut Out) -> Step {
    let prim = *o.i_prim as usize;
    let a = c.vtx(prim, 0);
    let mut verts = [a, c.vtx(prim, 1), c.vtx(prim, 2)];
    let n = c.nrm(prim);
    let mut crossed = false;
    if q.seg {
        let (p0, p1, d) = (q.p0, q.p1, q.d);
        let d0 = ((p0.x - a.x) * n.x + (p0.y - a.y) * n.y) + (p0.z - a.z) * n.z;
        let d1 = ((p1.x - a.x) * n.x + (p1.y - a.y) * n.y) + (p1.z - a.z) * n.z;
        if !(d1 * d0 >= 0.0) {
            let dn = (n.y * d.y + n.x * d.x) + n.z * d.z;
            let adn = dn.abs();
            if adn > EPS4 {
                crossed = true;
                o.den = adn;
                let sgn = if sb(dn) != 0 { -1.0f32 } else { 1.0 };
                let t = ((a.x - p0.x) * n.x + (a.y - p0.y) * n.y) + (a.z - p0.z) * n.z;
                o.ptres[1] = Vec3::new(
                    (p0.x * dn + t * d.x) * sgn,
                    sgn * (p0.y * dn + t * d.y),
                    sgn * (p0.z * dn + t * d.z),
                );
                for v in &mut verts {
                    *v = *v * adn;
                }
            }
        }
        if !crossed {
            o.ptres[1] = if sb(d1 - d0) != 0 { p1 } else { p0 };
            o.den = 1.0;
        }
    } else {
        o.ptres[1] = q.p0;
        o.den = 1.0;
    }
    let p = o.ptres[1];
    let [a, b, cc] = verts;
    let s0 = sb(edge_side(a, b, p, n));
    let s1 = sb(edge_side(b, cc, p, n));
    let s2 = sb(edge_side(cc, a, p, n));
    let dist = ((p.x - a.x) * n.x + (p.y - a.y) * n.y) + (p.z - a.z) * n.z;
    o.ptres[0] = Vec3::new(p.x - dist * n.x, p.y - dist * n.y, p.z - dist * n.z);
    if s0 + s1 + s2 == 0 {
        return Step::Done;
    }
    let e = ((s0 ^ 1) + ((s0 | s1) ^ 1)) as usize;
    let nb = c.topo[prim][e];
    if nb < 0 {
        let e0 = verts[e];
        let e1 = verts[INC_MOD3[e]];
        let u = Vec3::new(e1.x - e0.x, e1.y - e0.y, e1.z - e0.z);
        let uu = (u.x * u.x + u.y * u.y) + u.z * u.z;
        let p0 = q.p0;
        let t = ((p0.x - e0.x) * u.x + (p0.y - e0.y) * u.y) + (p0.z - e0.z) * u.z;
        let t = min_ab(pos_part(t), uu);
        o.ptres[0] = Vec3::new(e0.x * uu + u.x * t, e0.y * uu + u.y * t, e0.z * uu + u.z * t);
        scale(&mut o.ptres[1], uu);
        o.den = uu;
        *o.i_feature = e as i32 | 0x20;
        return Step::Done;
    }
    *o.i_feature = back_edge(c.topo, nb as usize, prim as i32) as i32 | 0x20;
    *o.i_prim = nb;
    Step::Move
}

/// Edge step (`iFeature == 0x20 | e`).
///
/// Edge `E0 = v_e → E1 = v_inc(e)` of triangle `t`, `u = E1 − E0`. Segment mode: with
/// `c = Δ × u`, the segment parameter is `clamp(((E0 − p0) × u)·c, 0, |c|²)` over `|c|²` (or
/// over 1 when `|c|² <= 1e-6`); the query point is `p0`, `p1` or that point, chosen by
/// [`select3`] on `|(p − E0) × u|²·den` of the end points and `|u|²·((p0 − E0)·c)²`; the edge
/// is scaled by the chosen denominator. Point mode: `p0`, denominator 1.
///
/// With a neighbour `t'` across the edge (normals `n`, `n'`, `w = P − E0`): the closest edge
/// point is `E0·|u|² + u·(u·w)` (`ptres[1]` and `den` multiplied by `|u|²`); the step is done
/// when `(n × u)·w < 0`, `(n' × u)·w >= 0`, `u·(P − E1) < 0` and `u·w >= 0` (sign bits). Else
/// if `(n × u)·w < 0` and `(n' × u)·w >= 0` the descent moves to the vertex `E1` (when
/// `u·(P − E1) >= 0`) or `E0`; otherwise to the face of `t'` (when `(n' × u)·w < 0`) or of
/// `t`. An open edge moves to the face of `t` without computing a point.
fn edge_step(c: &Ctx, q: &Query, o: &mut Out) -> Step {
    let prim = *o.i_prim as usize;
    let e = (*o.i_feature & 0x1F) as usize;
    let n = c.nrm(prim);
    let e0 = c.vtx(prim, e);
    let e1 = c.vtx(prim, INC_MOD3[e]);
    let mut u = Vec3::new(e1.x - e0.x, e1.y - e0.y, e1.z - e0.z);
    let (mut q0, mut q1) = (e0, e1);
    if q.seg {
        let (p0, p1, d) = (q.p0, q.p1, q.d);
        let cx = u.z * d.y - u.y * d.z;
        let cy = d.z * u.x - u.z * d.x;
        let cz = u.y * d.x - d.y * u.x;
        let w0 = Vec3::new(e0.x - p0.x, e0.y - p0.y, e0.z - p0.z);
        let tn = ((w0.y * u.z - w0.z * u.y) * cx + (w0.z * u.x - u.z * w0.x) * cy)
            + (u.y * w0.x - w0.y * u.x) * cz;
        let cc = (cx * cx + cy * cy) + cz * cz;
        let s = sb(EPS6 - cc);
        let den = (s as f32) * cc + ((s ^ 1) as f32);
        let t = min_ab(pos_part(tn), den);
        let segpt = Vec3::new(p0.x * den + d.x * t, p0.y * den + d.y * t, p0.z * den + d.z * t);
        let line_d2 = |p: Vec3| {
            let (rx, ry, rz) = (p.x - e0.x, p.y - e0.y, p.z - e0.z);
            let g0 = ry * u.z - rz * u.y;
            let g1 = rz * u.x - u.z * rx;
            let g2 = u.y * rx - ry * u.x;
            ((g0 * g0 + g1 * g1) + g2 * g2) * den
        };
        let h0 = line_d2(p0);
        let h1 = line_d2(p1);
        let k = ((p0.x - e0.x) * cx + (p0.y - e0.y) * cy) + (p0.z - e0.z) * cz;
        let hm = ((u.x * u.x + u.y * u.y) + u.z * u.z) * (k * k);
        let sel = select3(h0, h1, hm);
        o.ptres[1] = [p0, p1, segpt][sel];
        let de = ((sel >> 1) as f32) * den + (((sel ^ 2) >> 1) as f32);
        o.den = de;
        q0 = q0 * de;
        q1 = q1 * de;
        u = u * de;
    } else {
        o.ptres[1] = q.p0;
        o.den = 1.0;
    }
    let nb = c.topo[prim][e];
    if nb >= 0 {
        let n2 = c.nrm(nb as usize);
        let p = o.ptres[1];
        let w = Vec3::new(p.x - q0.x, p.y - q0.y, p.z - q0.z);
        let side = |m: Vec3| {
            ((m.y * u.z - m.z * u.y) * w.x + (u.x * m.z - u.z * m.x) * w.y)
                + (u.y * m.x - u.x * m.y) * w.z
        };
        let s_a = sb(side(n)) ^ 1;
        let s_b = sb(side(n2));
        let t = (u.x * w.x + w.y * u.y) + w.z * u.z;
        let t2 = (u.x * (p.x - q1.x) + (p.y - q1.y) * u.y) + (p.z - q1.z) * u.z;
        let s_t2 = sb(t2) ^ 1;
        let done = s_a + s_b + s_t2 + sb(t) == 0;
        let uu = (u.x * u.x + u.y * u.y) + u.z * u.z;
        o.ptres[0] = Vec3::new(q0.x * uu + u.x * t, q0.y * uu + u.y * t, q0.z * uu + u.z * t);
        scale(&mut o.ptres[1], uu);
        o.den *= uu;
        if done {
            return Step::Done;
        }
        if s_b + s_a == 0 {
            *o.i_feature = if s_t2 != 0 { INC_MOD3[e] } else { e } as i32;
            return Step::Move;
        }
        if s_b != 0 {
            *o.i_prim = nb;
        }
    }
    *o.i_feature = 0x40;
    Step::Move
}

/// Vertex step (`iFeature == v`).
///
/// Vertex `V`. Segment mode: the query point is `p0`, `p1` or `p0 + Δ·t` with the
/// **unclamped** `t = Δ·(V − p0)/|Δ|²` (homogeneous, denominator `|Δ|²`), chosen by
/// [`select3`] on `h0 = |p0 − V|⁴·|Δ|²`, `h1 = |p1 − V|⁴·|Δ|²`, `hm = |(V − p0) × Δ|²`
/// (**original bug kept**: the end-point terms square the squared distance); `V` is scaled
/// by the chosen denominator. Point mode: `p0`, denominator 1.
///
/// Then the fan of triangles around `V` is walked (at most 35 steps, starting with the
/// triangle across edge `v` as the "previous" one, then across edge `dec(v)` each step, until
/// an open edge or the start triangle): with `δ = P − V`, a step flags `(n_prev × n)·δ + 1e-7
/// < 0` (sign bit), and the triangle with the largest `n·δ` (above 0, first one on ties) is
/// remembered. `ptres[0] = V`. Without any flag the vertex is the answer; otherwise the
/// descent moves to the face of the remembered triangle (the start one if none beat 0).
fn vertex_step(c: &Ctx, q: &Query, o: &mut Out) -> Step {
    let start = *o.i_prim as usize;
    let vi = (*o.i_feature & 0x1F) as usize;
    let mut v = c.vtx(start, vi);
    if q.seg {
        let (p0, p1, d, dd) = (q.p0, q.p1, q.d, q.dd);
        let ex = p0.x - v.x;
        let ey = p0.y - v.y;
        let ez = p0.z - v.z;
        let r0 = (ex * ex + ey * ey) + ez * ez;
        let h0 = (r0 * r0) * dd;
        let (wx, wy, wz) = (v.x - p0.x, v.y - p0.y, v.z - p0.z);
        let g0 = wy * d.z - wz * d.y;
        let r1 = ((p1.x - v.x) * (p1.x - v.x) + (p1.y - v.y) * (p1.y - v.y))
            + (p1.z - v.z) * (p1.z - v.z);
        let t = (d.x * wx + wy * d.y) + wz * d.z;
        let g1 = wz * d.x - d.z * wx;
        let g2 = d.y * wx - wy * d.x;
        let hm = (g0 * g0 + g1 * g1) + g2 * g2;
        let h1 = (r1 * r1) * dd;
        let segpt = Vec3::new(p0.x * dd + d.x * t, p0.y * dd + d.y * t, p0.z * dd + d.z * t);
        let sel = select3(h0, h1, hm);
        o.ptres[1] = [p0, p1, segpt][sel];
        o.den = ((sel >> 1) as f32) * dd + (((sel ^ 2) >> 1) as f32);
        v = v * o.den;
    } else {
        o.ptres[1] = q.p0;
        o.den = 1.0;
    }
    let p = o.ptres[1];
    let dl = Vec3::new(p.x - v.x, p.y - v.y, p.z - v.z);
    let mut j = vi;
    let nb0 = c.topo[start][j];
    let mut prev = if nb0 < 0 {
        Vec3::ZERO
    } else {
        c.nrm(nb0 as usize)
    };
    let mut best = 0.0f32;
    let mut best_prim = start;
    let mut cur = start;
    let mut flag = 0u32;
    let mut count = 35;
    let fl;
    loop {
        let n = c.nrm(cur);
        let x = ((((n.z * prev.y) - (n.y * prev.z)) * dl.x
            + ((prev.z * n.x) - (n.z * prev.x)) * dl.y)
            + ((n.y * prev.x) - (prev.y * n.x)) * dl.z)
            + EPS7;
        let f = sb(x) | flag;
        let dot = (n.x * dl.x + n.y * dl.y) + n.z * dl.z;
        let s = sb(best - dot);
        if s != 0 {
            best_prim = cur;
        }
        best = ((s ^ 1) as f32) * best + (s as f32) * dot;
        let next = c.topo[cur][DEC_MOD3[j]];
        if next < 0 {
            fl = f;
            break;
        }
        prev = n;
        j = back_edge(c.topo, next as usize, cur as i32);
        cur = next as usize;
        if cur != start {
            count -= 1;
            flag = f;
            if count != 0 {
                continue;
            }
        }
        fl = f;
        break;
    }
    o.ptres[0] = v;
    if fl == 0 {
        return Step::Done;
    }
    *o.i_prim = best_prim as i32;
    *o.i_feature = 0x40;
    Step::Move
}

/// `sub_351DB3A0`: divide both points by `den` unless `den == 1` (or NaN, `ucomiss; je`) or
/// `|den| <= 1e-20`.
fn finalize(ptres: &mut [Vec3; 2], den: f32) {
    if den == 1.0 || den.is_nan() || !(den.abs() > EPS20) {
        return;
    }
    let inv = 1.0 / den;
    scale(&mut ptres[1], inv);
    scale(&mut ptres[0], inv);
}

impl TriMesh {
    /// Slot 11 `FindClosestPoint` (0x351C6780); see [`mesh_find_closest_point`].
    #[allow(clippy::too_many_arguments)]
    pub fn find_closest_point(
        &self,
        gwd: &GeomWorldData,
        i_prim: &mut i32,
        i_feature: &mut i32,
        ptdst0: Vec3,
        ptdst1: Vec3,
        ptres: &mut [Vec3; 2],
        n_max_iters: i32,
    ) -> i32 {
        mesh_find_closest_point(
            &self.view(),
            &self.topology,
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }
}

/// `CTriMesh` slot 11 `FindClosestPoint` (0x351C6780) over a mesh view and its topology
/// (`+112`); see the module docs for the semantics.
///
/// 1. `ptres[1] = ptdst0`, `ptres[0] = (1e10, 1e10, 1e10)`; returns −1 at once (no
///    finalisation) when `iPrim >= nTris` (unsigned) or `(iFeature & 0x1F) >= 3`.
/// 2. Loop: run the step of the current feature type (`0x40` face, `0x20` edge, `0` vertex;
///    `0x60` finalises and returns −1). A finished step finalises and returns the remaining
///    count (which can be `<= 0` here, see 4).
/// 3. After a move, the new `(iPrim, iFeature)` is compared with the last four recorded states
///    (initially `−1`) and then recorded in that ring. A new state costs one iteration and the
///    loop goes on while the count stays `> 0`.
/// 4. On a repeated state or an exhausted count: in segment mode, when the state recorded
///    before the current one was a face (`0x40`), the loop goes on anyway (no decrement);
///    otherwise it finalises and returns `max(count, 0)`.
#[allow(clippy::too_many_arguments)]
pub fn mesh_find_closest_point(
    m: &MeshView,
    topology: &[[i32; 3]],
    gwd: &GeomWorldData,
    i_prim: &mut i32,
    i_feature: &mut i32,
    ptdst0: Vec3,
    ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    n_max_iters: i32,
) -> i32 {
    let (p0, p1) = (ptdst0, ptdst1);
    let d = Vec3::new(p1.x - p0.x, p1.y - p0.y, p1.z - p0.z);
    let dd = (d.y * d.y + d.x * d.x) + d.z * d.z;
    let q = Query {
        p0,
        p1,
        d,
        dd,
        seg: sb(EPS4 - dd) != 0,
    };
    ptres[1] = p0;
    ptres[0] = Vec3::splat(FAR);
    if (*i_prim as u32) >= (m.n_tris as u32) || (*i_feature & 0x1F) >= 3 {
        return -1;
    }
    let c = Ctx {
        m: *m,
        topo: topology,
        gwd,
    };
    let mut o = Out {
        i_prim,
        i_feature,
        ptres,
        den: 1.0,
    };
    let mut hist_prim = [-1i32; 4];
    let mut hist_feat = [-1i32; 4];
    let mut hidx = 0usize;
    let mut iters = n_max_iters;
    loop {
        let step = match *o.i_feature & 0x60 {
            0x40 => face_step(&c, &q, &mut o),
            0x20 => edge_step(&c, &q, &mut o),
            0 => vertex_step(&c, &q, &mut o),
            _ => {
                finalize(o.ptres, o.den);
                return -1;
            }
        };
        if let Step::Done = step {
            finalize(o.ptres, o.den);
            return iters;
        }
        let (np, nf) = (*o.i_prim, *o.i_feature);
        let seen = (0..4).any(|k| (np.wrapping_sub(hist_prim[k]) | nf.wrapping_sub(hist_feat[k])) == 0);
        hist_feat[hidx] = nf;
        hist_prim[hidx] = np;
        hidx = (hidx + 1) & 3;
        if !seen {
            iters -= 1;
            if iters > 0 {
                continue;
            }
        }
        if !q.seg || hist_feat[(hidx + 2) & 3] != 0x40 {
            finalize(o.ptres, o.den);
            return iters - (iters & (iters >> 31));
        }
    }
}

/// `sub_35158520` (heightfield edge candidate): edge `a → b` (`u = b − a`) replaces `best`
/// when `|(p − a) × u|² < |p − best|²·|u|²`, `u·(p − a) > 0` and `u·(p − b) < 0`; the new
/// point is `a + u·(u·(p − a)/|u|²)`, `feature = 0x20`.
fn hf_edge(a: Vec3, b: Vec3, p: Vec3, best: &mut Vec3, feature: &mut i32) {
    let (ux, uy, uz) = (b.x - a.x, b.y - a.y, b.z - a.z);
    let (wx, wy, wz) = (p.x - a.x, p.y - a.y, p.z - a.z);
    let t = (wy * uy + wx * ux) + wz * uz;
    let uu = (ux * ux + uy * uy) + uz * uz;
    let c0 = wz * ux - uz * wx;
    let c1 = wy * uz - wz * uy;
    let c2 = wx * uy - wy * ux;
    let cr = (c0 * c0 + c1 * c1) + c2 * c2;
    let bd = ((p.y - best.y) * (p.y - best.y) + (p.x - best.x) * (p.x - best.x))
        + (p.z - best.z) * (p.z - best.z);
    if cr < bd * uu && t > 0.0 {
        let te = ((p.y - b.y) * uy + (p.x - b.x) * ux) + (p.z - b.z) * uz;
        if te < 0.0 {
            let k = t / uu;
            *best = Vec3::new(k * ux + a.x, k * uy + a.y, uz * k + a.z);
            *feature = 0x20;
        }
    }
}

/// `sub_35158770` (heightfield triangle candidate): triangle `t` with the unnormalised normal
/// `n` replaces `best` when `p` is strictly above the plane (`h = n·(p − a) > 0`), `h² <
/// |n|²·|p − best|²` and `p` is strictly inside all three edges; the new point is `p` projected
/// with the normalised normal (`sub_350210A0`: `n·(1/sqrtss(|n|²))`, `(0,0,1)` when
/// `|n|² <= 1e-20`), `feature = 0x40`.
fn hf_tri(t: &[Vec3; 4], p: Vec3, best: &mut Vec3, feature: &mut i32) {
    let [a, b, c, n] = *t;
    let h = ((p.y - a.y) * n.y + n.x * (p.x - a.x)) + (p.z - a.z) * n.z;
    if !(h > 0.0) {
        return;
    }
    let nn = (n.x * n.x + n.y * n.y) + n.z * n.z;
    let bd = ((p.y - best.y) * (p.y - best.y) + (p.x - best.x) * (p.x - best.x))
        + (p.z - best.z) * (p.z - best.z);
    if !(h * h < nn * bd) {
        return;
    }
    // ((V − U) × (p − U))·n with the binary's order
    let side = |u: Vec3, v: Vec3| {
        let ez = v.z - u.z;
        ((ez * (p.x - u.x) - (p.z - u.z) * (v.x - u.x)) * n.y
            + ((v.y - u.y) * (p.z - u.z) - ez * (p.y - u.y)) * n.x)
            + ((p.y - u.y) * (v.x - u.x) - (v.y - u.y) * (p.x - u.x)) * n.z
    };
    if !(side(a, b) > 0.0) || !(side(b, c) > 0.0) {
        return;
    }
    // edge c → a: the binary forms `a.z − c.z` etc. the same way
    if !(side(c, a) > 0.0) {
        return;
    }
    let nu = if nn <= EPS20 || nn.is_nan() {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / nn.sqrt();
        Vec3::new(n.x * inv, inv * n.y, inv * n.z)
    };
    let k = (nu.y * (p.y - a.y) + nu.x * (p.x - a.x)) + nu.z * (p.z - a.z);
    *best = Vec3::new(p.x - nu.x * k, p.y - nu.y * k, p.z - nu.z * k);
    *feature = 0x40;
}

/// `CHeightfield` slot 11 `FindClosestPoint` (0x35155CB0) over the heightfield primitive the
/// object keeps at `+608` (step `+660`, 1/step `+668`, size `+676`, heightscale `+692`,
/// typemask `+696`, typehole `+700`, typepower `+704`, callbacks `+712` / `+720`).
///
/// Only `ptdst0` is read (`ptdst1` and `nMaxIters` are ignored; the binary has no 8th
/// argument). `ptres[1] = ptdst0`. The point goes to the grid frame, `l = Rᵀ·(p − offset)`
/// (`(dx·R0j + dy·R1j) + dz·R2j`), times `1/scale` unless `scale == 1` (or NaN). Cell
/// `(ix, iy) = floor_magic(l·stepr)` (the `−0.5 + 12582912` trick). **Returns −1** with
/// `ptres[0] = (1e10, 1e10, 1e10)` (and `iPrim`/`iFeature` untouched) when `ix > nx − 2` or
/// `iy > ny − 2` (unsigned, so negative cells too) or the cell's surface type
/// (`(getSurfType(ix, iy) & typemask) >> typepower`) is the hole type.
///
/// Otherwise, with the four corner heights `h·heightscale` (`getHeight` called for
/// `(ix, iy)`, `(ix+1, iy)`, `(ix, iy+1)`, `(ix+1, iy+1)`): the start point is the corner
/// `(jx, jy) = (ix + [l.x > ix·step.x + 0.5], iy + [l.y > iy·step.y + 0.5])` (**original
/// quirk kept**: `+ 0.5` in world units, not half a cell), `iFeature = 0` (vertex); then the
/// candidates in order: triangle `(x0,y0,h00), (x1,y0,h10), (x0,y1,h01)`, triangle
/// `(x0,y1,h01), (x1,y0,h10), (x1,y1,h11)` (normals from the binary's explicit `0·…` cross
/// products), the column edge at `jx`, the row edge at `jy`, the diagonal
/// `((ix+1)·sx, y0, h10) → (x0, (iy+1)·sy, h01)` (here the coordinates are `(float)(i+1)·s`,
/// elsewhere `s + i·s`). The cell corner is `x0 = sx·ix`, `x1 = sx + x0`. The result:
/// `iPrim = 0`, `ptres[0] = (R·best)·scale + offset`, returns 1.
///
/// # Panics
/// When a callback is missing (the binary would call a null pointer).
#[allow(clippy::too_many_arguments)]
pub fn heightfield_find_closest_point(
    hf: &HeightfieldPrim,
    gwd: &GeomWorldData,
    i_prim: &mut i32,
    i_feature: &mut i32,
    ptdst0: Vec3,
    _ptdst1: Vec3,
    ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    ptres[1] = ptdst0;
    let r = &gwd.r.m;
    let (dx, dy, dz) = (
        ptdst0.x - gwd.offset.x,
        ptdst0.y - gwd.offset.y,
        ptdst0.z - gwd.offset.z,
    );
    let mut l = Vec3::new(
        (dx * r[0][0] + dy * r[1][0]) + dz * r[2][0],
        (dy * r[1][1] + dx * r[0][1]) + dz * r[2][1],
        (dx * r[0][2] + dy * r[1][2]) + dz * r[2][2],
    );
    let s = gwd.scale;
    if !(s == 1.0 || s.is_nan()) {
        let inv = 1.0 / s;
        l = Vec3::new(l.x * inv, l.y * inv, l.z * inv);
    }
    let ix = round_magic(l.x * hf.stepr[0] - 0.5);
    let iy = round_magic(l.y * hf.stepr[1] - 0.5);
    let get_h = hf.get_height.expect("heightfield without a height callback");
    let get_s = hf
        .get_surf_type
        .expect("heightfield without a surface-type callback");
    let out_x = (ix as u32) > (hf.size[0].wrapping_sub(2) as u32);
    let out_y = (iy as u32) > (hf.size[1].wrapping_sub(2) as u32);
    let hole = || {
        let t = i32::from(hf.type_mask & get_s(ix, iy)) >> (hf.type_power & 31);
        is_zero_idiom(t.wrapping_sub(hf.type_hole)) != 0
    };
    if out_x || out_y || hole() {
        ptres[0] = Vec3::splat(FAR);
        return -1;
    }
    let hs = hf.height_scale;
    let h = [
        get_h(ix, iy) * hs,
        get_h(ix + 1, iy) * hs,
        get_h(ix, iy + 1) * hs,
        get_h(ix + 1, iy + 1) * hs,
    ];
    let (sx, sy) = (hf.step[0], hf.step[1]);
    let (fx, fy) = (ix as f32, iy as f32);
    let bx = sb((sx * fx + 0.5) - l.x) as i32;
    let by = sb((sy * fy + 0.5) - l.y) as i32;
    let cx = (bx + ix) as f32;
    let cy = (by + iy) as f32;
    let mut best = Vec3::new(cx * sx, cy * sy, h[(2 * by + bx) as usize]);
    *i_feature = 0;
    let x0 = sx * fx;
    let y0 = sy * fy;
    let x1 = sx + x0;
    let y1 = sy + y0;
    let (h00, h10, h01, h11) = (h[0], h[1], h[2], h[3]);
    // triangle 1
    {
        let ex = x1 - x0;
        let ey = y1 - y0;
        let n = Vec3::new(
            0.0 * (h01 - h00) - (h10 - h00) * ey,
            (h10 - h00) * 0.0 - (h01 - h00) * ex,
            ey * ex - 0.0 * 0.0,
        );
        let t = [
            Vec3::new(x0, y0, h00),
            Vec3::new(x1, y0, h10),
            Vec3::new(x0, y1, h01),
            n,
        ];
        hf_tri(&t, l, &mut best, i_feature);
    }
    // triangle 2
    {
        let x1b = x0 + sx;
        let dxa = x1 - x0;
        let dxb = x1b - x0;
        let n = Vec3::new(
            (y0 - y1) * (h11 - h01) - (h10 - h01) * 0.0,
            (h10 - h01) * dxb - (h11 - h01) * dxa,
            0.0 * dxa - (y0 - y1) * dxb,
        );
        let t = [
            Vec3::new(x0, y1, h01),
            Vec3::new(x1, y0, h10),
            Vec3::new(x1b, y1, h11),
            n,
        ];
        hf_tri(&t, l, &mut best, i_feature);
    }
    // column edge at jx
    {
        let x = cx * sx;
        let a = Vec3::new(x, y0, h[bx as usize]);
        let b = Vec3::new(x, sy + sy * fy, h[2 + bx as usize]);
        hf_edge(a, b, l, &mut best, i_feature);
    }
    // row edge at jy
    {
        let y = cy * sy;
        let a = Vec3::new(sx * fx, y, h[2 * by as usize]);
        let b = Vec3::new(sx + sx * fx, y, h[2 * by as usize + 1]);
        hf_edge(a, b, l, &mut best, i_feature);
    }
    // diagonal
    {
        let a = Vec3::new((ix + 1) as f32 * sx, sy * fy, h10);
        let b = Vec3::new(sx * fx, (iy + 1) as f32 * sy, h01);
        hf_edge(a, b, l, &mut best, i_feature);
    }
    *i_prim = 0;
    let w = Vec3::new(
        (best.x * r[0][0] + best.y * r[0][1]) + best.z * r[0][2],
        (best.y * r[1][1] + best.x * r[1][0]) + best.z * r[1][2],
        (best.y * r[2][1] + best.x * r[2][0]) + best.z * r[2][2],
    );
    ptres[0] = Vec3::new(
        w.x * s + gwd.offset.x,
        w.y * s + gwd.offset.y,
        w.z * s + gwd.offset.z,
    );
    1
}

/// `CHeightfield` slot 12 `CalcVolumetricPressure` (0x35154CB0): an empty function — `P` and
/// `L` are left as they are.
#[allow(clippy::too_many_arguments)]
pub fn heightfield_calc_volumetric_pressure(
    _hf: &HeightfieldPrim,
    _gwd: &GeomWorldData,
    _epicenter: Vec3,
    _k: f32,
    _rmin: f32,
    _com: Vec3,
    _p: &mut Vec3,
    _l: &mut Vec3,
) {
}
