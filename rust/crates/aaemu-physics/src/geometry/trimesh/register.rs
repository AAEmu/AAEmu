//! `CTriMesh` slot 54 `RegisterIntersection` (x64 0x351D3950, x86 0x351E9650) and the mesh's
//! protocol slots 57 / 58 / 59 (0x351C3380 / 0x351C4610 / 0x351C5B90). `CHeightfield` and
//! `CVoxelGeom` use the same functions.
//!
//! # `RegisterIntersection(primThis, primOther, gtestThis, gtestOther, inters)`
//!
//! Called by the tree traversal on the registering side (the mesh against a primitive, side A
//! for mesh vs mesh) for a triangle pair that `INTERSECT` reported. Builds one contact in the
//! next slot of the caller's contact pool:
//!
//! 1. **Dedup.** Contacts of this `Intersect` call (and of kept previous calls) left their
//!    border in the per-caller border pool (`+0x10EC8` points / `+0x16ED0` triangle pairs,
//!    entries `[+0x16ECC, +0x16EC8)`). With `ip+104` the pair `(triThis, triOther)` already
//!    present returns 0; otherwise a pool point within `2·minVtxDist` of `inters.pt[1]` does.
//!    The pool budget for this contact is `min(2032 − used, ip+104 ? 1000 : 100)`.
//! 2. **Border.** `pt[0]`, `pt[1]` with their pairs; when a feature of `pt[1]` has the side bit
//!    (bit 7) and `ip+100` is clear, the intersection curve is followed across neighbouring
//!    triangles ([`walk`]); a closed curve (return 2) marks every pair's triangles used in
//!    both BV trees (BV slot 25) and keeps `ip+112` (convexity), anything else clears it.
//!    `+104 = n`, `+108 = closed`; the pool count grows by `n` (kept even on later returns).
//! 3. **Normal.** The average of the walked triangles' normals (side B negated; the side with
//!    fewer non-zero normals wins), normalised in double; the "preferred direction" is the
//!    steepest walked normal unless that is flatter than 0.95, then the average.
//! 4. **Direction.** Over the border points: the largest relative velocity with a positive
//!    component along the normal, the point farthest from the rotation axis and from the
//!    rotation centre. Mode 1 (rotational, `ip.mode > 0`): axis `ip+16` (or `(p−c) × n` from
//!    the farthest point, written back into `ip`), turned against the normal's side, angle
//!    limit `|axis|·dt`, `tmax = sin(min(angle, π/2))`; a point closer than `minAxisDist`
//!    goes to the fallback. Mode 0: `dir = −vrel`; below `vrel_min` the fallback; `vrel = 0`
//!    against a ray uses the ray's sideways direction.
//! 5. **Unprojection loop.** The registering geometry is moved (sub_351C4360: offset along
//!    `dir·t`, or rotation about the centre) by the accumulated `t`; after each step both
//!    sides list unprojection candidates (slot 57) at the result point and every candidate
//!    pair is unprojected again; the largest `t` wins (its ids are the contact's). Up to 3
//!    consecutive steps shorter than `minVtxDist` (0.001 rad) or until `t ≤ 0`; exceeding the
//!    limit falls back. Rotational results near the axis are redone linearly along `−n`.
//! 6. **Fallback.** Unproject along a direction derived from the border: the pair's normal
//!    (`inters+84 > n`), the preferred direction (`n < 3`), or the null vector of the
//!    border polygon's second-moment matrix (oriented by the area normal or the averaged
//!    normal), `tmax = ip.maxUnproj`; the border's length-weighted centre becomes the contact
//!    centre. An unprojection that fails at a small border gives a "touching" contact
//!    (`t = 1e-4`, `vel = −1`, `pt = border[0]`, `n` = averaged normal, `dir = −n`, no area).
//! 7. **Area.** With an area attached: a face of this side anti-parallel to a face of the other
//!    (`n·n' < −cos(gap)`) builds a face–face patch, else a face of one side parallel to an
//!    edge of the other a face–edge patch: both features' polygons (slots 58 / 59) are clipped
//!    in the face's frame (sub_35110F50) and lifted back to 3D.
//! 8. Border points of the pair function are appended (`+124 = 0`), or a long pair border
//!    (`inters+84 > 100`, no `ip+104`) collapses to the centre. The contact is counted; the
//!    stop flag goes up at the pool limit, with `ip+98` or with `ip+112`; else the next slot
//!    gets a pool area.
//!
//! Returns 1 for a contact, 0 for none.

// The comparisons mirror `comiss`/`ucomiss` branches, whose NaN behaviour `!(a > b)` keeps.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

pub mod candidates;
pub mod motion;
pub mod patch;
pub mod polyclip;
pub mod walk;

use super::protocol::{BorderKind, GeomProtocol, IntersectCtx};
use super::TriMesh;
use crate::collision::contact::{GeomContact, PrimInters, UnprojParams, UnprojResult};
use crate::collision::swept::poly::crt::{atan2, cos as dcos};
use crate::collision::swept::{unproject_none, UnprojFn};
use crate::geometry::primitives::{Primitive, Triangle};
use crate::geometry::shapes::protocol::{ContactFrame, FeaturePolygon, IntersectionProtocol};
use crate::ids::GeomType;
use crate::math::crt::cosf;
use crate::math::{Mat33, Vec3};
use walk::{ensure, WalkState};

/// Slots 57/58/59 of `CTriMesh` through P1a's trait. The trait carries only the record, so
/// these entry points lose what the binary keeps next to it: slot 57 the candidates' triangle
/// indices (the record's `+48`), slot 58 the BV marking. The intersection code uses the full
/// versions ([`GeomProtocol::proto_unproj_candidates`] and friends, which reach
/// [`TriMesh::unprojection_candidates_mesh`], [`TriMesh::face_polygon_mesh`],
/// [`TriMesh::edge_segment_mesh`]).
impl IntersectionProtocol for TriMesh {
    fn face_polygon(
        &self,
        frame: &mut ContactFrame,
        i_prim: i32,
        i_feature: i32,
        gt: &crate::geometry::shapes::protocol::GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        let _ = i_feature;
        self.face_polygon_mesh(frame, i_prim, gt, None, out)
    }

