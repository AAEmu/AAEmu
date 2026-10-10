//! `CRayGeom` (sizeof 112): `+48` origin, `+60` dir (full segment vector), `+72` ndir
//! (normalised; zero length → (0,0,1)), `+88` embedded `CRayBV` (its `+16` points at the
//! primitive), `+104` pointer to `+48`.
//!
//! Header: `m_minVtxDist = 1.0`, priority 0, `+24 = 0`. Most slots are the `CGeometry`
//! base (slot 7 returns 0, slot 8 returns -1, volume 0, no buoyancy / drag).

use super::{GeomHeader, PrimitiveGeom};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::primitives::{BoxPrim, Plane, Primitive, RayPrim};
use crate::geometry::{GeomWorldData, Geometry, PhysProperties};
use crate::ids::GeomType;
use crate::math::{Mat33, Vec3};

/// `CRayGeom`.
#[derive(Clone, Debug, PartialEq)]
pub struct RayGeom {
    pub hdr: GeomHeader,
    /// `+48`.
    pub prim: RayPrim,
    /// `+72`.
    pub ndir: Vec3,
}

/// `ndir` as the ray ctor / `SetData` / `PrepareForRayTest` compute it: `|d|² =
/// (dx² + dy²) + dz²`; if `<= 1e-20` → `(0, 0, 1)`, else `d · (1/sqrtss(|d|²))`.
#[must_use]
pub fn ray_ndir(d: Vec3) -> Vec3 {
    let l2 = d.len2();
    if l2 <= 1e-20 {
        Vec3::Z
    } else {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(d.x * inv, inv * d.y, inv * d.z)
    }
}

impl RayGeom {
    /// `CreatePrimitive(3, prim)`: the ray ctor `sub_3514D950` (copies the primitive,
    /// computes `ndir`, binds the `CRayBV`).
    #[must_use]
    pub fn new(prim: &RayPrim) -> Self {
        Self {
            hdr: GeomHeader {
                min_vtx_dist: 1.0,
                priority: 0,
                flag24: 0,
                foreign_data: 0,
                i_foreign_data: 0,
            },
            prim: *prim,
            ndir: ray_ndir(prim.dir),
        }
    }

    /// Slot 30 `SetData` (0x35038EE0): `ndir` first, then the primitive.
    pub fn set_data(&mut self, prim: &RayPrim) {
        self.ndir = ray_ndir(prim.dir);
        self.prim = *prim;
    }

    /// Slot 22 `PrepareForRayTest(raylen)` (0x35038FA0): recomputes `ndir` from `dir`;
    /// returns 1.
    pub fn prepare_for_ray_test(&mut self, _raylen: f32) -> bool {
        self.ndir = ray_ndir(self.prim.dir);
        true
    }
}

impl PrimitiveGeom for RayGeom {
    fn header(&self) -> &GeomHeader {
        &self.hdr
    }
}

impl Geometry for RayGeom {
    /// Slot 1 (0x35024CD0).
    fn geom_type(&self) -> GeomType {
        GeomType::Ray
    }

    /// Slot 6 (0x35199F50): identity basis, `bOriented` 0, centre `d·0.5 + o`, half-size
    /// `|d|·0.5`.
    fn bbox(&self) -> BoxPrim {
        let (o, d) = (self.prim.origin, self.prim.dir);
        BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: Vec3::new(d.x * 0.5 + o.x, d.y * 0.5 + o.y, d.z * 0.5 + o.z),
            size: Vec3::new(d.x.abs() * 0.5, d.y.abs() * 0.5, d.z.abs() * 0.5),
        }
    }

    /// Slot 7: base `return 0`.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        None
    }

    /// Slot 8: base `return -1`.
    fn point_inside_status(&self, _pt: Vec3) -> i32 {
        -1
    }

    /// Slot 13: base 0x35037800 zeroes the centre and returns the integer 0 in `eax`; the float
    /// result register `xmm0` is left as it was (garbage for the caller). The port returns 0.
    fn calculate_buoyancy(&self, _plane: &Plane, _gwd: &GeomWorldData) -> (f32, Vec3) {
        (0.0, Vec3::ZERO)
    }

    /// Slot 14: base 0x35037810 zeroes `dP` and `dL`.
    fn calculate_medium_resistance(&self, _plane: &Plane, _gwd: &GeomWorldData) -> (Vec3, Vec3) {
        (Vec3::ZERO, Vec3::ZERO)
    }

    /// Slot 14: base 0x35037810 **assigns** zero to both accumulators.
    fn calculate_medium_resistance_acc(
        &self,
        _plane: &Plane,
        _gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        *dp = Vec3::ZERO;
        *dl = Vec3::ZERO;
    }

    /// Slot 17 (0x35038F70): `{origin, dir}`.
    fn primitive(&self, _i_prim: i32) -> Option<Primitive> {
        Some(Primitive::Ray(self.prim))
    }

    /// Slot 31: base `0.0`.
    fn volume(&self) -> f32 {
        0.0
    }

    /// Slot 32 (0x35038E70): `d·0.5 + o`.
    fn center(&self) -> Vec3 {
        let (o, d) = (self.prim.origin, self.prim.dir);
        Vec3::new(d.x * 0.5 + o.x, d.y * 0.5 + o.y, d.z * 0.5 + o.z)
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

    /// Slot 10: the ray uses the base `CGeometry::Intersect` 0x3514FD70 (P2a/P2b), not wired.
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

    /// Slot 48 (0x3519A020), see [`super::protocol`].
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(Primitive::Ray(super::protocol::ray_prepare_primitive(
            self, gwd,
        )))
    }

    /// Slot 52 (0x35038E60).
    fn size_of_object(&self) -> i32 {
        112
    }
}
