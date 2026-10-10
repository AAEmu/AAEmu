//! The protocol slots of `CTriMesh` (also used by `CHeightfield` and `CVoxelGeom` where their
//! vtables point here): 53 PrepareForIntersectionTest 0x351D3620, 55 Cleanup 0x351BDF60,
//! 56 GetPrimitiveList 0x351C3C60 (+ sub_351C27A0 GetTriangleWorld). Slot 54 is
//! [`crate::geometry::trimesh::register`].

use super::{BvRef, GeomProtocol, IntersectCtx};
use crate::collision::contact::PrimInters;
use crate::geometry::bvtree::{BvPrim, BvRecord};
use crate::geometry::primitives::{Primitive, Triangle};
use crate::geometry::shapes::protocol::GeomUnderTest;
use crate::geometry::trimesh::TriMesh;
use crate::ids::GeomType;
use crate::math::Vec3;

/// A BV record as the primitive the OVERLAP / INTERSECT cells receive.
#[must_use]
pub fn bv_record_prim(r: &BvRecord) -> Primitive {
    match r.prim {
        BvPrim::Box(b) => Primitive::Box(b),
        BvPrim::Ray(ray) => Primitive::Ray(ray),
        BvPrim::Heightfield(h) => Primitive::Heightfield(h),
    }
}

impl TriMesh {
    /// The local triangle `i` (vertices and stored normal).
    #[must_use]
    pub fn local_triangle(&self, i: usize) -> Triangle {
        let v = |k: usize| self.vertices[self.indices[3 * i + k] as usize];
        Triangle {
            pt: [v(0), v(1), v(2)],
            n: self.normals[i],
        }
    }

    /// sub_351C27A0 `GetTriangleWorld` (inferred name): triangle `i` placed by the gtest
    /// (`(R·v)·s + off`, normal `R·n`).
    #[must_use]
    pub fn triangle_in_gtest(&self, i: usize, gt: &GeomUnderTest) -> Triangle {
        let t = self.local_triangle(i);
        Triangle {
            pt: [gt.point(t.pt[0]), gt.point(t.pt[1]), gt.point(t.pt[2])],
            n: gt.r.mul_vec(t.n),
        }
    }

    /// Triangle `i` in the other side's frame (`R_rel`, `scale_rel`, `offset_rel`).
    fn triangle_rel(&self, i: usize, gt: &GeomUnderTest) -> Triangle {
        let t = self.local_triangle(i);
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

    /// The id slot 56 gives triangle `i`: its material, or −2 (a local char) without materials.
    #[must_use]
    pub fn prim_id(&self, i: usize) -> i8 {
        self.mats.as_ref().map_or(-2, |m| m[i])
    }
}

/// `((id + 1) ^ id) >> 31` is set only for id −1: such triangles are skipped.
#[inline]
fn id_ok(id: i8) -> bool {
    let id = i32::from(id);
    ((id + 1) ^ id) >= 0
}

impl GeomProtocol for TriMesh {
    fn proto_type(&self) -> GeomType {
        GeomType::TriMesh
    }

    fn proto_min_vtx_dist(&self) -> f32 {
        self.min_vtx_dist
    }

    fn proto_priority(&self) -> i32 {
        self.priority
    }

    fn proto_flag24(&self) -> i32 {
        self.is_convex
    }

    fn proto_bv_tree(&self) -> BvRef<'_> {
        BvRef::Borrowed(
            self.tree
                .as_ref()
                .expect("CTriMesh without a BV tree")
                .as_dyn(),
        )
    }

