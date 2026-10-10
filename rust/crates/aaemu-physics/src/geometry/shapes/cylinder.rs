//! `CCylinderGeom` and `CCapsuleGeom` (both sizeof 176, same layout and ctor body):
//! `+48` center, `+60` axis (unit), `+72` r, `+76` hh, `+80` int 18 (tessellation count),
//! `+88` embedded `CSingleBoxTree`.
//!
//! The capsule derives from the cylinder: `hh` is the half-length of the cylindrical part.
//! It overrides slots 7, 8, 11, 14, 30, 31, 48, 49, 53 and 57; slot 13 is a thunk to the
//! cylinder's (0x3513F790) and every other slot is the cylinder's (including slot 42
//! `GetExtent`, which therefore ignores the end caps).

use super::{
    normalize_safe, orthogonal, quat_rotation_arc, GeomHeader, PrimitiveGeom, SingleBoxBv,
};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Plane, Primitive};
use crate::geometry::{GeomWorldData, Geometry, PhysProperties};
use crate::ids::GeomType;
use crate::math::{max_idiom, min_idiom, sgnnz, Mat33, Vec3};

/// `2π` (double 0x401921FB54442D18).
pub const TWO_PI_F64: f64 = f64::from_bits(0x4019_21FB_5444_2D18);
/// `π` (double 0x400921FB54442D18).
pub const PI_F64: f64 = f64::from_bits(0x4009_21FB_5444_2D18);
/// `π` as a float (0x40490FDB).
pub const PI_F32: f32 = f32::from_bits(0x4049_0FDB);

/// Shared data of both classes.
#[derive(Clone, Debug, PartialEq)]
pub struct CylinderData {
    pub hdr: GeomHeader,
    /// `+48`.
    pub prim: CylinderPrim,
    /// `+80` (18, read by the wireframe/random-position code; serialized by slot 25).
    pub n_tess: i32,
    /// `+88`.
    pub bv: SingleBoxBv,
}

impl CylinderData {
    fn empty() -> Self {
        Self {
            hdr: GeomHeader::solid(),
            prim: CylinderPrim {
                center: Vec3::ZERO,
                axis: Vec3::ZERO,
                r: 0.0,
                hh: 0.0,
            },
            n_tess: 18,
            bv: SingleBoxBv::default(),
        }
    }

    /// `SetData` body shared by the cylinder (`sub_35144DB0`) and the capsule
    /// (`sub_35141920`): copy the primitive; tree box `Basis = (e0, axis × e0, axis)` with
    /// `e0 = normalize_safe(orthogonal(axis), (0,0,1))`, centre, size `(r, r, hz)` where `hz`
    /// is `hh` (cylinder) or `r + hh` (capsule); `m_minVtxDist = (r + hh)·1e-4`.
    fn set_data(&mut self, prim: &CylinderPrim, capsule: bool) {
        self.prim = *prim;
        let a = self.prim.axis;
        let e0 = normalize_safe(orthogonal(a), Vec3::Z);
        let e1 = a.cross(e0);
        let r = self.prim.r;
        let hz = if capsule {
            r + self.prim.hh
        } else {
            self.prim.hh
        };
        self.bv.set_box(&BoxPrim {
            basis: Mat33::from_rows(e0, e1, a),
            oriented: 1,
            center: self.prim.center,
            size: Vec3::new(r, r, hz),
        });
        self.hdr.min_vtx_dist = (self.prim.r + self.prim.hh) * 1e-4;
    }
}

/// `CCylinderGeom`.
#[derive(Clone, Debug, PartialEq)]
pub struct CylinderGeom(pub CylinderData);

/// `CCapsuleGeom`.
#[derive(Clone, Debug, PartialEq)]
pub struct CapsuleGeom(pub CylinderData);

impl CylinderGeom {
    /// `CreatePrimitive(5, prim)`: ctor 0x35038960 + `SetData` 0x35144DB0.
    #[must_use]
    pub fn new(prim: &CylinderPrim) -> Self {
        let mut g = Self(CylinderData::empty());
        g.set_data(prim);
        g
    }