    fn edge_segment(
        &self,
        frame: &mut ContactFrame,
        i_prim: i32,
        i_feature: i32,
        gt: &crate::geometry::shapes::protocol::GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        self.edge_segment_mesh(frame, i_prim, i_feature, gt, out)
    }
}

/// A primitive pointer of slot 54: the primitive and its record's `+48` (triangle index; −1 for
/// primitives).
#[derive(Clone, Copy, Debug)]
pub struct CurPrim {
    pub prim: Primitive,
    pub idx: i32,
}

/// What slot 57 leaves the caller: `n` candidates `prims[0..n]` with their features and ids,
/// and `base`, where the caller's primitive pointer now points (entry 0 of the list even when
/// `n = 0`).
#[derive(Clone, Debug)]
pub struct CandidateList {
    pub n: i32,
    pub base: CurPrim,
    pub prims: Vec<CurPrim>,
    pub feats: Vec<i32>,
    pub ids: Vec<i8>,
}

/// Mode-0 / mode-1 unprojection parameters plus the two words of the caller's block that
/// sub_351C4360 reads behind them (the saved transform).
#[derive(Clone, Copy, Debug)]
pub struct MotionBase {
    pub r: Mat33,
    pub offset: Vec3,
}

fn tri_of(p: &Primitive) -> Triangle {
    match p {
        Primitive::Triangle(t) => *t,
        other => panic!("slot 54: expected a triangle, got {other:?}"),
    }
}

/// `(x·x + y·y) + z·z`.
fn len2(v: Vec3) -> f32 {
    (v.x * v.x + v.y * v.y) + v.z * v.z
}

/// BV slot 25 on side `k`'s tree; for the primitives' `CSingleBoxTree` the bits live in the
/// record's inline word (`+32`), which the port keeps in `gt.used.word`.
fn mark_used(g: &dyn GeomProtocol, ctx: &mut IntersectCtx<'_>, k: usize, i_tri: i32) {
    use crate::geometry::shapes::protocol::UsedNodeMap;
    let tref = g.proto_bv_tree();
    let inline = ctx.s[k].gt.used.map == UsedNodeMap::Inline;
    if inline {
        ctx.s[k].bvs.i_last_used = ctx.s[k].gt.used.word;
    }
    tref.tree()
        .mark_used_triangle(i_tri, &mut ctx.s[k].bvs, &mut ctx.arena.bv);
    if inline {
        ctx.s[k].gt.used.word = ctx.s[k].bvs.i_last_used;
    }
}

/// `& 0x807FFFFF`: the triangle index of a pool pair entry (feature bits 23–30 cleared).
const IDX_MASK: i32 = 0x807F_FFFF_u32 as i32;

/// Which area patch the detection found.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum AreaKind {
    None,
    /// Face `i` of this side against face `j` of the other.
    FaceFace(usize, usize),
    /// Face `i` of side `k` (0 = this) against edge `j` of the other side.
    FaceEdge(usize, usize, usize),
}

