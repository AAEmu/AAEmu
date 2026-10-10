//! Intersection-test protocol of the primitives (`geometry.md` §2.1, §2.3, §3.4): slot 48
//! `PreparePrimitive` and the "geometry under test" slots the generic `CGeometry::Intersect`
//! (0x3514FD70, package P2b) and the BV trees (P1b) drive:
//!
//! | slot | role | box | sphere | cylinder | capsule | ray |
//! |---|---|---|---|---|---|---|
//! | 48 | PreparePrimitive (world-space primitive) | 0x3513B580 | 0x351AF380 | 0x35142240 | 0x35038B60 | 0x3519A020 |
//! | 49 | UnprojectSphere | 0x3513AFC0 | 0x351AFA60 | 0x35142C10 | 0x35140AA0 | base (0) |
//! | 53 | PrepareForIntersectionTest | 0x3513A330 | 0x351AE910 | 0x35141CC0 | 0x3513F750 | 0x3519A5C0 |
//! | 54 | RegisterIntersection | base (P2b) | base | base | base | 0x3519A380 |
//! | 56 | GetPrimitiveList | 0x3513B810 | 0x351AF480 | 0x351423D0 | 0x351423D0 | 0x3519A340 |
//! | 57 | GetUnprojectionCandidates | 0x3513BAF0 | 0x351AF570 | 0x35142410 | 0x3513F9A0 | 0x3519A2F0 |
//! | 58 | face polygon | 0x3513C1C0 | base (0) | 0x351455A0 | 0x351455A0 | base (0) |
//! | 59 | edge segment | 0x3513C990 | base (0) | 0x351427D0 | 0x351427D0 | base (0) |
//!
//! Layout: [`gtest`] (the [`GeomUnderTest`] record and the small output records), [`prep`]
//! (48/53/56), [`candidates`] (57), [`polygon`] (58/59), [`unproject`] (49), [`register`]
//! (ray 54). [`IntersectionProtocol`] exposes the slots per class; its default bodies are the
//! `CGeometry` base slots.
//!
//! **Cross-package calls.** Box and cylinder slot 49 forward into `UNPROJECT[0][box][sphere]`
//! (0x35185B80) and `UNPROJECT[0][cylinder][sphere]` (0x35187780), owned by P2b; they are
//! reached through [`SphereUnprojectCallees`]. The OVERLAP cell `[box][sphere]` (0x35196CE0,
//! P2a) is small and ported locally ([`overlap_box_sphere`]).

pub mod candidates;
pub mod gtest;
pub mod polygon;
pub mod prep;
pub mod register;
pub mod unproject;

pub use candidates::{
    base_get_unprojection_candidates, box_get_unprojection_candidates,
    capsule_get_unprojection_candidates, cylinder_get_unprojection_candidates,
    ray_get_unprojection_candidates, sphere_get_unprojection_candidates,
};
pub use gtest::{
    ContactFrame, EdgeCandidate, FaceCandidate, FeaturePolygon, GeomUnderTest, UsedNodeMap,
    UsedNodes,
};
pub use polygon::{
    box_edge_segment, box_face_polygon, cylinder_edge_segment, cylinder_face_polygon,
};
pub use prep::{
    box_get_primitive_list, box_prepare_for_intersection_test, box_prepare_primitive,
    cylinder_get_primitive_list, cylinder_min_pt_dist, cylinder_prepare_for_intersection_test,
    cylinder_prepare_primitive, ray_get_primitive_list, ray_prepare_for_intersection_test,
    ray_prepare_primitive, sphere_get_primitive_list, sphere_prepare_for_intersection_test,
    sphere_prepare_primitive, xf_box, xf_cylinder, xf_ray, xf_sphere,
};
pub use register::ray_register_intersection;
pub use unproject::{
    box_unproject_sphere, capsule_unproject_sphere, cylinder_unproject_sphere, overlap_box_sphere,
    slot49_params, sphere_unproject_sphere, SphereUnprojectCallees,
};

use super::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use crate::collision::contact::UnprojResult;
use crate::geometry::primitives::{BoxPrim, Primitive};
use crate::geometry::Geometry;
use crate::math::{sgnnz, Vec3};

/// Word `i` of the 16-word box primitive layout (`Basis` 0–8, `bOriented` 9 as float bits,
/// centre 10–12, size 13–15); words past the record read as 0 (the binary reads whatever
/// follows the 64-byte buffer).
#[must_use]
pub fn box_word(b: &BoxPrim, i: usize) -> f32 {
    match i {
        0..=8 => b.basis.m[i / 3][i % 3],
        9 => f32::from_bits(b.oriented as u32),
        10..=12 => b.center[i - 10],
        13..=15 => b.size[i - 13],
        _ => 0.0,
    }
}

/// Row `i` of the box basis read as `prim[3i .. 3i+3]`, so that the out-of-range row 3 is the
/// words 9–11 as in the binary.
#[must_use]
pub fn box_row(b: &BoxPrim, i: usize) -> Vec3 {
    Vec3::new(
        box_word(b, 3 * i),
        box_word(b, 3 * i + 1),
        box_word(b, 3 * i + 2),
    )
}

