//! Slot 11 `FindClosestPoint(gwd, iPrim&, iFeature&, ptdst0, ptdst1, ptres[2], nMaxIters)`.

use super::cylinder::CylinderData;
use super::{BoxGeom, SphereGeom};
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

/// Box slot 11 (0x3513D020).
#[allow(clippy::too_many_arguments)]
pub fn box_find_closest_point(
    _g: &BoxGeom,
    _gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    _ptdst0: Vec3,
    _ptdst1: Vec3,
    _ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    todo!("P1a: port 0x3513D020")
}

/// Sphere slot 11 (0x351AF660).
#[allow(clippy::too_many_arguments)]
pub fn sphere_find_closest_point(
    _g: &SphereGeom,
    _gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    _ptdst0: Vec3,
    _ptdst1: Vec3,
    _ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    todo!("P1a: port 0x351AF660")
}

/// Cylinder slot 11 (0x35145CC0).
#[allow(clippy::too_many_arguments)]
pub fn cylinder_find_closest_point(
    _d: &CylinderData,
    _gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    _ptdst0: Vec3,
    _ptdst1: Vec3,
    _ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    todo!("P1a: port 0x35145CC0")
}

/// Capsule slot 11 (0x3513FCA0).
#[allow(clippy::too_many_arguments)]
pub fn capsule_find_closest_point(
    _d: &CylinderData,
    _gwd: &GeomWorldData,
    _i_prim: &mut i32,
    _i_feature: &mut i32,
    _ptdst0: Vec3,
    _ptdst1: Vec3,
    _ptres: &mut [Vec3; 2],
    _n_max_iters: i32,
) -> i32 {
    todo!("P1a: port 0x3513FCA0")
}