/// `CTriMesh` slot 54 (0x351D3950); see the module docs. `m` is the registering mesh (for a
/// heightfield its region mesh) and `this` the registering geometry (its BV tree and
/// `minVtxDist`).
#[allow(clippy::too_many_lines, clippy::too_many_arguments)]
pub fn mesh_register_intersection(
    m: &TriMesh,
    this: &dyn GeomProtocol,
    ctx: &mut IntersectCtx<'_>,
    side: usize,
    other: &dyn GeomProtocol,
    i_this: usize,
    i_other: usize,
    inters: &mut PrimInters,
) -> i32 {
    let so = side ^ 1;
    // side index k: 0 = this (gtestThis), 1 = other
    let sx = |k: usize| if k == 0 { side } else { so };
    let mvd = this.proto_min_vtx_dist();
    let other_mesh = ctx.s[so].gt.prim_type == 1;
    let idx0 = ctx.s[side].prim_idx[i_this];
    let idx1 = if other_mesh {
        ctx.s[so].prim_idx[i_other]
    } else {
        -1
    };
    let mut cur = [
        CurPrim {
            prim: ctx.s[side].gt.prims[i_this],
            idx: idx0,
        },
        CurPrim {
            prim: ctx.s[so].gt.prims[i_other],
            idx: idx1,
        },
    ];
    let mut tri_idx = [idx0, idx1];
    let i104 = ctx.ip.border_by_tri_pairs;
    let cnt0 = ctx.arena.w16ec8 as u32 as i32;
    let start = ctx.arena.w16ecc as i32;
    // 1. dedup
    let tol2;
    if i104 != 0 {
        tol2 = 0.0;
        for k in start.max(0)..cnt0 {
            let e = ctx
                .arena
                .border_idx
                .get(k as usize)
                .copied()
                .unwrap_or([0, 0]);
            if ((e[0] & IDX_MASK).wrapping_sub(idx0) | (e[1] & IDX_MASK).wrapping_sub(idx1)) == 0 {
                return 0;
            }
        }
    } else {
        tol2 = (mvd * 2.0) * (mvd * 2.0);
        let q = inters.pt[1];
        for k in start.max(0)..cnt0 {
            let p = ctx
                .arena
                .border_pts
                .get(k as usize)
                .copied()
                .unwrap_or(Vec3::ZERO);
            let d =
                ((p.y - q.y) * (p.y - q.y) + (p.x - q.x) * (p.x - q.x)) + (p.z - q.z) * (p.z - q.z);
            if !(d > tol2) {
                return 0;
            }
        }
    }
    let mut c = GeomContact {
        area: ctx.next_area.take(),
        ..GeomContact::default()
    };
    c.i_prim = [idx0, idx1];
    c.border_consecutive = true;
    let (mut cos_gap, mut sin2_gap) = (1.0f32, 0.0f32);
    if c.area.is_some() {
        let g = ctx.ip.max_surface_gap_angle;
        if g >= 0.2 {
            cos_gap = cosf(g);
            let s = dcos(std::f64::consts::FRAC_PI_2 - f64::from(g));
            sin2_gap = (s * s) as f32;
        } else {
            sin2_gap = g * g;
            cos_gap = 1.0 - (g * g) * 2.0;
        }
    }
    c.id = [i32::from(inters.id[0]), i32::from(inters.id[1])];
    c.i_node = inters.i_node;
    let cap = {
        let rem = 2032i32.wrapping_sub(cnt0);
        let lim = i104.wrapping_mul(900).wrapping_add(100).wrapping_sub(rem);
        rem + (lim & (lim >> 31))
    };
    if cap < 0 {
        ctx.next_area = c.area;
        return 0;
    }
    let b = cnt0 as usize;
    // 2. border
    let fb = |inters: &PrimInters, k: usize| -> u8 {
        match k {
            0 => inters.i_feature[0][0],
            1 => inters.i_feature[0][1],
            2 => inters.i_feature[1][0],
            _ => inters.i_feature[1][1],
        }
    };
    let s0 = (fb(inters, 1) >> 7) as usize;
    let mut st = WalkState {
        base: b,
        count: 2,
        cap,
        start_pt: inters.pt[0],
        start_code: tri_idx[s0] | ((s0 as i32) << 31),
        start_edge: i32::from(fb(inters, s0) & 0x1F),
        tol2,
        i104,
        ref_idx: 1,
        ref_ctr: 10,
        ..WalkState::default()
    };
    {
        let a = &mut ctx.arena;
        ensure(&mut a.border_pts, &mut a.border_idx, b + 2);
        a.border_pts[b] = inters.pt[0];
        a.border_pts[b + 1] = inters.pt[1];
        for s in [s0, s0 ^ 1] {
            a.border_idx[b][s] = tri_idx[s] | (i32::from(fb(inters, s)) << 23);
            a.border_idx[b + 1][s] = tri_idx[s] | (i32::from(fb(inters, 2 + s)) << 23);
        }
    }
    c.center = inters.pt[0];
    let v652 = (fb(inters, 3) >> 7) as usize;
    let v47 = v652 ^ 1;
    if ctx.s[sx(v47)].gt.prim_type == 1 {
        let n = tri_of(&cur[v47].prim).n;
        st.nsum[v47] = [f64::from(n.x), f64::from(n.y), f64::from(n.z)];
        let sg = f64::from(2 * v652 as i32 - 1);
        st.dirn = [
            (f64::from(n.x) * sg) as f32,
            (f64::from(n.y) * sg) as f32,
            (f64::from(n.z) * sg) as f32,
        ];
        st.ncnt[v47] = 1;
    }
    let mut last_ret = 0;
    if (fb(inters, 3) | fb(inters, 2)) & 0x80 == 0 || ctx.ip.no_border_walk {
        st.ncnt[0] = 1;
        let n = tri_of(&cur[0].prim).n;
        st.nsum[0] = [f64::from(n.x), f64::from(n.y), f64::from(n.z)];
    } else {
        let mut s = v652;
        let ret = loop {
            let n = tri_of(&cur[s].prim).n;
            st.nsum[s][0] += f64::from(n.x);
            st.nsum[s][1] += f64::from(n.y);
            st.nsum[s][2] += f64::from(n.z);
            st.ncnt[s] += 1;
            if !(st.dirn[2].abs() >= n.z.abs()) {
                let f = (1 - 2 * s as i32) as f32;
                st.dirn = [n.x * f, n.y * f, n.z * f];
            }
            let other_type = ctx.s[sx(s ^ 1)].gt.prim_type;
            let ret = {
                let gtk = ctx.s[sx(s)].gt.clone();
                let tables = ctx.tables;
                let a = &mut *ctx.arena;
                let mut run = |mesh: &TriMesh, mvd: f32| {
                    walk::walk(
                        mesh,
                        mvd,
                        s,
                        &mut cur,
                        tri_idx[s ^ 1],
                        other_type,
                        inters,
                        &gtk,
                        &mut st,
                        &mut a.border_pts,
                        &mut a.border_idx,
                        tables,
                    )
                };
                if s == 0 {
                    run(m, mvd)
                } else {
                    let mut r = None;
                    let omvd = other.proto_min_vtx_dist();
                    let found = other.proto_with_mesh(&mut |om: &TriMesh| r = Some(run(om, omvd)));
                    assert!(found, "slot 54: border walk on a geometry without a mesh");
                    r.expect("walk result")
                }
            };
            ctx.ip_convex &= ret >> 1;
            tri_idx[s] = cur[s].idx;
            s ^= 1;
            if ret != 1 {
                break ret;
            }
        };
        if ret == 2 && st.count > 1 {
            for k in 1..st.count as usize {
                let e = ctx.arena.border_idx[b + k];
                mark_used(this, ctx, side, e[0] & IDX_MASK);
                mark_used(other, ctx, so, e[1] & IDX_MASK);
            }
        }
        last_ret = ret;
    }
    let count = st.count;
    c.border_closed = last_ret >> 1;
    ctx.arena.w16ec8 = u64::from((cnt0 + count) as u32);
    // 3. normal
    for k in 0..3 {
        st.nsum[1][k] = -st.nsum[1][k];
    }
    let pick = if (st.ncnt[0] < st.ncnt[1] && st.ncnt[0] != 0) || st.ncnt[1] == 0 {
        st.nsum[0]
    } else {
        st.nsum[1]
    };
    let l2 = (pick[0] * pick[0] + pick[1] * pick[1]) + pick[2] * pick[2];
    let mut navg = if l2 > f64::from(1e-20f32) {
        let inv = 1.0 / l2.sqrt();
        [inv * pick[0], inv * pick[1], inv * pick[2]]
    } else {
        [0.0, 0.0, 1.0]
    };
    if !(st.dirn[2].abs() >= f32::from_bits(0x3F73_3333)) {
        st.dirn = [navg[0] as f32, navg[1] as f32, navg[2] as f32];
    }
    let pref = Vec3::new(st.dirn[0], st.dirn[1], st.dirn[2]);
    // 4. direction
    let (ga, gb) = (&ctx.s[side].gt, &ctx.s[so].gt);
    let pts: Vec<Vec3> = ctx.arena.border_pts[b..b + count as usize].to_vec();
    let mut vbest = Vec3::new(ga.v.x - gb.v.x, ga.v.y - gb.v.y, ga.v.z - gb.v.z);
    let dv = vbest;
    let mut vmax = 0.0f32;
    let mut ax_d = 0.0f32;
    let mut ax_p = pts[0];
    let mut c_d = 0.0f32;
    let mut c_p = pts[0];
    let nf = Vec3::new(navg[0] as f32, navg[1] as f32, navg[2] as f32);
    let rc = ga.center_of_rotation;
    let axis = ctx.ip.axis_of_rotation;
    for &p in &pts {
        let (wb, cb) = (gb.w, gb.center_of_mass);
        let (bx, by, bz) = (p.x - cb.x, p.y - cb.y, p.z - cb.z);
        let x7 = bz * wb.y - by * wb.z;
        let x8 = by * wb.x - bx * wb.y;
        let x6 = bx * wb.z - bz * wb.x;
        let (wa, ca) = (ga.w, ga.center_of_mass);
        let (ax, ay, az) = (p.x - ca.x, p.y - ca.y, p.z - ca.z);
        let x3 = az * wa.y - ay * wa.z;
        let x2 = ax * wa.z - az * wa.x;
        let x5 = ay * wa.x - ax * wa.y;
        let vr = Vec3::new((dv.x + x3) - x7, (dv.y + x2) - x6, (dv.z + x5) - x8);
        if (nf.x * vr.x + nf.y * vr.y) + nf.z * vr.z > 0.0 {
            let l = (vr.x * vr.x + vr.y * vr.y) + vr.z * vr.z;
            if l > vmax {
                vmax = l;
                vbest = vr;
            }
        }
        let (dx, dy, dz) = (p.x - rc.x, p.y - rc.y, p.z - rc.z);
        let cx = dy * axis.z - dz * axis.y;
        let cy = dz * axis.x - dx * axis.z;
        let cz = dx * axis.y - dy * axis.x;
        let la = (cx * cx + cy * cy) + cz * cz;
        if la > ax_d {
            ax_d = la;
            ax_p = p;
        }
        let lc = (dx * dx + dy * dy) + dz * dz;
        if lc > c_d {
            c_d = lc;
            c_p = p;
        }
    }
    let mut p = UnprojParams {
        mode: 0,
        dir: Vec3::ZERO,
        center: Vec3::ZERO,
        vel: 0.0,
        tmax: 0.0,
        tmin: 0.0,
        min_pt_dist: 0.0,
        max_cos: 0.1,
        flag: 0,
    };
    let mut lim: f64; // v189 / v546
    let mut early_fallback = false;
    let saved = MotionBase {
        r: ctx.s[side].gt.r,
        offset: ctx.s[side].gt.offset,
    };
    if ctx.ip.i_unprojection_mode > 0 {
        let (mut d, mut pt) = (ax_d, ax_p);
        if len2(axis) == 0.0 || len2(axis).is_nan() {
            d = c_d;
            pt = c_p;
        }
        let ma = ctx.ip.min_axis_dist;
        if !(d >= ma * ma) {
            early_fallback = true;
            lim = 0.0;
        } else {
            p.mode = 1;
            p.center = rc;
            let r = Vec3::new(pt.x - rc.x, pt.y - rc.y, pt.z - rc.z);
            let ax;
            if len2(axis) > 0.0 {
                let a = axis;
                let x3 = r.z * a.y - r.y * a.z;
                let x2 = r.x * a.z - a.x * r.z;
                let x1 = a.x * r.y - r.x * a.y;
                let s =
                    (f64::from(x3) * navg[0] + f64::from(x2) * navg[1]) + f64::from(x1) * navg[2];
                let k = (-1 - (((s as f32).to_bits() as i32 >> 30) & !1)) as f32;
                ax = Vec3::new(a.x * k, a.y * k, a.z * k);
            } else {
                let (rx, ry, rz) = (f64::from(r.x), f64::from(r.y), f64::from(r.z));
                if (rx * navg[0] + ry * navg[1]) + rz * navg[2] > 0.0 {
                    ctx.next_area = c.area;
                    return 0;
                }
                let v201 = ry * navg[0] - rx * navg[1];
                let v202 = rx * navg[2] - rz * navg[0];
                let v203 = rz * navg[1] - ry * navg[2];
                let l = (v202 * v202 + v203 * v203) + v201 * v201;
                let (x, y, z) = if l > f64::from(1e-20f32) {
                    let inv = 1.0 / l.sqrt();
                    (v203 * inv, inv * v202, inv * v201)
                } else {
                    (0.0, 0.0, 1.0)
                };
                ax = Vec3::new(x as f32, y as f32, z as f32);
                ctx.ip.axis_of_rotation = ax;
            }
            let vel = ((ax.x * ax.x + ax.y * ax.y) + ax.z * ax.z).sqrt();
            let inv = 1.0 / vel;
            p.dir = Vec3::new(ax.x * inv, ax.y * inv, ax.z * inv);
            p.vel = vel;
            let ang = f64::from(vel * ctx.ip.time_interval);
            lim = ang;
            let h = std::f64::consts::FRAC_PI_2;
            p.tmax =
                crate::collision::swept::poly::crt::sin(((ang + h) - (h - ang).abs()) * 0.5) as f32;
        }
    } else {
        let d = Vec3::new(-vbest.x, -vbest.y, -vbest.z);
        p.dir = d;
        let l = (d.x * d.x + d.y * d.y) + d.z * d.z;
        let vm = ctx.ip.vrel_min;
        lim = 0.0;
        if !(l >= vm * vm) {
            early_fallback = true;
        } else {
            let mut ray_dir = None;
            // `ucomiss; jne`: NaN counts as zero
            if !(l != 0.0) && other.proto_type() == GeomType::Ray {
                {
                    let rd = other.proto_ray().expect("ray geometry").0.dir;
                    let r = &ctx.s[so].gt.r.m;
                    let rx = (rd.y * r[0][1] + rd.x * r[0][0]) + rd.z * r[0][2];
                    let ry = (rd.x * r[1][0] + rd.y * r[1][1]) + rd.z * r[1][2];
                    let rz = (rd.x * r[2][0] + rd.y * r[2][1]) + rd.z * r[2][2];
                    let (rx, ry, rz) = (f64::from(rx), f64::from(ry), f64::from(rz));
                    if (ry * navg[1] + rx * navg[0]) + rz * navg[2] > 0.0 {
                        ctx.next_area = c.area;
                        return 0;
                    }
                    let v173 = rx * navg[2] - rz * navg[0];
                    let v175 = rz * navg[1] - ry * navg[2];
                    let v176 = ry * navg[0] - rx * navg[1];
                    let v177 = ry * v175 - rx * v173;
                    let v178 = rz * v173 - ry * v176;
                    let v179 = rx * v176 - rz * v175;
                    let l = (v179 * v179 + v178 * v178) + v177 * v177;
                    let (x, y, z) = if l > f64::from(1e-20f32) {
                        let inv = 1.0 / l.sqrt();
                        (v178 * inv, inv * v179, inv * v177)
                    } else {
                        (0.0, 0.0, 1.0)
                    };
                    ray_dir = Some(Vec3::new(x as f32, y as f32, z as f32));
                }
            }
            let vel;
            if let Some(rd) = ray_dir {
                vel = 1.0f32;
                p.dir = rd;
            } else {
                vel = ((d.x * d.x + d.y * d.y) + d.z * d.z).sqrt();
                let inv = 1.0 / vel;
                p.dir = Vec3::new(d.x * inv, d.y * inv, d.z * inv);
            }
            p.vel = vel;
            p.tmax = vel * ctx.ip.time_interval;
            lim = f64::from(p.tmax);
        }
    }
    let omvd = other.proto_min_vtx_dist();
    p.min_pt_dist = ((mvd + omvd) - (mvd - omvd).abs()) * 0.5;
    let mut ip44 = ctx.ip.unproj_plane_normal;
    // 5./6. the unprojection loop
    let mut v654 = 0i32;
    let mut v166;
    let mut res = UnprojResult::default();
    let mut res2 = UnprojResult::default();
    let mut v165;
    let mut area;
    let mut go_fallback = early_fallback;
    let restore = |ctx: &mut IntersectCtx<'_>| {
        ctx.s[side].gt.r = saved.r;
        ctx.s[side].gt.offset = saved.offset;
    };
    let unproj_fn = |ctx: &IntersectCtx<'_>, mode: i32| -> UnprojFn {
        let t = |k: usize| GeomType::from_id(ctx.s[sx(k)].gt.prim_type).unwrap_or(GeomType::Box);
        ctx.unproj.get(mode as usize, t(0), t(1))
    };
    'outer: loop {
        if go_fallback {
            restore(ctx);
            let pool_pts = &mut ctx.arena.border_pts;
            let (dir, center) =
                fallback_direction(pool_pts, b, count, inters, &mut navg, pref, mvd);
            p.dir = Vec3::new(-dir.x, -dir.y, -dir.z);
            v654 = -1;
            p.mode = 0;
            p.vel = 0.0;
            ip44 = Vec3::ZERO;
            p.tmax = ctx.ip.max_unproj;
            lim = f64::from(p.tmax);
            c.center = center;
        }
        let v167;
        loop {
            // loop top (0x351D53B1)
            if p.mode == 0 && len2_ip44(ip44) > 0.0 {
                let dd = p.dir;
                let cdot = (ip44.y * dd.y + ip44.x * dd.x) + ip44.z * dd.z;
                if !(cdot.abs() >= f32::from_bits(0x3F34_FDF4)) {
                    let z = dd.z - cdot * ip44.z;
                    let x = dd.x - cdot * ip44.x;
                    let y = dd.y - cdot * ip44.y;
                    let l = (x * x + y * y) + z * z;
                    p.dir = if l > 1e-20 {
                        let inv = 1.0 / l.sqrt();
                        Vec3::new(x * inv, y * inv, z * inv)
                    } else {
                        Vec3::new(0.0, 0.0, 1.0)
                    };
                }
            }
            // the longest border segment's pair
            let bp = &ctx.arena.border_pts[b..b + count as usize];
            let last = count as usize - 1;
            let seg = |i: usize, j: usize| {
                let (u, v) = (bp[i], bp[j]);
                let (x, y, z) = (u.x - v.x, u.y - v.y, u.z - v.z);
                (y * y + x * x) + z * z
            };
            let mut best = seg(0, last);
            let mut bi = last;
            for i in 0..last {
                let d = seg(i, i + 1);
                if d > best {
                    best = d;
                    bi = i;
                }
            }
            let pair = ctx.arena.border_idx[b + bi];
            let t0 = pair[0] & IDX_MASK;
            cur[0] = CurPrim {
                prim: Primitive::Triangle(m.triangle_in_gtest(t0 as usize, &ctx.s[side].gt)),
                idx: t0,
            };
            if pair[1] >= 0 {
                let t1 = pair[1] & IDX_MASK;
                let mut tri = None;
                let gt_o = ctx.s[so].gt.clone();
                other.proto_with_mesh(&mut |om: &TriMesh| {
                    tri = Some(om.triangle_in_gtest(t1 as usize, &gt_o));
                });
                cur[1] = CurPrim {
                    prim: Primitive::Triangle(tri.expect("slot 54: other side is not a mesh")),
                    idx: t1,
                };
            }
            v166 = 0.0;
            res.pt = p.center;
            let sel = {
                let (wa, wb) = (ctx.s[side].gt.w, ctx.s[so].gt.w);
                let d = ((wb.x * wb.x + wb.y * wb.y) + wb.z * wb.z)
                    - ((wa.x * wa.x + wa.y * wa.y) + wa.z * wa.z);
                if d.to_bits() >> 31 != 0 {
                    so
                } else {
                    side
                }
            };
            res.n = ctx.s[sel].gt.unproj_dir;
            res.cos_t = 1.0;
            res2.cos_t = 1.0;
            let f = unproj_fn(ctx, p.mode);
            let ok = f(&mut p, &cur[0].prim, -1, &cur[1].prim, -1, &mut res, None);
            let iter_v167;
            if ok != 0 && res.t > 0.0 {
                if p.mode != 0 {
                    res.t = atan2(res.t, res.cos_t);
                }
                let mut n3 = 0i32;
                let mut exceeded = false;
                if res.t > 0.0 {
                    while n3 < 3 {
                        v166 += res.t;
                        motion::apply(v166, &p, &saved, &mut ctx.s[side].gt);
                        if v166 > lim {
                            exceeded = true;
                            break;
                        }
                        c.i_prim[0] = cur[0].idx;
                        c.i_prim[1] = cur[1].idx;
                        let la = this.proto_unproj_candidates(ctx, side, 0, &res, &cur[0]);
                        let lb = other.proto_unproj_candidates(ctx, so, 1, &res, &cur[1]);
                        res.t = 0.0;
                        let (mut bi, mut bj) = (0usize, 0usize);
                        for i in 0..la.n.max(0) as usize {
                            for j in 0..lb.n.max(0) as usize {
                                let f = unproj_fn(ctx, p.mode);
                                let r = f(
                                    &mut p,
                                    &la.prims[i].prim,
                                    la.feats[i],
                                    &lb.prims[j].prim,
                                    lb.feats[j],
                                    &mut res2,
                                    None,
                                );
                                if r != 0 && res2.t > res.t {
                                    res = res2;
                                    c.id = [i32::from(la.ids[i]), i32::from(lb.ids[j])];
                                    bi = i;
                                    bj = j;
                                }
                            }
                        }
                        cur[0] = if la.n > 0 { la.prims[bi] } else { la.base };
                        cur[1] = if lb.n > 0 { lb.prims[bj] } else { lb.base };
                        let step = if p.mode != 0 {
                            res.t = atan2(res.t, res.cos_t);
                            (res.t - 0.001) as f32
                        } else {
                            (res.t - f64::from(mvd)) as f32
                        };
                        let neg = (step.to_bits() >> 31) as i32;
                        n3 = neg + (n3 & -neg);
                        if !(res.t > 0.0) {
                            break;
                        }
                    }
                }
                if exceeded {
                    v165 = 1;
                    iter_v167 = v654;
                    area = AreaKind::None;
                } else {
                    area = if c.area.is_some() {
                        detect_area(ctx, side, cos_gap, sin2_gap)
                    } else {
                        AreaKind::None
                    };
                    iter_v167 = v654;
                    v165 = 0;
                }
            } else {
                area = AreaKind::None;
                let cell_none = std::ptr::eq(f as *const (), unproject_none as *const ());
                if count > 3 || inters.unk_84 != 0 || cell_none {
                    iter_v167 = v654;
                    if v654 == 0 {
                        go_fallback = true;
                        continue 'outer;
                    }
                } else {
                    iter_v167 = v654;
                }
                v165 = 1;
            }
            if iter_v167 != 0 {
                if iter_v167 == 1 {
                    go_fallback = true;
                    continue 'outer;
                }
                v167 = iter_v167;
                break;
            }
            if p.mode != 1 {
                v167 = 0;
                break;
            }
            let (ax, cen, pt) = (p.dir, p.center, res.pt);
            let (dx, dy, dz) = (pt.x - cen.x, pt.y - cen.y, pt.z - cen.z);
            let x5 = ax.x * dz - ax.z * dx;
            let x4 = dy * ax.z - dz * ax.y;
            let x9 = ax.y * dx - ax.x * dy;
            let ma = ctx.ip.min_axis_dist;
            if (x5 * x5 + x4 * x4) + x9 * x9 >= ma * ma {
                v167 = 0;
                break;
            }
            // redo linearly along −n
            p.mode = 0;
            let n = res.n;
            p.dir = Vec3::new(-n.x, -n.y, -n.z);
            let vel = rel_vel_along(ctx, side, so, pt, p.dir);
            p.vel = vel;
            p.tmax = ctx.ip.max_unproj;
            lim = f64::from(p.tmax);
            v654 = -1;
            restore(ctx);
        }
        if v166 <= lim {
            break 'outer;
        }
        if v167 == -1 {
            break 'outer;
        }
        go_fallback = true;
    }
    // final contact
    if !other_mesh {
        c.i_prim[1] = 0;
    }
    let mut kind = BorderKind::Pool;
    if v165 != 0 {
        c.t = f64::from_bits(0x3F1A_36E2_EB1C_432D);
        c.pt = ctx.arena.border_pts[b];
        c.n = Vec3::new(navg[0] as f32, navg[1] as f32, navg[2] as f32);
        c.i_feature = [
            i32::from(inters.i_feature[0][0]),
            i32::from(inters.i_feature[0][1]),
        ];
        c.dir = Vec3::new(-navg[0] as f32, -navg[1] as f32, -navg[2] as f32);
        c.vel = -1.0;
        c.area = None;
        c.i_unproj_mode = 0;
    } else {
        c.t = v166;
        c.pt = res.pt;
        c.i_feature = res.i_feature;
        let n = res.n;
        let l = (n.x * n.x + n.y * n.y) + n.z * n.z;
        c.n = if l > 1e-20 {
            let inv = 1.0 / l.sqrt();
            Vec3::new(n.x * inv, n.y * inv, n.z * inv)
        } else {
            Vec3::new(0.0, 0.0, 1.0)
        };
        c.dir = p.dir;
        c.i_unproj_mode = p.mode;
        c.vel = p.vel;
        if area == AreaKind::None {
            c.area = None;
        } else {
            patch::build_area(ctx, this, other, side, area, &mut c);
        }
    }
    let mut nb = count as usize;
    if !inters.border.is_empty() {
        let a = &mut ctx.arena;
        ensure(
            &mut a.border_pts,
            &mut a.border_idx,
            b + nb + inters.border.len(),
        );
        for q in &inters.border {
            a.border_pts[b + nb] = *q;
            nb += 1;
        }
        a.w16ec8 = u64::from((a.w16ec8 as u32).wrapping_add(inters.border.len() as u32));
        c.border_consecutive = false;
    } else if inters.unk_84 > 100 && i104 == 0 {
        nb = 1;
        kind = BorderKind::Center;
    }
    c.border = if kind == BorderKind::Center {
        vec![c.center]
    } else {
        ctx.arena.border_pts[b..b + nb].to_vec()
    };
    {
        let a = &mut ctx.arena;
        ensure(&mut a.border_pts, &mut a.border_idx, b + nb);
        c.idx_border = a.border_idx[b..b + nb].to_vec();
    }
    restore(ctx);
    ctx.push_contact_pool(c, kind, b);
    let n = ctx.n_contacts();
    if n >= ctx.s[side].gt.n_max_contacts || ctx.first_only || ctx.ip_convex != 0 {
        ctx.s[side].gt.stop = 1;
    } else {
        let mut na = ctx.arena.peek_area(ctx.ip.no_area_contacts);
        if let Some(a) = na.as_mut() {
            let (x, y) = (ctx.s[side].gt.min_pt_dist, ctx.s[so].gt.min_pt_dist);
            a.min_edge = ((x + y) - (x - y).abs()) * 0.5;
        }
        ctx.next_area = na;
    }
    1
}

