//! sub_351C2D40 (inferred name `TraceIntersectionBorder`): one leg of `CTriMesh` slot 54's walk
//! along the intersection curve of two meshes (or a mesh and a primitive), across the
//! neighbouring triangles of side `s`.
//!
//! The pair function (`INTERSECT`) reports in `inters.iFeature[1][s]` (byte `38 + s`) the edge
//! of side `s`'s triangle through which the intersection line leaves at `inters.pt[1]`, with
//! bit 7 set when the line leaves through that side. Each step:
//!
//! 1. `nb = topology[cur][edge]`. Stop with 2 ("closed") when `nb | s<<31` is the start
//!    triangle and its slot towards `cur` is the start edge; when (with more than 3 points and
//!    `ip+104` clear) `pt[1]` is within the closing distance (`(2·mvd)²`) of the start point; or
//!    when `nb < 0` (open edge). Return 0 when the pool budget is used up.
//! 2. `prim[s] = triangle(nb)` (sub_351C27A0) and `INTERSECT[t0][t1](prim0, prim1, inters)`
//!    (the record is overwritten in place). If it misses, the curve passes through a vertex of
//!    the shared edge: the vertex of `nb` (or of `cur`) within `mvd` of `pt[1]` is the pivot,
//!    and the triangles around it are tried in turn (≤ 30, not the pivot's other side, no open
//!    edge) until one intersects; otherwise return 0.
//! 3. Record the point and the pair `(nb | feat<<23, other | feat<<23)`, add the triangle's
//!    normal to the side's double sum, keep the steepest normal (`|n.z|` largest) signed by
//!    side, count it; after 30 points, return 0 if the curve came back within `0.01·tol` of
//!    the point remembered every 11 steps (a loop that does not close).
//! 4. Continue on this side unless bit 7 of the other side's byte is set (return 1: the
//!    caller continues on the other side).

#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::candidates::{adj_slot, DEC, INC};
use super::CurPrim;
use crate::collision::contact::PrimInters;
use crate::collision::pairs::PairTables;
use crate::geometry::primitives::Primitive;
use crate::geometry::shapes::protocol::GeomUnderTest;
use crate::geometry::trimesh::TriMesh;
use crate::ids::GeomType;
use crate::math::Vec3;

/// The walk state (the caller's stack block passed as `a8`).
#[derive(Clone, Debug, Default)]
pub struct WalkState {
    /// Pool index of the first border point of this contact.
    pub base: usize,
    /// `+24` points recorded.
    pub count: i32,
    /// `+28` budget.
    pub cap: i32,
    /// `+32` the start point (`inters.pt[0]` at registration).
    pub start_pt: Vec3,
    /// `+44` start triangle `| side << 31`.
    pub start_code: i32,
    /// `+48` start edge.
    pub start_edge: i32,
    /// `+52` closing distance² (`(2·mvd)²`, 0 with `ip+104`).
    pub tol2: f32,
    /// `+56` `ip+104`.
    pub i104: i32,
    /// `+60` index of the remembered point.
    pub ref_idx: i32,
    /// `+64` countdown to the next remembered point.
    pub ref_ctr: i32,
    /// `+72` per-side normal sums (double).
    pub nsum: [[f64; 3]; 2],
    /// `+120` the steepest normal, signed by side.
    pub dirn: [f32; 3],
    /// `+132` normals per side.
    pub ncnt: [i32; 2],
}

/// Byte `38 + s` (`iFeature[1][s]`).
fn feat1(inters: &PrimInters, s: usize) -> u8 {
    inters.i_feature[1][s]
}

fn d2(a: Vec3, b: Vec3) -> f32 {
    let (x, y, z) = (a.x - b.x, a.y - b.y, a.z - b.z);
    (y * y + x * x) + z * z
}

