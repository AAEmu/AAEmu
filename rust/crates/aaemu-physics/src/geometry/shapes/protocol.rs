//! Intersection-test helpers of the primitives: slot 48 `PreparePrimitive` and the
//! geometry-under-test protocol slots 49/53/56/57/58/59 (`geometry.md` §2.1, §2.3).

use super::cylinder::CylinderData;
use super::{BoxGeom, RayGeom, SphereGeom};
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim};
use crate::geometry::GeomWorldData;

/// Box slot 48 (0x3513B580).
pub fn box_prepare_primitive(_g: &BoxGeom, _gwd: &GeomWorldData) -> Primitive {
    let _: Option<BoxPrim> = None;
    todo!("P1a: port 0x3513B580")
}

/// Sphere slot 48 (0x351AF380).
pub fn sphere_prepare_primitive(_g: &SphereGeom, _gwd: &GeomWorldData) -> Primitive {
    let _: Option<SpherePrim> = None;
    todo!("P1a: port 0x351AF380")
}

/// Cylinder slot 48 (0x35142240), also the capsule's (0x35038B60).
pub fn cylinder_prepare_primitive(_d: &CylinderData, _gwd: &GeomWorldData) -> CylinderPrim {
    todo!("P1a: port 0x35142240")
}

/// Ray slot 48 (0x3519A020).
pub fn ray_prepare_primitive(_g: &RayGeom, _gwd: &GeomWorldData) -> RayPrim {
    todo!("P1a: port 0x3519A020")
}