fn len2_ip44(v: Vec3) -> f32 {
    (v.y * v.y + v.x * v.x) + v.z * v.z
}

/// The relative velocity at `pt` along `dir` (the rotational → linear switch):
/// `((vB + wB×(pt−comB)) − vA − wA×(pt−comA))·dir`, in the binary's operation order.
fn rel_vel_along(ctx: &IntersectCtx<'_>, side: usize, so: usize, pt: Vec3, dir: Vec3) -> f32 {
    let (a, b) = (&ctx.s[side].gt, &ctx.s[so].gt);
    let (wa, ca) = (a.w, a.center_of_mass);
    let (x4, x7, x2) = (pt.x - ca.x, pt.y - ca.y, pt.z - ca.z);
    let ax7 = x7 * wa.x - wa.y * x4;
    let ax5 = x2 * wa.y - x7 * wa.z;
    let ax6 = wa.z * x4 - x2 * wa.x;
    let (wb, cb) = (b.w, b.center_of_mass);
    let bx = pt.x - cb.x;
    let (y15, z13) = (pt.y - cb.y, pt.z - cb.z);
    let x3 = ((z13 * wb.y - y15 * wb.z) + b.v.x - a.v.x) - ax5;
    let x4b = ((wb.z * bx - z13 * wb.x) + b.v.y - a.v.y) - ax6;
    let x15 = ((y15 * wb.x - wb.y * bx) + b.v.z - a.v.z) - ax7;
    (x4b * dir.y + x3 * dir.x) + x15 * dir.z
}

