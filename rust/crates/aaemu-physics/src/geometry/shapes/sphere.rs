//! `CSphereGeom` (sizeof 152): `+48` center, `+60` r, `+64` embedded `CSingleBoxTree`.

use super::{GeomHeader, PrimitiveGeom, SingleBoxBv};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::primitives::{BoxPrim, Plane, Primitive, SpherePrim};
use crate::geometry::{GeomWorldData, Geometry, PhysProperties};
use crate::ids::GeomType;
use crate::math::{Mat33, Quat, Vec3};

/// `4/3·π` as the binary stores it (double 0x4010C152382D7365).
pub const SPHERE_K_F64: f64 = f64::from_bits(0x4010_C152_382D_7365);
/// `4/3·π` as a float constant (0x40860A92, used by slot 42 and the capsule).
pub const SPHERE_K_F32: f32 = f32::from_bits(0x4086_0A92);

/// `CSphereGeom`.
#[derive(Clone, Debug, PartialEq)]
pub struct SphereGeom {
    pub hdr: GeomHeader,
    /// `+48`.
    pub prim: SpherePrim,
    /// `+64`.
    pub bv: SingleBoxBv,
}

impl SphereGeom {
    /// `CreatePrimitive(4, prim)`: ctor 0x35038C20 + `SetData`.
    #[must_use]
    pub fn new(prim: &SpherePrim) -> Self {
        let mut g = Self::empty();
        g.set_data(prim);
        g
    }

    /// The object right after the ctor.
    #[must_use]
    pub fn empty() -> Self {
        Self {
            hdr: GeomHeader::solid(),
            prim: SpherePrim::default(),
            bv: SingleBoxBv::default(),
        }
    }

    /// Slot 30 `SetData` (`sub_351AE9E0`): centre and r; the tree box is the axis-aligned
    /// cube `(center, (r, r, r))`; `m_minVtxDist = r·1e-4`.
    pub fn set_data(&mut self, prim: &SpherePrim) {
        self.prim = *prim;
        self.bv.set_box(&BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: prim.center,
            size: Vec3::splat(prim.r),
        });
        self.hdr.min_vtx_dist = self.prim.r * 1e-4;
    }

    /// The world centre `R·center + offset` **without** the scale (slots 13 — see the quirk
    /// there) and `(R·center)·s + offset` with it (everything else).
    #[inline]
    #[must_use]
    pub fn world_center(&self, gwd: &GeomWorldData, scaled: bool) -> Vec3 {
        let rc = gwd.r.mul_vec(self.prim.center);
        if scaled {
            rc * gwd.scale + gwd.offset
        } else {
            rc + gwd.offset
        }
    }
}

impl PrimitiveGeom for SphereGeom {
    fn header(&self) -> &GeomHeader {
        &self.hdr
    }
}

impl Geometry for SphereGeom {
    /// P1c hook: the intersection protocol (`trimesh::protocol::shapes`).
    fn as_protocol(&self) -> Option<&dyn crate::geometry::trimesh::protocol::GeomProtocol> {
        Some(self)
    }

    /// Slot 1 (0x35037C40).
    fn geom_type(&self) -> GeomType {
        GeomType::Sphere
    }

    /// Slot 6 (0x35038CB0) → `CSingleBoxTree` slot 2.
    fn bbox(&self) -> BoxPrim {
        self.bv.box_
    }

