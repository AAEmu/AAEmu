//! `CHeightfield` in the generic `CGeometry::Intersect` ([`crate::geometry::trimesh::intersect`]):
//! the [`GeomProtocol`] implementation and slot 10.
//!
//! Slot 53 rebuilds the region mesh, so the protocol needs to mutate the geometry through
//! `&self`: [`SharedHeightfield`] wraps the [`Heightfield`] in a `RwLock` (the original guards
//! the same writes with the geometry's spin lock, slots 4/5).
//!
//! | slot | x64 | port |
//! |---|---|---|
//! | 6 GetBBox | 0x35103340 → BV 2 | [`GeomProtocol::proto_bbox`] |
//! | 10 Intersect | 0x35156E20 | [`SharedHeightfield::intersect`] (ray → [`Heightfield::intersect_ray`], else the generic `Intersect`) |
//! | 46 GetBVTree | 0x350FE3A0 | [`GeomProtocol::proto_bv_tree`] (a shallow snapshot of the BV sharing the used-triangle mask) |
//! | 49/57/58/59 | `CTriMesh`'s | forwarded to the region [`TriMesh`] |
//! | 53 Prepare | 0x35155180 (+ `CTriMesh` 0x351D3620) | [`GeomProtocol::proto_prepare`] |
//! | 54 Register | `CTriMesh` 0x351D3950 | [`crate::geometry::trimesh::register::mesh_register_intersection`] on the region mesh |
//! | 55 Cleanup | `CTriMesh` 0x351BDF60 → BV 7 (nop) | nop |
//! | BV 23 → 56 | 0x351546A0 → `CTriMesh` 0x351C3C60 | [`GeomProtocol::proto_node_contents`] |

use std::sync::RwLock;

use crate::collision::contact::{IntersectionParams, PrimInters, UnprojResult};
use crate::geometry::bvtree::{BvPrim, BvRecord, BvTree};
use crate::geometry::primitives::{BoxPrim, Primitive, Triangle};
use crate::geometry::shapes::protocol::{
    ContactFrame, FeaturePolygon, GeomUnderTest, IntersectionProtocol, SphereUnprojectCallees,
};
use crate::geometry::shapes::RayGeom;
use crate::geometry::trimesh::intersect::{intersect, IntersectExtra, IntersectResult, Tables};
use crate::geometry::trimesh::protocol::mesh::bv_record_prim;
use crate::geometry::trimesh::protocol::{
    BorderKind, BvRef, CallerArena, GeomProtocol, IntersectCtx,
};
use crate::geometry::trimesh::TriMesh;
use crate::geometry::GeomWorldData;
use crate::ids::GeomType;
use crate::math::Vec3;

use super::ray::RayIntersectInput;
use super::region::{HfFrame, OtherSweep, RegionOutcome};
use super::Heightfield;

/// A [`Heightfield`] usable through `&self` by the intersection code.
#[derive(Debug, Default)]
pub struct SharedHeightfield(pub RwLock<Heightfield>);

impl SharedHeightfield {
    #[must_use]
    pub fn new(hf: Heightfield) -> Self {
        Self(RwLock::new(hf))
    }

    fn rd(&self) -> std::sync::RwLockReadGuard<'_, Heightfield> {
        self.0
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }

    fn wr(&self) -> std::sync::RwLockWriteGuard<'_, Heightfield> {
        self.0
            .write()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }
}