/// The fallback direction and centre (0x351D650D…0x351D6E9C); `navg` is replaced by the pair's
/// normal in the first case, as in the original.
fn fallback_direction(
    pool: &mut Vec<Vec3>,
    b: usize,
    count: i32,
    inters: &PrimInters,
    navg: &mut [f64; 3],
    pref: Vec3,
    mvd: f32,
) -> (Vec3, Vec3) {
    let n = count as usize;
    if inters.unk_84 > count {
        let v = inters.n;
        *navg = [f64::from(v.x), f64::from(v.y), f64::from(v.z)];
        return (v, inters.n_alt);
    }
    if count < 3 {
        let (p0, p1) = (pool[b], pool[b + 1]);
        let c = Vec3::new(
            (p0.x + p1.x) * 0.5,
            (p1.y + p0.y) * 0.5,
            (p1.z + p0.z) * 0.5,
        );
        return (pref, c);
    }
    if pool.len() < b + n + 1 {
        pool.resize(b + n + 1, Vec3::ZERO);
    }
    pool[b + n] = pool[b];
    let bp = &pool[b..=b + n];
    let mut seg = vec![0f32; n];
    let (mut per, mut sx, mut sy, mut sz) = (0f64, 0f64, 0f64, 0f64);
    for i in 0..n {
        let (u, v) = (bp[i], bp[i + 1]);
        let (dx, dy, dz) = (v.x - u.x, v.y - u.y, v.z - u.z);
        let l = ((dy * dy + dx * dx) + dz * dz).sqrt();
        seg[i] = l;
        per += f64::from(l);
        sx += f64::from((v.x + u.x) * l);
        sy += f64::from((u.y + v.y) * l);
        sz += f64::from((u.z + v.z) * l);
    }
    let (cx, cy, cz) = if per > 0.0 {
        let k = 1.0 / (per * 2.0);
        (sx * k, sy * k, sz * k)
    } else {
        (f64::from(bp[0].x), f64::from(bp[0].y), f64::from(bp[0].z))
    };
    let mut mm = [0f64; 9];
    let (mut nx, mut ny, mut nz) = (0f64, 0f64, 0f64);
    for i in 0..n {
        let red = |q: Vec3| {
            [
                f64::from((f64::from(q.x) - cx) as f32),
                f64::from((f64::from(q.y) - cy) as f32),
                f64::from((f64::from(q.z) - cz) as f32),
            ]
        };
        let a = red(bp[i]);
        let c2 = red(bp[i + 1]);
        nx += c2[2] * a[1] - c2[1] * a[2];
        ny += a[2] * c2[0] - c2[2] * a[0];
        nz += c2[1] * a[0] - a[1] * c2[0];
        let s = f64::from(seg[i]);
        for k in 0..3 {
            let (u, v) = (c2[k], a[k]);
            for j in 0..3 {
                let e = (((u * c2[j] + v * a[j]) * 2.0 + v * c2[j]) + u * a[j]) * s;
                mm[3 * k + j] += e;
            }
        }
    }
    let center = Vec3::new(cx as f32, cy as f32, cz as f32);
    let area2 = (ny * ny + nx * nx) + nz * nz;
    if !(area2 >= f64::from(mvd * mvd)) {
        return (pref, center);
    }
    const INC: [usize; 3] = [1, 2, 0];
    const DEC: [usize; 3] = [2, 0, 1];
    let mut best = 0f64;
    let mut kb = 3usize;
    for k in 0..3 {
        let (i, j) = (INC[k], DEC[k]);
        let det = mm[4 * i] * mm[4 * j] - mm[i + 3 * j] * mm[3 * i + j];
        if det.abs() > best.abs() {
            best = det;
            kb = k;
        }
    }
    if !(best.abs() >= 1e-20) {
        return (
            Vec3::new(navg[0] as f32, navg[1] as f32, navg[2] as f32),
            center,
        );
    }
    let k = kb;
    let (i, j) = (INC[k], DEC[k]);
    let inv = 1.0 / best;
    let mut v = [0f32; 3];
    v[k] = 1.0;
    v[i] = (-((mm[3 * i + k] * mm[4 * j] - mm[3 * j + k] * mm[3 * i + j]) * inv)) as f32;
    v[j] = (-((mm[3 * j + k] * mm[4 * i] - mm[3 * i + k] * mm[3 * j + i]) * inv)) as f32;
    let l = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
    let mut d = if l > 1e-20 {
        let s = 1.0 / l.sqrt();
        Vec3::new(v[0] * s, v[1] * s, v[2] * s)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    };
    let t = (mvd * 10.0) * (mvd * 10.0);
    let refn = if area2 >= f64::from(t * t) {
        [nx, ny, nz]
    } else {
        *navg
    };
    let dot = ((refn[1] as f32) * d.y + (refn[0] as f32) * d.x) + (refn[2] as f32) * d.z;
    if !(dot >= 0.0) {
        d = Vec3::new(-d.x, -d.y, -d.z);
    }
    (d, center)
}