/// `(float)sgnnz(x)` (`cvtdq2ps` of the `((bits >> 30) & ~1) + 1` idiom).
#[inline]
#[must_use]
pub fn sgnnz_f(x: f32) -> f32 {
    sgnnz(x) as f32
}

/// The protocol slots of a geometry class. Default bodies are the `CGeometry` base slots
/// (slot 49 `r0`, 53 `r1`, 56 `r0`, 57 0x35037720, 58/59 `r0`); the five primitive classes
/// implement the overrides listed in the module table. Slot 54 is not here: the base belongs
/// to P2b and only the ray overrides it ([`ray_register_intersection`]).
pub trait IntersectionProtocol {
    /// Slot 49 `UnprojectSphere(center, r, rsep, unproj_result*)`.
    fn unproject_sphere(
        &self,
        _center: Vec3,
        _r: f32,
        _rsep: f32,
        _out: &mut UnprojResult,
        _callees: &mut dyn SphereUnprojectCallees,
    ) -> i32 {
        0
    }

    /// Slot 53 `PrepareForIntersectionTest(gtest*, IGeometry* other, gtest_other*,
    /// bKeepPrevContacts)`. The primitives only use `gtest`.
    fn prepare_for_intersection_test(
        &self,
        _gt: &mut GeomUnderTest,
        _other: Option<&dyn Geometry>,
        _gt_other: Option<&GeomUnderTest>,
        _keep_prev_contacts: bool,
    ) -> i32 {
        1
    }

    /// Slot 56 `GetPrimitiveList(iStartPrim, nPrims, colliderType, colliderPrim*,
    /// bColliderLocal, gtest*, gtestOp*, primBuf, idBuf)`; the buffers are the gtest's own
    /// `+288`/`+320` (what every BV passes). The primitives ignore the range and the collider.
    #[allow(clippy::too_many_arguments)]
    fn get_primitive_list(
        &self,
        _i_start: i32,
        _n_prims: i32,
        _collider_type: i32,
        _collider_prim: Option<&Primitive>,
        _b_collider_local: i32,
        _gt: &mut GeomUnderTest,
        _gt_op: &GeomUnderTest,
    ) -> i32 {
        0
    }

    /// Slot 57 `GetUnprojectionCandidates(iSide, contact*, prim**, iPrim, gtest*)`; `prim` is
    /// the caller's primitive (redirected = overwritten where the binary redirects the pointer).
    fn get_unprojection_candidates(
        &self,
        _i_side: i32,
        _contact: &UnprojResult,
        _prim: &mut Primitive,
        _i_prim: i32,
        gt: &mut GeomUnderTest,
    ) -> i32 {
        base_get_unprojection_candidates(gt)
    }

    /// Slot 58 (feature polygon of a face).
    fn face_polygon(
        &self,
        _frame: &mut ContactFrame,
        _i_prim: i32,
        _i_feature: i32,
        _gt: &GeomUnderTest,
        _out: &mut FeaturePolygon,
    ) -> i32 {
        0
    }

    /// Slot 59 (segment of an edge feature).
    fn edge_segment(
        &self,
        _frame: &mut ContactFrame,
        _i_prim: i32,
        _i_feature: i32,
        _gt: &GeomUnderTest,
        _out: &mut FeaturePolygon,
    ) -> i32 {
        0
    }
}

impl IntersectionProtocol for BoxGeom {
    fn unproject_sphere(
        &self,
        center: Vec3,
        r: f32,
        rsep: f32,
        out: &mut UnprojResult,
        callees: &mut dyn SphereUnprojectCallees,
    ) -> i32 {
        box_unproject_sphere(self, center, r, rsep, out, callees)
    }

    fn prepare_for_intersection_test(
        &self,
        gt: &mut GeomUnderTest,
        _other: Option<&dyn Geometry>,
        _gt_other: Option<&GeomUnderTest>,
        _keep_prev_contacts: bool,
    ) -> i32 {
        box_prepare_for_intersection_test(self, gt)
    }

    fn get_primitive_list(
        &self,
        _i_start: i32,
        _n_prims: i32,
        _collider_type: i32,
        _collider_prim: Option<&Primitive>,
        _b_collider_local: i32,
        gt: &mut GeomUnderTest,
        gt_op: &GeomUnderTest,
    ) -> i32 {
        box_get_primitive_list(self, gt, gt_op)
    }

    fn get_unprojection_candidates(
        &self,
        i_side: i32,
        contact: &UnprojResult,
        prim: &mut Primitive,
        _i_prim: i32,
        gt: &mut GeomUnderTest,
    ) -> i32 {
        box_get_unprojection_candidates(self, i_side, contact, prim, gt)
    }

    fn face_polygon(
        &self,
        frame: &mut ContactFrame,
        _i_prim: i32,
        i_feature: i32,
        gt: &GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        box_face_polygon(self, frame, i_feature, gt, out)
    }