    /// Slot 53 (0x351D3620): BV slot 6; primitive type 1; `+304` = the tree's MaxPrimsInNode
    /// (slot 3); `+296`/`+312`/`+336`/`+344` sized by the maximum vertex valency (`+168`),
    /// `+308` = valency, ids for `max(MaxPrimsInNode, valency)`, stride 52; `minPtDist` 0; the
    /// arena word +0x16EC8 is cleared unless previous contacts are kept, and copied to
    /// +0x16ECC.
    fn proto_prepare(&self, ctx: &mut IntersectCtx<'_>, side: usize, keep_prev: bool) -> i32 {
        let tref = self.proto_bv_tree();
        let tree = tref.tree();
        {
            let sd = &mut ctx.s[side];
            tree.prepare_for_intersection_test(&mut sd.bvs, &mut ctx.arena.bv);
        }
        let nmax = tree.max_prims_in_node();
        let val = self.max_vertex_valency;
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

    /// Slot 55 (0x351BDF60): BV slot 7; the buffers are freed.
    fn proto_cleanup(&self, ctx: &mut IntersectCtx<'_>, side: usize) {
        let tref = self.proto_bv_tree();
        let sd = &mut ctx.s[side];
        tref.tree()
            .cleanup_after_intersection_test(&mut sd.bvs, &mut ctx.arena.bv);
    }

    /// Slot 56 (0x351C3C60). For each triangle of `[i_start, i_start + n_prims)` (clamped to
    /// nTris) — in the collider's frame: local if `b_local == 1`, the other side's frame
    /// (`R_rel`, `scale_rel`, `offset_rel`) if `b_local == 0` —
    /// `OVERLAP[colliderType][tri](collider, tri)` and the id (material, or −2 without
    /// materials; id −1 is skipped); the triangle is then stored in world space
    /// (sub_351C27A0) with its index and, when the other side has an outside pivot
    /// (`pivot.x < 1e10`), kept only if it faces away from it (`(pt0 − pivot)·n < 0`). Any other
    /// `b_local` stores every triangle in world space without tests (the sweep traversal passes
    /// −1). `+160 = 0`. Returns the number kept.
    fn proto_primitive_list(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        i_start: i32,
        n_prims: i32,
        collider: &BvRecord,
        b_local: i32,
    ) -> i32 {
        let nt = self.n_tris;
        let end = i_start + n_prims;
        let d = nt - end;
        let end = end + (d & (d >> 31));
        let pivot = ctx.s[side ^ 1].gt.pt_outside_pivot;
        let has_pivot = ((pivot.x - 1e10).to_bits() >> 31) != 0;
        let ovl = ctx.tables.overlap_fn(
            GeomType::from_id(collider.kind).unwrap_or(GeomType::Box),
            GeomType::TriMesh,
        );
        let cprim = bv_record_prim(collider);
        let sd = &mut ctx.s[side];
        let gt = &mut sd.gt;
        gt.prims.clear();
        sd.prim_idx.clear();
        let span = (end - i_start).max(0) as usize;
        if gt.ids.len() < span {
            gt.ids.resize(span, 0);
        }
        let mut count = 0usize;
        if b_local == 1 || b_local == 0 {
            for i in i_start..end {
                let iu = i as usize;
                let tri = if b_local == 1 {
                    self.local_triangle(iu)
                } else {
                    self.triangle_rel(iu, gt)
                };
                let id = self.prim_id(iu);
                gt.ids[count] = id;
                let ov = ovl(&cprim, &Primitive::Triangle(tri));
                let mut keep = ov && id_ok(id);
                let w = self.triangle_in_gtest(iu, gt);
                if has_pivot {
                    let p = w.pt[0];
                    let a = ((p.y - pivot.y) * w.n.y + w.n.x * (p.x - pivot.x))
                        + (p.z - pivot.z) * w.n.z;
                    keep &= (a.to_bits() >> 31) != 0;
                }
                if gt.prims.len() == count {
                    gt.prims.push(Primitive::Triangle(w));
                    sd.prim_idx.push(i);
                } else {
                    gt.prims[count] = Primitive::Triangle(w);
                    sd.prim_idx[count] = i;
                }
                count += usize::from(keep);
            }
            gt.prims.truncate(count);
            sd.prim_idx.truncate(count);
        } else {
            for i in i_start..end {
                let w = self.triangle_in_gtest(i as usize, gt);
                gt.prims.push(Primitive::Triangle(w));
                sd.prim_idx.push(i);
            }
            count = span;
        }
        gt.transform_prims = 0;
        count as i32
    }

    fn proto_register(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        other: &dyn GeomProtocol,
        i_this: usize,
        i_other: usize,
        inters: &mut PrimInters,
    ) -> i32 {
        crate::geometry::trimesh::register::mesh_register_intersection(
            self, self, ctx, side, other, i_this, i_other, inters,
        )
    }

    fn proto_with_mesh(&self, f: &mut dyn FnMut(&TriMesh)) -> bool {
        f(self);
        true
    }
}
