//! [`GeomProtocol`] for P1a's primitive geometry classes: slots 53/56/57/58/59 are their
//! [`IntersectionProtocol`] implementations; the BV is the embedded `CSingleBoxTree` (box,
//! sphere, cylinder, capsule) or `CRayBV` (ray), seen through P1b's tree types.
//!
//! Slot 54: the ray's own (0x3519A380, P1a [`ray_register_intersection`]); the solid
//! primitives use the `CGeometry` base (0x3514F3F0, P2b's
//! [`crate::collision::swept::sweep::register_intersection`]) plus the caller bookkeeping it
//! performs (count, next area, stop flag). The base is only reached when a solid primitive
//! has the lower priority of the pair, which no pair with a mesh or a heightfield produces
//! (they have priority 1, the primitives 3): the mesh / heightfield registers.

use super::{BorderKind, BvRef, GeomProtocol, IntersectCtx};
use crate::collision::contact::{GeomContact, PrimInters};
use crate::collision::swept::sweep::{
    next_area_min_edge, register_intersection, GeomTestMotion, StaleUnprojSlots,
};
use crate::geometry::bvtree::{BvRecord, RayBv, SingleBoxTree};
use crate::geometry::shapes::protocol::{ray_register_intersection, IntersectionProtocol};
use crate::geometry::shapes::{
    BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SingleBoxBv, SphereGeom,
};
use crate::ids::GeomType;

fn single_box(bv: &SingleBoxBv) -> SingleBoxTree {
    let mut t = SingleBoxTree::new(bv.box_, bv.n_prims);
    t.is_convex = bv.is_convex;
    t
}

/// The `CGeometry` base slot 54 (0x3514F3F0) with its caller-side bookkeeping, for a solid
/// primitive registering against `other`.
fn base_register(
    ctx: &mut IntersectCtx<'_>,
    side: usize,
    i_this: usize,
    i_other: usize,
    inters: &mut PrimInters,
) -> i32 {
    let motion = |s: &super::Side| GeomTestMotion {
        v: s.gt.v,
        w: s.gt.w,
        com: s.gt.center_of_mass,
        rot_center: s.gt.center_of_rotation,
        min_vtx_dist: s.gt.min_pt_dist,
    };
    let (ma, mb) = (motion(&ctx.s[side]), motion(&ctx.s[side ^ 1]));
    let pa = ctx.s[side].gt.prims[i_this];
    let pb = ctx.s[side ^ 1].gt.prims[i_other];
    let mut c = GeomContact {
        area: ctx.next_area.take(),
        ..GeomContact::default()
    };
    let ok = register_intersection(
        ctx.unproj,
        &pa,
        &ma,
        &pb,
        &mb,
        inters,
        &ctx.ip,
        &mut c,
        StaleUnprojSlots::default(),
    );
    if !ok {
        ctx.next_area = c.area;
        return 0;
    }
    c.i_prim = [-1, -1];
    if let Some(a) = c.area.as_ref() {
        ctx.arena.n_areas += 1;
        ctx.arena.n_area_pts += a.npt;
    }
    ctx.push_contact(c, BorderKind::Pt);
    let mut na = ctx.arena.peek_area(ctx.ip.no_area_contacts);
    if let Some(a) = na.as_mut() {
        a.min_edge = next_area_min_edge(&ma, &mb);
    }
    ctx.next_area = na;
    if ctx.n_contacts() >= ctx.s[side].gt.n_max_contacts {
        ctx.s[side].gt.stop = 1;
    }
    0
}

macro_rules! solid_protocol {
    ($ty:ty, $gt:expr, $bv:ident) => {
        impl GeomProtocol for $ty {
            fn proto_type(&self) -> GeomType {
                $gt
            }
            fn proto_min_vtx_dist(&self) -> f32 {
                $bv!(self).0
            }
            fn proto_priority(&self) -> i32 {
                $bv!(self).1
            }
            fn proto_flag24(&self) -> i32 {
                $bv!(self).2
            }
            fn proto_bv_tree(&self) -> BvRef<'_> {
                BvRef::Owned(Box::new(single_box($bv!(self).3)))
            }
            fn proto_prepare(
                &self,
                ctx: &mut IntersectCtx<'_>,
                side: usize,
                keep_prev: bool,
            ) -> i32 {
                self.prepare_for_intersection_test(&mut ctx.s[side].gt, None, None, keep_prev)
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
                let (a, b) = ctx.s.split_at_mut(1);
                let (me, op) = if side == 0 {
                    (&mut a[0], &b[0])
                } else {
                    (&mut b[0], &a[0])
                };
                let cp = super::mesh::bv_record_prim(collider);
                let n = self.get_primitive_list(
                    i_start,
                    n_prims,
                    collider.kind,
                    Some(&cp),
                    b_local,
                    &mut me.gt,
                    &op.gt,
                );
                me.prim_idx = vec![0; me.gt.prims.len()];
                n
            }
            fn proto_register(
                &self,
                ctx: &mut IntersectCtx<'_>,
                side: usize,
                _other: &dyn GeomProtocol,
                i_this: usize,
                i_other: usize,
                inters: &mut PrimInters,
            ) -> i32 {
                base_register(ctx, side, i_this, i_other, inters)
            }
        }
    };
}