    fn edge_segment(
        &self,
        frame: &mut ContactFrame,
        _i_prim: i32,
        i_feature: i32,
        gt: &GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        box_edge_segment(self, frame, i_feature, gt, out)
    }
}

impl IntersectionProtocol for SphereGeom {
    fn unproject_sphere(
        &self,
        center: Vec3,
        r: f32,
        rsep: f32,
        out: &mut UnprojResult,
        _callees: &mut dyn SphereUnprojectCallees,
    ) -> i32 {
        sphere_unproject_sphere(self, center, r, rsep, out)
    }

    fn prepare_for_intersection_test(
        &self,
        gt: &mut GeomUnderTest,
        _other: Option<&dyn Geometry>,
        _gt_other: Option<&GeomUnderTest>,
        _keep_prev_contacts: bool,
    ) -> i32 {
        sphere_prepare_for_intersection_test(self, gt)
    }

    fn get_primitive_list(
        &self,
        _i_start: i32,
        _n_prims: i32,
        _collider_type: i32,
        _collider_prim: Option<&Primitive>,
        _b_collider_local: i32,
        gt: &mut GeomUnderTest,
        _gt_op: &GeomUnderTest,
    ) -> i32 {
        sphere_get_primitive_list(self, gt)
    }

    fn get_unprojection_candidates(
        &self,
        _i_side: i32,
        _contact: &UnprojResult,
        prim: &mut Primitive,
        _i_prim: i32,
        gt: &mut GeomUnderTest,
    ) -> i32 {
        sphere_get_unprojection_candidates(self, prim, gt)
    }
}

macro_rules! cylinder_protocol {
    ($ty:ty, $capsule:expr) => {
        impl IntersectionProtocol for $ty {
            fn prepare_for_intersection_test(
                &self,
                gt: &mut GeomUnderTest,
                _other: Option<&dyn Geometry>,
                _gt_other: Option<&GeomUnderTest>,
                _keep_prev_contacts: bool,
            ) -> i32 {
                cylinder_prepare_for_intersection_test(&self.0, $capsule, gt)
            }

            fn get_primitive_list(
                &self,
                _i_start: i32,
                _n_prims: i32,
                _collider_type: i32,
                _collider_prim: Option<&Primitive>,
                _b_collider_local: i32,
                gt: &mut GeomUnderTest,
                _gt_op: &GeomUnderTest,
            ) -> i32 {
                cylinder_get_primitive_list(&self.0, $capsule, gt)
            }

            fn face_polygon(
                &self,
                frame: &mut ContactFrame,
                _i_prim: i32,
                i_feature: i32,
                gt: &GeomUnderTest,
                out: &mut FeaturePolygon,
            ) -> i32 {
                cylinder_face_polygon(&self.0, frame, i_feature, gt, out)
            }

            fn edge_segment(
                &self,
                frame: &mut ContactFrame,
                _i_prim: i32,
                _i_feature: i32,
                gt: &GeomUnderTest,
                out: &mut FeaturePolygon,
            ) -> i32 {
                cylinder_edge_segment(&self.0, frame, gt, out)
            }

            fn unproject_sphere(
                &self,
                center: Vec3,
                r: f32,
                rsep: f32,
                out: &mut UnprojResult,
                callees: &mut dyn SphereUnprojectCallees,
            ) -> i32 {
                if $capsule {
                    capsule_unproject_sphere(&self.0, center, r, out)
                } else {
                    cylinder_unproject_sphere(&self.0, center, r, rsep, out, callees)
                }
            }

            fn get_unprojection_candidates(
                &self,
                i_side: i32,
                contact: &UnprojResult,
                prim: &mut Primitive,
                _i_prim: i32,
                gt: &mut GeomUnderTest,
            ) -> i32 {
                if $capsule {
                    capsule_get_unprojection_candidates(&self.0, i_side, contact, prim, gt)
                } else {
                    cylinder_get_unprojection_candidates(&self.0, false, i_side, contact, prim, gt)
                }
            }
        }
    };
}

cylinder_protocol!(CylinderGeom, false);
cylinder_protocol!(CapsuleGeom, true);

impl IntersectionProtocol for RayGeom {
    fn prepare_for_intersection_test(
        &self,
        gt: &mut GeomUnderTest,
        _other: Option<&dyn Geometry>,
        _gt_other: Option<&GeomUnderTest>,
        _keep_prev_contacts: bool,
    ) -> i32 {
        ray_prepare_for_intersection_test(self, gt)
    }

    fn get_primitive_list(
        &self,
        _i_start: i32,
        _n_prims: i32,
        _collider_type: i32,
        _collider_prim: Option<&Primitive>,
        _b_collider_local: i32,
        gt: &mut GeomUnderTest,
        _gt_op: &GeomUnderTest,
    ) -> i32 {
        ray_get_primitive_list(self, gt)
    }

    fn get_unprojection_candidates(
        &self,
        _i_side: i32,
        _contact: &UnprojResult,
        _prim: &mut Primitive,
        _i_prim: i32,
        gt: &mut GeomUnderTest,
    ) -> i32 {
        ray_get_unprojection_candidates(self, gt)
    }
}