/// sub_351C2D40. `mesh` is side `s`'s mesh, `gt` its record; `cur` the two current primitives
/// (`a3`), `other_idx` the other side's triangle (`a4`), `other_type` its primitive type (`a5`).
#[allow(clippy::too_many_arguments)]
pub fn walk(
    mesh: &TriMesh,
    mvd: f32,
    s: usize,
    cur: &mut [CurPrim; 2],
    other_idx: i32,
    other_type: i32,
    inters: &mut PrimInters,
    gt: &GeomUnderTest,
    st: &mut WalkState,
    pts: &mut Vec<Vec3>,
    idx: &mut Vec<[i32; 2]>,
    tables: &PairTables,
) -> i32 {
    let so = s ^ 1;
    let mut types = [0i32; 2];
    types[s] = 1;
    types[so] = other_type;
    let ty = |t: i32| GeomType::from_id(t).unwrap_or(GeomType::Box);
    let isect = tables.intersect_fn(ty(types[0]), ty(types[1]));
    let topo = |i: i32| mesh.topology[i as usize];
    let side_bit = (s as i32) << 31;
    let mut cur_t = cur[s].idx;
    let mut v62 = cur_t;
    loop {
        let mut nb = topo(cur_t)[(feat1(inters, s) & 0x1F) as usize];
        if (nb | side_bit) == st.start_code && adj_slot(&topo(nb), cur_t) as i32 == st.start_edge {
            return 2;
        }
        let count = st.count;
        // `comiss; jb`: a NaN distance also closes
        if (count & st.i104.wrapping_sub(1)) > 3 && !(d2(inters.pt[1], st.start_pt) >= st.tol2) {
            return 2;
        }
        if nb < 0 {
            return 2;
        }
        if count >= st.cap {
            return 0;
        }
        let set = |t: i32, cur: &mut [CurPrim; 2]| {
            cur[s] = CurPrim {
                prim: Primitive::Triangle(mesh.triangle_in_gtest(t as usize, gt)),
                idx: cur[s].idx,
            };
        };
        set(nb, cur);
        if !isect(&cur[0].prim, &cur[1].prim, inters) {
            let tri = match cur[s].prim {
                Primitive::Triangle(t) => t,
                _ => unreachable!(),
            };
            let slot = adj_slot(&topo(nb), cur_t);
            let m2 = mvd * mvd;
            let p = inters.pt[1];
            let pivot_other;
            let next;
            if !(d2b(p, tri.pt[slot]) >= m2) {
                pivot_other = cur_t;
                v62 = nb;
                next = topo(nb)[DEC[slot]];
            } else {
                if d2b(p, tri.pt[INC[slot]]) >= m2 {
                    return 0;
                }
                pivot_other = nb;
                next = topo(cur_t)[DEC[(feat1(inters, s) & 0x1F) as usize]];
            }
            nb = next;
            if nb < 0 {
                return 0;
            }
            let mut n_rot = 0;
            set(nb, cur);
            while !isect(&cur[0].prim, &cur[1].prim, inters) {
                let prev = v62;
                v62 = nb;
                nb = topo(nb)[DEC[adj_slot(&topo(nb), prev)]];
                if nb < 0 || nb == pivot_other {
                    return 0;
                }
                n_rot += 1;
                if n_rot >= 30 {
                    return 0;
                }
                set(nb, cur);
            }
        }
        let k = st.base + count as usize;
        ensure(pts, idx, k + 1);
        idx[k][s] = nb | (i32::from(feat1(inters, s)) << 23);
        idx[k][so] = other_idx | (i32::from(feat1(inters, so)) << 23);
        pts[k] = inters.pt[1];
        let n = match cur[s].prim {
            Primitive::Triangle(t) => t.n,
            _ => unreachable!(),
        };
        st.nsum[s][0] += f64::from(n.x);
        st.nsum[s][1] += f64::from(n.y);
        st.nsum[s][2] += f64::from(n.z);
        if !(st.dirn[2].abs() >= n.z.abs()) {
            let f = (1 - 2 * s as i32) as f32;
            st.dirn = [n.x * f, n.y * f, n.z * f];
        }
        st.ncnt[s] += 1;
        if count > 30 {
            let r = pts[st.base + st.ref_idx as usize];
            if !(d2(pts[k], r) >= st.tol2 * f32::from_bits(0x3C23_D70A)) {
                return 0;
            }
        }
        let c1 = st.ref_ctr - 1;
        st.ref_idx += (c1 >> 31) & (count - st.ref_idx);
        st.ref_ctr = c1 + ((c1 >> 31) & 0xB);
        st.count = count + 1;
        cur[s].idx = nb;
        cur_t = nb;
        v62 = nb;
        if feat1(inters, so) & 0x80 != 0 {
            return 1;
        }
    }
}

/// `((p.x − v.x)² + (p.y − v.y)²) + (p.z − v.z)²` (the vertex tests of step 2).
fn d2b(p: Vec3, v: Vec3) -> f32 {
    let (x, y, z) = (p.x - v.x, p.y - v.y, p.z - v.z);
    (x * x + y * y) + z * z
}

/// Grows the pools to hold index `k` (the original's are fixed 2048-entry arrays).
pub(crate) fn ensure(pts: &mut Vec<Vec3>, idx: &mut Vec<[i32; 2]>, n: usize) {
    if pts.len() < n {
        pts.resize(n, Vec3::ZERO);
    }
    if idx.len() < n {
        idx.resize(n, [0, 0]);
    }
}