    /// The object right after the ctor.
    #[must_use]
    pub fn empty() -> Self {
        Self(CylinderData::empty())
    }

    /// Slot 30 (0x35038A20 → 0x35144DB0).
    pub fn set_data(&mut self, prim: &CylinderPrim) {
        self.0.set_data(prim, false);
    }

    /// The primitive.
    #[must_use]
    pub fn prim(&self) -> &CylinderPrim {
        &self.0.prim
    }
}

impl CapsuleGeom {
    /// `CreatePrimitive(6, prim)`: ctor 0x35038AB0 + `SetData` 0x35141920.
    #[must_use]
    pub fn new(prim: &CylinderPrim) -> Self {
        let mut g = Self(CylinderData::empty());
        g.set_data(prim);
        g
    }

    /// The object right after the ctor.
    #[must_use]
    pub fn empty() -> Self {
        Self(CylinderData::empty())
    }

    /// Slot 30 (0x35038B50 → 0x35141920).
    pub fn set_data(&mut self, prim: &CylinderPrim) {
        self.0.set_data(prim, true);
    }

    /// The primitive.
    #[must_use]
    pub fn prim(&self) -> &CylinderPrim {
        &self.0.prim
    }
}

/// Cylinder slot 7 (0x35145000) / capsule slot 7 (0x35141B70): origin = centre,
/// `q = QuatRotationArc((0,0,1), axis)` (0x35040F80).
fn cyl_props(d: &CylinderData, capsule: bool) -> PhysProperties {
    let q = quat_rotation_arc(Vec3::Z, d.prim.axis);
    let (r, hh) = (d.prim.r, d.prim.hh);
    if capsule {
        // all single precision, float constants 2π, 4/3π, π, 0.4, 2/3
        let v7 = ((r * r) * r) * super::sphere::SPHERE_K_F32;
        let volume = ((r * r) * hh) * f32::from_bits(0x40C9_0FDB) + v7;
        let v8 = r * r;
        let v9 = ((v8 * v8) * (hh * PI_F32)) * 0.5;
        let hh2 = hh * hh;
        let v3 = (((hh2 * hh) * (v8 * PI_F32)) * f32::from_bits(0x3F2A_AAAB)) + v9;
        let ixy = (hh2 + v8 * 0.4) * v7 + v3;
        let iz = (v8 * v7) * 0.4 + v9 * 2.0;
        PhysProperties {
            volume,
            inertia: Vec3::new(ixy, ixy, iz),
            q,
            origin: d.prim.center,
        }
    } else {
        let volume = (f64::from((r * r) * hh) * TWO_PI_F64) as f32;
        let v7 = r * r;
        let v8 = ((f64::from(v7 * v7) * (f64::from(hh) * PI_F64)) * 0.5) as f32;
        let v9 = ((f64::from((hh * hh) * hh) * (f64::from(v7) * PI_F64)) * (2.0 / 3.0)) as f32;
        let ixy = v9 + v8;
        PhysProperties {
            volume,
            inertia: Vec3::new(ixy, ixy, v8 * 2.0),
            q,
            origin: d.prim.center,
        }
    }
}

/// Cylinder slot 42 (0x35141DD0), also used by the capsule: form 1 `(r·4)·π`, form 2
/// `(hh·2 + r)·((r·π)·2)`, form 3 `(((r·r)·π)·hh)·2` (float π), else 0.
fn cyl_extent(p: &CylinderPrim, form: i32) -> f32 {
    match form {
        1 => (p.r * 4.0) * PI_F32,
        2 => (p.hh * 2.0 + p.r) * ((p.r * PI_F32) * 2.0),
        3 => (((p.r * p.r) * PI_F32) * p.hh) * 2.0,
        _ => 0.0,
    }
}