    /// Slot 7 (0x351AEAC0): `V = (float)((double)((r·r)·r)·4/3π)`, `I = ((r·r)·0.4)·V` on all
    /// three axes, identity q, origin = centre.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        let r = self.prim.r;
        let volume = (f64::from((r * r) * r) * SPHERE_K_F64) as f32;
        let i = ((r * r) * 0.4) * volume;
        Some(PhysProperties {
            volume,
            inertia: Vec3::splat(i),
            q: Quat::IDENTITY,
            origin: self.prim.center,
        })
    }

    /// Slot 8 (0x351AEB40): sign bit of `|p − c|² − r²`.
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        let d = pt - self.prim.center;
        ((d.len2() - self.prim.r * self.prim.r).to_bits() >> 31) as i32
    }

    /// Slot 13 (0x351AEC00), see [`super::buoyancy`].
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        super::buoyancy::sphere_buoyancy(self, plane, gwd)
    }

    fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3) {
        let (mut dp, mut dl) = (Vec3::ZERO, Vec3::ZERO);
        self.calculate_medium_resistance_acc(plane, gwd, &mut dp, &mut dl);
        (dp, dl)
    }

    /// Slot 14 (0x351AFCB0), see [`super::drag`].
    fn calculate_medium_resistance_acc(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        super::drag::sphere_medium_resistance(self, plane, gwd, dp, dl);
    }

    /// Slot 17 (0x35038D40).
    fn primitive(&self, _i_prim: i32) -> Option<Primitive> {
        Some(Primitive::Sphere(self.prim))
    }

    /// Slot 31 (0x35038D10): `(float)((double)((r·r)·r)·4/3π)`.
    fn volume(&self) -> f32 {
        let r = self.prim.r;
        (f64::from((r * r) * r) * SPHERE_K_F64) as f32
    }

    /// Slot 32 (0x35038CE0, shared with the cylinder).
    fn center(&self) -> Vec3 {
        self.prim.center
    }

    fn is_a_primitive(&self) -> bool {
        true
    }

    fn min_vtx_dist(&self) -> f32 {
        self.hdr.min_vtx_dist
    }

    fn priority(&self) -> i32 {
        self.hdr.priority
    }

    /// Slot 10: shared primitive Intersect 0x35151F20 (P2a/P2b), not wired yet.
    fn intersect(
        &self,
        other: &dyn Geometry,
        gwd1: &GeomWorldData,
        gwd2: &GeomWorldData,
        params: &mut IntersectionParams,
        contacts: &mut Vec<GeomContact>,
    ) -> usize {
        crate::collision::prim_intersect::geometry_intersect(
            self, other, gwd1, gwd2, params, contacts,
        )
    }

    /// Slot 11 (0x351AF660), see [`super::closest`].
    fn find_closest_point(
        &self,
        gwd: &GeomWorldData,
        i_prim: &mut i32,
        i_feature: &mut i32,
        ptdst0: Vec3,
        ptdst1: Vec3,
        ptres: &mut [Vec3; 2],
        n_max_iters: i32,
    ) -> i32 {
        super::closest::sphere_find_closest_point(
            self,
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }

    /// Slot 12 (0x351AEE70), see [`super::buoyancy`].
    fn calc_volumetric_pressure(
        &self,
        gwd: &GeomWorldData,
        epicenter: Vec3,
        k: f32,
        rmin: f32,
        center_of_mass: Vec3,
        p: &mut Vec3,
        l: &mut Vec3,
    ) {
        super::buoyancy::sphere_volumetric_pressure(
            self,
            gwd,
            epicenter,
            k,
            rmin,
            center_of_mass,
            p,
            l,
        );
    }

    /// Slot 25 (0x351AFBF0), see [`super::stream`].
    fn save(&self, out: &mut Vec<u8>) {
        super::stream::save_sphere(self, out);
    }

    /// Slot 42 (0x351AEBB0): form 2 `(r·r)·4π`, form 3 `((r·r)·r)·4/3π` (float constants),
    /// else 0.
    fn extent(&self, form: i32) -> f32 {
        let r = self.prim.r;
        match form {
            2 => (r * r) * f32::from_bits(0x4149_0FDB),
            3 => ((r * r) * r) * SPHERE_K_F32,
            _ => 0.0,
        }
    }

    /// Slot 48 (0x351AF380), see [`super::protocol`].
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(super::protocol::sphere_prepare_primitive(self, gwd))
    }

    /// Slot 52 (0x35038D00).
    fn size_of_object(&self) -> i32 {
        152
    }

    /// Slot 43 (0x351AF140), see [`super::misc::random`].
    fn get_random_pos(
        &self,
        out: &mut super::misc::PosNorm,
        rng: &mut super::misc::CryRandom,
        form: i32,
    ) {
        super::misc::random::sphere_get_random_pos(self, out, rng, form);
    }
}
