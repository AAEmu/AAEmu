//! Slot 14 `CalculateMediumResistance(plane, gwd, dP&, dL&)` (`geometry.md` §6.4).

use super::cylinder::CylinderData;
use super::{BoxGeom, SphereGeom};
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

/// Box slot 14 (0x3513E7C0).
pub fn box_medium_resistance(
    _g: &BoxGeom,
    _plane: &Plane,
    _gwd: &GeomWorldData,
    _dp: &mut Vec3,
    _dl: &mut Vec3,
) {
    todo!("P1a: port 0x3513E7C0")
}

/// Sphere slot 14 (0x351AFCB0).
pub fn sphere_medium_resistance(
    _g: &SphereGeom,
    _plane: &Plane,
    _gwd: &GeomWorldData,
    _dp: &mut Vec3,
    _dl: &mut Vec3,
) {
    todo!("P1a: port 0x351AFCB0")
}

/// Cylinder slot 14 (0x35146F40).
pub fn cylinder_medium_resistance(
    _d: &CylinderData,
    _plane: &Plane,
    _gwd: &GeomWorldData,
    _dp: &mut Vec3,
    _dl: &mut Vec3,
) {
    todo!("P1a: port 0x35146F40")
}

/// Capsule slot 14 (0x35140EA0).
pub fn capsule_medium_resistance(
    _d: &CylinderData,
    _plane: &Plane,
    _gwd: &GeomWorldData,
    _dp: &mut Vec3,
    _dl: &mut Vec3,
) {
    todo!("P1a: port 0x35140EA0")
}