macro_rules! box_fields {
    ($s:expr) => {
        ($s.hdr.min_vtx_dist, $s.hdr.priority, $s.hdr.flag24, &$s.bv)
    };
}
macro_rules! cyl_fields {
    ($s:expr) => {
        (
            $s.0.hdr.min_vtx_dist,
            $s.0.hdr.priority,
            $s.0.hdr.flag24,
            &$s.0.bv,
        )
    };
}

solid_protocol!(BoxGeom, GeomType::Box, box_fields);
solid_protocol!(SphereGeom, GeomType::Sphere, box_fields);
solid_protocol!(CylinderGeom, GeomType::Cylinder, cyl_fields);
solid_protocol!(CapsuleGeom, GeomType::Capsule, cyl_fields);

impl GeomProtocol for RayGeom {
    fn proto_type(&self) -> GeomType {
        GeomType::Ray
    }
    fn proto_min_vtx_dist(&self) -> f32 {
        self.hdr.min_vtx_dist
    }
    fn proto_priority(&self) -> i32 {
        self.hdr.priority
    }
    fn proto_flag24(&self) -> i32 {
        self.hdr.flag24
    }
    fn proto_bv_tree(&self) -> BvRef<'_> {
        BvRef::Owned(Box::new(RayBv::new(self.prim)))
    }
    fn proto_ray(&self) -> Option<crate::geometry::primitives::RayPrim> {
        Some(self.prim)
    }
    /// Slot 6 (0x35199F50): the segment's axis-aligned box.
    fn proto_bbox(&self) -> crate::geometry::primitives::BoxPrim {
        crate::geometry::Geometry::bbox(self)
    }
    fn proto_prepare(&self, ctx: &mut IntersectCtx<'_>, side: usize, keep_prev: bool) -> i32 {
        self.prepare_for_intersection_test(&mut ctx.s[side].gt, None, None, keep_prev)
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
        let (a, b) = ctx.s.split_at_mut(1);
        let (me, op) = if side == 0 {
            (&mut a[0], &b[0])
        } else {
            (&mut b[0], &a[0])
        };
        let cp = super::mesh::bv_record_prim(collider);
        let n = self.get_primitive_list(
            i_start,
            n_prims,
            collider.kind,
            Some(&cp),
            b_local,
            &mut me.gt,
            &op.gt,
        );
        me.prim_idx = vec![0; me.gt.prims.len()];
        n
    }
    /// 0x3519A380 (P1a): one contact per hit; the other side's triangle index comes from its
    /// 52-byte record (`+48`).
    fn proto_register(
        &self,
        ctx: &mut IntersectCtx<'_>,
        side: usize,
        _other: &dyn GeomProtocol,
        _i_this: usize,
        i_other: usize,
        inters: &mut PrimInters,
    ) -> i32 {
        let idx_b = ctx.s[side ^ 1].prim_idx.get(i_other).copied().unwrap_or(0);
        let (a, b) = ctx.s.split_at_mut(1);
        let (me, op) = if side == 0 {
            (&mut a[0], &b[0])
        } else {
            (&mut b[0], &a[0])
        };
        me.gt.n_max_contacts = me.gt.n_max_contacts.max(0);
        let r = ray_register_intersection(
            self,
            idx_b,
            &mut me.gt,
            Some(&op.gt),
            inters,
            &mut ctx.contacts,
        );
        ctx.border_kind.push(BorderKind::Pt);
        ctx.border_pool_at.push(None);
        // the ray writes no area into its contact and none into the next slot
        ctx.next_area = None;
        r
    }
}
