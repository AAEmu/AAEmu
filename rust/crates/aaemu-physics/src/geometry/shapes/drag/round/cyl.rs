//! Cylinder slot 14 (0x35146F40) and CircleClipByLine (0x351440E0).

use super::super::super::cylinder::CylinderData;
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

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

/// `sub_351440E0` CircleClipByLine (inferred name).
pub fn circle_clip_by_line() {
    todo!("P1a: port 0x351440E0")
}