macro_rules! cylinder_common_slots {
    () => {
        /// Slot 6 (0x35038A10) → `CSingleBoxTree` slot 2.
        fn bbox(&self) -> BoxPrim {
            self.0.bv.box_
        }

        fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3) {
            let (mut dp, mut dl) = (Vec3::ZERO, Vec3::ZERO);
            self.calculate_medium_resistance_acc(plane, gwd, &mut dp, &mut dl);
            (dp, dl)
        }

        /// Slot 17 (0x35038A70): the 32-byte primitive.
        fn primitive(&self, _i_prim: i32) -> Option<Primitive> {
            Some(self.wrap_prim())
        }

        /// Slot 32 (0x35038CE0): the centre.
        fn center(&self) -> Vec3 {
            self.0.prim.center
        }

        fn is_a_primitive(&self) -> bool {
            true
        }

        fn min_vtx_dist(&self) -> f32 {
            self.0.hdr.min_vtx_dist
        }

        fn priority(&self) -> i32 {
            self.0.hdr.priority
        }

        /// Slot 10: shared primitive Intersect 0x35151F20 (P2a/P2b), not wired yet.
        fn intersect(
            &self,
            _other: &dyn Geometry,
            _gwd1: &GeomWorldData,
            _gwd2: &GeomWorldData,
            _params: &mut IntersectionParams,
            _contacts: &mut Vec<GeomContact>,
        ) -> usize {
            0
        }

        /// Slot 12 (0x35142EE0, both classes), see [`super::buoyancy`].
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
            super::buoyancy::cylinder_volumetric_pressure(
                &self.0,
                gwd,
                epicenter,
                k,
                rmin,
                center_of_mass,
                p,
                l,
            );
        }

        /// Slot 25 (0x35144CD0, both classes), see [`super::stream`].
        fn save(&self, out: &mut Vec<u8>) {
            super::stream::save_cylinder(&self.0, out);
        }

        /// Slot 42 (0x35141DD0, both classes).
        fn extent(&self, form: i32) -> f32 {
            cyl_extent(&self.0.prim, form)
        }

        /// Slot 52 (0x35038A30).
        fn size_of_object(&self) -> i32 {
            176
        }

        /// Slot 43 (0x35145120, both classes), see [`super::misc::random`].
        fn get_random_pos(
            &self,
            out: &mut super::misc::PosNorm,
            rng: &mut super::misc::CryRandom,
            form: i32,
        ) {
            super::misc::random::cylinder_get_random_pos(&self.0, out, rng, form);
        }
    };
}

impl CylinderGeom {
    fn wrap_prim(&self) -> Primitive {
        Primitive::Cylinder(self.0.prim)
    }
}

impl CapsuleGeom {
    fn wrap_prim(&self) -> Primitive {
        Primitive::Capsule(self.0.prim)
    }
}

impl PrimitiveGeom for CylinderGeom {
    fn header(&self) -> &GeomHeader {
        &self.0.hdr
    }
}

impl PrimitiveGeom for CapsuleGeom {
    fn header(&self) -> &GeomHeader {
        &self.0.hdr
    }
}

impl Geometry for CylinderGeom {
    cylinder_common_slots!();

    /// Slot 1 (0x35038A00).
    fn geom_type(&self) -> GeomType {
        GeomType::Cylinder
    }