/// The area-contact detection after the unprojection (0x351D5CFC…0x351D601B).
fn detect_area(ctx: &IntersectCtx<'_>, side: usize, cos_gap: f32, sin2: f32) -> AreaKind {
    let so = side ^ 1;
    let (fa, fb) = (&ctx.s[side].gt.face_cands, &ctx.s[so].gt.face_cands);
    let mc = -cos_gap;
    for (i, a) in fa.iter().enumerate() {
        for (j, o) in fb.iter().enumerate() {
            let d = (o.n.x * a.n.x + o.n.y * a.n.y) + o.n.z * a.n.z;
            if !(d >= mc) {
                return AreaKind::FaceFace(i, j);
            }
        }
    }
    for k in 0..2usize {
        let (sf, se) = if k == 0 { (side, so) } else { (so, side) };
        let faces = &ctx.s[sf].gt.face_cands;
        let edges = &ctx.s[se].gt.edge_cands;
        for (i, f) in faces.iter().enumerate() {
            let fnv = f.n;
            for (j, e) in edges.iter().enumerate() {
                let dv = e.dir;
                let d = (dv.y * fnv.y + dv.x * fnv.x) + dv.z * fnv.z;
                let l = ((dv.y * dv.y + dv.x * dv.x) + dv.z * dv.z) * sin2;
                if d * d >= l {
                    continue;
                }
                let n0 = e.n[0];
                let d0 = (n0.y * fnv.y + n0.x * fnv.x) + n0.z * fnv.z;
                let l0 = -(((n0.x * n0.x + n0.y * n0.y) + n0.z * n0.z) * sin2);
                if !(d0.abs() * d0 > l0) {
                    continue;
                }
                let n1 = e.n[1];
                let d1 = (n1.y * fnv.y + n1.x * fnv.x) + n1.z * fnv.z;
                let l1 = -(((n1.x * n1.x + n1.y * n1.y) + n1.z * n1.z) * sin2);
                if d1.abs() * d1 > l1 {
                    return AreaKind::FaceEdge(k, i, j);
                }
            }
        }
    }
    AreaKind::None
}