/// The other geometry of slot 10.
#[derive(Clone, Copy, Debug)]
pub enum HfOther<'a> {
    /// `GetType() == 3`: the heightfield's own ray branch.
    Ray(&'a RayGeom),
    /// Anything else: the generic `Intersect`.
    Geom(&'a dyn GeomProtocol),
}

impl SharedHeightfield {
    /// Slot 10 (0x35156E20). A ray takes [`Heightfield::intersect_ray`] (one contact at most,
    /// border pointing at the contact's `center`); any other geometry goes to `CTriMesh` slot 10
    /// (0x351C9910), which for a non-ray is the generic `CGeometry::Intersect` with this
    /// heightfield as A.
    #[allow(clippy::too_many_arguments)]
    pub fn intersect(
        &self,
        tables: Tables<'_>,
        arena: &mut CallerArena,
        other: HfOther<'_>,
        gwd_hf: &mut GeomWorldData,
        gwd_other: Option<&GeomWorldData>,
        ip: Option<&mut IntersectionParams>,
        extra: IntersectExtra,
        draw_ray_limit: Option<i32>,
    ) -> IntersectResult {
        match other {
            HfOther::Ray(ray) => {
                let input = RayIntersectInput {
                    ray,
                    gwd_hf,
                    gwd_ray: gwd_other,
                    ip: ip.as_deref(),
                    draw_ray_limit,
                };
                let c = self.rd().intersect_ray(&input, tables.unproj, arena);
                let mut r = IntersectResult::default();
                if let Some(c) = c {
                    r.contacts.push(c);
                    r.border_kinds.push(BorderKind::Center);
                }
                r
            }
            HfOther::Geom(g) => {
                intersect(tables, arena, self, g, Some(gwd_hf), gwd_other, ip, extra)
            }
        }
    }
}

/// The record fields slots 53 / 23 read, from a side's gtest.
fn frame_of(gt: &GeomUnderTest) -> HfFrame {
    HfFrame {
        stop: gt.stop,
        r: gt.r,
        r_rel: gt.r_rel,
        offset: gt.offset,
        offset_rel: gt.offset_rel,
        scale_rel_r: gt.scale_rel_r,
    }
}

/// `((id + 1) ^ id) >> 31` is set only for id −1: such triangles are skipped.
#[inline]
fn id_ok(id: i8) -> bool {
    let id = i32::from(id);
    ((id + 1) ^ id) >= 0
}

/// Triangle `i` in the other side's frame (`R_rel`, `scale_rel`, `offset_rel`), as `CTriMesh`
/// slot 56 places it.
fn triangle_rel(m: &TriMesh, i: usize, gt: &GeomUnderTest) -> Triangle {
    let t = m.local_triangle(i);
    let r = &gt.r_rel.m;
    let s = gt.scale_rel;
    let o = gt.offset_rel;
    let place = |p: Vec3| {
        Vec3::new(
            ((r[0][0] * p.x + r[0][1] * p.y) + r[0][2] * p.z) * s + o.x,
            ((r[1][0] * p.x + r[1][1] * p.y) + r[1][2] * p.z) * s + o.y,
            ((r[2][0] * p.x + r[2][1] * p.y) + r[2][2] * p.z) * s + o.z,
        )
    };
    let n = t.n;
    Triangle {
        pt: [place(t.pt[0]), place(t.pt[1]), place(t.pt[2])],
        n: Vec3::new(
            (r[0][0] * n.x + r[0][1] * n.y) + r[0][2] * n.z,
            (r[1][0] * n.x + r[1][1] * n.y) + r[1][2] * n.z,
            (r[2][0] * n.x + r[2][1] * n.y) + r[2][2] * n.z,
        ),
    }
}

/// `CTriMesh` slot 56 (0x351C3C60) on the region mesh, writing from entry `base` of the
/// side's buffers on (BV slot 23 advances the buffers by the earlier calls' counts). Same rules
/// as the mesh's `proto_primitive_list`.
#[allow(clippy::too_many_arguments)]
fn primitive_list_at(
    m: &TriMesh,
    ctx: &mut IntersectCtx<'_>,
    side: usize,
    i_start: i32,
    n_prims: i32,
    collider: &BvRecord,
    b_local: i32,
    base: usize,
) -> i32 {
    let nt = m.n_tris;
    let end = i_start.wrapping_add(n_prims);
    let d = nt.wrapping_sub(end);
    let end = end.wrapping_add(d & (d >> 31));
    let pivot = ctx.s[side ^ 1].gt.pt_outside_pivot;
    let has_pivot = ((pivot.x - 1e10).to_bits() >> 31) != 0;
    let ovl = ctx.tables.overlap_fn(
        GeomType::from_id(collider.kind).unwrap_or(GeomType::Box),
        GeomType::TriMesh,
    );
    let cprim = bv_record_prim(collider);
    let sd = &mut ctx.s[side];
    let gt = &mut sd.gt;
    gt.prims.truncate(base);
    sd.prim_idx.truncate(base);
    let span = (end - i_start).max(0) as usize;
    if gt.ids.len() < base + span {
        gt.ids.resize(base + span, 0);
    }
    let mut count = 0usize;
    if b_local == 1 || b_local == 0 {
        for i in i_start..end {
            let iu = i as usize;
            let tri = if b_local == 1 {
                m.local_triangle(iu)
            } else {
                triangle_rel(m, iu, gt)
            };
            let id = m.prim_id(iu);
            gt.ids[base + count] = id;
            let ov = ovl(&cprim, &Primitive::Triangle(tri));
            let mut keep = ov && id_ok(id);
            let w = m.triangle_in_gtest(iu, gt);
            if has_pivot {
                let p = w.pt[0];
                let a =
                    ((p.y - pivot.y) * w.n.y + w.n.x * (p.x - pivot.x)) + (p.z - pivot.z) * w.n.z;
                keep &= (a.to_bits() >> 31) != 0;
            }
            if gt.prims.len() == base + count {
                gt.prims.push(Primitive::Triangle(w));
                sd.prim_idx.push(i);
            } else {
                gt.prims[base + count] = Primitive::Triangle(w);
                sd.prim_idx[base + count] = i;
            }
            count += usize::from(keep);
        }
        gt.prims.truncate(base + count);
        sd.prim_idx.truncate(base + count);
    } else {
        for i in i_start..end {
            let w = m.triangle_in_gtest(i as usize, gt);
            gt.prims.push(Primitive::Triangle(w));
            sd.prim_idx.push(i);
            count += 1;
        }
    }
    gt.transform_prims = 0;
    count as i32
}

impl GeomProtocol for SharedHeightfield {
    /// The region mesh (`CTriMesh` slots 57/58/59 read the object's mesh fields, which are
    /// the region mesh's). (Added by P1c-register.)
    fn proto_with_mesh(&self, f: &mut dyn FnMut(&TriMesh)) -> bool {
        f(&self.rd().mesh);
        true
    }

    fn proto_type(&self) -> GeomType {
        GeomType::Heightfield
    }

    fn proto_min_vtx_dist(&self) -> f32 {
        self.rd().mesh.min_vtx_dist
    }

    fn proto_priority(&self) -> i32 {
        self.rd().mesh.priority
    }

    fn proto_flag24(&self) -> i32 {
        self.rd().mesh.is_convex
    }

    /// Slot 46: the embedded BV (a snapshot; its slot 25 marks the geometry's own mask).
    fn proto_bv_tree(&self) -> BvRef<'_> {
        BvRef::Owned(Box::new(self.rd().bv.clone()))
    }

    /// Slot 6: BV slot 2 (the current region's box).
    fn proto_bbox(&self) -> BoxPrim {
        self.rd().bbox()
    }

    /// Slot 53 (0x35155180): [`Heightfield::prepare_region`] with the other side's box
    /// ([`IntersectCtx::bboxes`]) and sweep (`+248` local direction, `+260` world length as
    /// the flag, `+264` local length) and this side's record; then, unless rejected / stopped,
    /// `CTriMesh` slot 53 (0x351D3620) on the region mesh with the heightfield BV as its tree:
    /// BV slot 6 (base), primitive type 1, `+304 = 2·w·h` (BV slot 3), buffers sized by the
    /// valency 6, stride 52, `minPtDist` 0, the +0x16EC8 word handling.
    ///
    /// The original reads the other side's box at this point; the port takes it when the
    /// call starts (only differs for heightfield-vs-heightfield, where A's slot 53 changes A's
    /// box before B's slot 53 reads it).
    fn proto_prepare(&self, ctx: &mut IntersectCtx<'_>, side: usize, keep_prev: bool) -> i32 {
        let other = side ^ 1;
        let o = &ctx.s[other];
        let sweep = OtherSweep {
            dir: o.sweep_dir_loc,
            flag: o.sweep_len,
            len: o.sweep_len_loc,
        };
        let mut frame = frame_of(&ctx.s[side].gt);
        let obox = ctx.bboxes[other];
        let mut hf = self.wr();
        let r = hf.prepare_region(&obox, Some(sweep), Some(&mut frame));
        ctx.s[side].gt.offset = frame.offset;
        match r {
            RegionOutcome::Reject => return 0,
            RegionOutcome::Done => return 1,
            RegionOutcome::CallMeshSlot53 => {}
        }
        let hf = &*hf;
        {
            let sd = &mut ctx.s[side];
            hf.bv
                .prepare_for_intersection_test(&mut sd.bvs, &mut ctx.arena.bv);
        }
        let nmax = hf.bv.max_prims_in_node();
        let val = hf.mesh.max_vertex_valency;
        let nv = val.max(0) as usize;
        let sd = &mut ctx.s[side];
        let gt = &mut sd.gt;
        gt.prim_type = 1;
        gt.n_prims = nmax;
        gt.prims.clear();
        sd.prim_idx.clear();
        gt.unproj_prims = Some(vec![Primitive::Triangle(Triangle::default()); nv]);
        sd.unproj_idx = vec![0; nv];
        gt.n_unproj_prims = val;
        gt.aux_idx = vec![0; nv];
        gt.ids = vec![0; nmax.max(val).max(0) as usize];
        gt.prim_stride = 52;
        gt.cand_bufs = true;
        gt.face_cands = Vec::with_capacity(nv);
        gt.edge_cands = Vec::with_capacity(nv);
        gt.min_pt_dist = 0.0;
        if !keep_prev {
            ctx.arena.w16ec8 = 0;
        }
        ctx.arena.w16ecc = ctx.arena.w16ec8 as u32;
        1
    }

    /// Slot 55: BV slot 7 is a nop for the heightfield BV.
    fn proto_cleanup(&self, _ctx: &mut IntersectCtx<'_>, _side: usize) {}

    /// BV slot 23 (0x351546A0, [`super::HeightfieldBv::node_contents`]) and the slot-56 call
    /// of each row, appended one after the other.
    fn proto_node_contents(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        _i_node: i32,
        collider: &BvRecord,
        collider_used: i32,
        b_local: i32,
    ) -> i32 {
        let hf = self.rd();
        let frame = frame_of(&ctx.s[side].gt);
        let cbox = match collider.prim {
            BvPrim::Box(b) => b,
            _ => BoxPrim::default(),
        };
        let calls = hf.bv.node_contents(
            hf.tri_cap,
            collider.kind,
            &cbox,
            collider_used != 0,
            b_local,
            &frame,
        );
        ctx.s[side].gt.prims.clear();
        ctx.s[side].prim_idx.clear();
        let mut total = 0usize;
        for c in calls {
            let n = primitive_list_at(
                &hf.mesh,
                ctx,
                side,
                c.first_prim,
                c.n_prims,
                collider,
                b_local,
                total,
            );
            total += n.max(0) as usize;
        }
        total as i32
    }

    fn proto_primitive_list(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        i_start: i32,
        n_prims: i32,
        collider: &BvRecord,
        b_local: i32,
    ) -> i32 {
        let hf = self.rd();
        primitive_list_at(&hf.mesh, ctx, side, i_start, n_prims, collider, b_local, 0)
    }

    /// Slot 54: the mesh's (0x351D3950) on the region mesh.
    fn proto_register(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        other: &dyn GeomProtocol,
        i_this: usize,
        i_other: usize,
        inters: &mut PrimInters,
    ) -> i32 {
        let hf = self.rd();
        crate::geometry::trimesh::register::mesh_register_intersection(
            &hf.mesh, self, ctx, side, other, i_this, i_other, inters,
        )
    }
}

/// Slots 49 / 57 / 58 / 59 are `CTriMesh`'s, run on the region mesh.
impl IntersectionProtocol for SharedHeightfield {
    fn unproject_sphere(
        &self,
        center: Vec3,
        r: f32,
        rsep: f32,
        out: &mut UnprojResult,
        callees: &mut dyn SphereUnprojectCallees,
    ) -> i32 {
        self.rd()
            .mesh
            .unproject_sphere(center, r, rsep, out, callees)
    }

    fn get_unprojection_candidates(
        &self,
        i_side: i32,
        contact: &UnprojResult,
        prim: &mut Primitive,
        i_prim: i32,
        gt: &mut GeomUnderTest,
    ) -> i32 {
        self.rd()
            .mesh
            .get_unprojection_candidates(i_side, contact, prim, i_prim, gt)
    }

    fn face_polygon(
        &self,
        frame: &mut ContactFrame,
        i_prim: i32,
        i_feature: i32,
        gt: &GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        self.rd()
            .mesh
            .face_polygon(frame, i_prim, i_feature, gt, out)
    }

    fn edge_segment(
        &self,
        frame: &mut ContactFrame,
        i_prim: i32,
        i_feature: i32,
        gt: &GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        self.rd()
            .mesh
            .edge_segment(frame, i_prim, i_feature, gt, out)
    }
}