    /// Slot 7 (0x35145000), see `cyl_props`: `V = (float)((double)((r·r)·hh)·2π)`,
    /// `Iz = 2·(float)((double)(r²·r²)·((double)hh·π)·0.5)`,
    /// `Ix = Iy = (float)((double)((hh·hh)·hh)·((double)r²·π)·2/3) + Iz/2`.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        Some(cyl_props(&self.0, false))
    }

    /// Slot 8 (0x35141FC0): `h = d·axis`, `max(|d − axis·h|² − r², |h| − hh) < 0` (max idiom,
    /// sign bit).
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        let p = &self.0.prim;
        let d = pt - p.center;
        let h = d.dot(p.axis);
        let perp = d - p.axis * h;
        let rr = perp.len2() - p.r * p.r;
        let hz = h.abs() - p.hh;
        (max_idiom(rr, hz).to_bits() >> 31) as i32
    }

    /// Slot 13 (0x35143230), see [`super::buoyancy`].
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        super::buoyancy::cylinder_buoyancy(&self.0, false, plane, gwd)
    }

    /// Slot 14 (0x35146F40), see [`super::drag`].
    fn calculate_medium_resistance_acc(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        super::drag::cylinder_medium_resistance(&self.0, plane, gwd, dp, dl);
    }

    /// Slot 31 (0x35038A40): `(float)((double)((r·r)·hh)·2π)`.
    fn volume(&self) -> f32 {
        let p = &self.0.prim;
        (f64::from((p.r * p.r) * p.hh) * TWO_PI_F64) as f32
    }

    /// Slot 11 (0x35145CC0), see [`super::closest`].
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
        super::closest::cylinder_find_closest_point(
            &self.0,
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }

    /// Slot 48 (0x35142240), see [`super::protocol`].
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(Primitive::Cylinder(
            super::protocol::cylinder_prepare_primitive(&self.0, gwd),
        ))
    }
}

impl Geometry for CapsuleGeom {
    cylinder_common_slots!();

    /// Slot 1 (0x35020A60).
    fn geom_type(&self) -> GeomType {
        GeomType::Capsule
    }

    /// Slot 7 (0x35141B70), all single precision: `V = ((r·r)·hh)·2π + ((r·r)·r)·4/3π`,
    /// `Ix = Iy = (hh² + 0.4·r²)·Vs + (((hh²·hh)·(r²·π))·2/3 + ((r²·r²)·(hh·π))·0.5)`,
    /// `Iz = (r²·Vs)·0.4 + 2·((r²·r²)·(hh·π))·0.5` with `Vs` the spheres' volume.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        Some(cyl_props(&self.0, true))
    }

    /// Slot 8 (0x3513F7A0): `min(max(|d − a·h|² − r², |h| − hh), |d − a·sgnnz(h)·hh|² − r²)`
    /// (idioms, sign bit).
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        let p = &self.0.prim;
        let d = pt - p.center;
        let h = d.dot(p.axis);
        let s = sgnnz(h) as f32 * p.hh;
        let r2 = p.r * p.r;
        let v7 = (d - p.axis * h).len2() - r2;
        let v8 = h.abs() - p.hh;
        let v9 = (v7 - v8).abs() + (v7 + v8);
        let v10 = (d - p.axis * s).len2() - r2;
        (min_idiom(v10, v9 * 0.5).to_bits() >> 31) as i32
    }

    /// Slot 13 (thunk 0x3513F790 → cylinder 0x35143230 with the capsule's length rule).
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        super::buoyancy::cylinder_buoyancy(&self.0, true, plane, gwd)
    }

    /// Slot 14 (0x35140EA0), see [`super::drag`].
    fn calculate_medium_resistance_acc(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        super::drag::capsule_medium_resistance(&self.0, plane, gwd, dp, dl);
    }

    /// Slot 31 (0x35038B80): `(float)((double)((r·r)·hh)·2π + (double)((r·r)·r)·4/3π)`.
    fn volume(&self) -> f32 {
        let p = &self.0.prim;
        let a = f64::from((p.r * p.r) * p.hh) * TWO_PI_F64;
        let b = f64::from((p.r * p.r) * p.r) * super::sphere::SPHERE_K_F64;
        (a + b) as f32
    }

    /// Slot 11 (0x3513FCA0), see [`super::closest`].
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
        super::closest::capsule_find_closest_point(
            &self.0,
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }

    /// Slot 48 (0x35038B60): the cylinder's transform, reported as a capsule.
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(Primitive::Capsule(
            super::protocol::cylinder_prepare_primitive(&self.0, gwd),
        ))
    }
}
