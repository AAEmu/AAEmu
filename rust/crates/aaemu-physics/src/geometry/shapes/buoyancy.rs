//! Slots 13 `CalculateBuoyancy` and 12 `CalcVolumetricPressure` (`geometry.md` §6.3, §6.5).

use super::cylinder::CylinderData;
use super::{BoxGeom, SphereGeom};
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

/// Box slot 13 (0x3513B1B0): temporary 12-triangle mesh (`sub_3513AC00`) + mesh slot 13
/// (0x351DA620).
pub fn box_buoyancy(_g: &BoxGeom, _plane: &Plane, _gwd: &GeomWorldData) -> (f32, Vec3) {
    todo!("P1a: port 0x3513B1B0 / 0x3513AC00 / 0x351DA620")
}

/// Sphere slot 13 (0x351AEC00).
pub fn sphere_buoyancy(_g: &SphereGeom, _plane: &Plane, _gwd: &GeomWorldData) -> (f32, Vec3) {
    todo!("P1a: port 0x351AEC00")
}

/// Cylinder slot 13 (0x35143230); the capsule thunk (0x3513F790) passes `capsule = true`.
pub fn cylinder_buoyancy(
    _d: &CylinderData,
    _capsule: bool,
    _plane: &Plane,
    _gwd: &GeomWorldData,
) -> (f32, Vec3) {
    todo!("P1a: port 0x35143230")
}

/// Box slot 12 (0x3513B0F0): temporary mesh + mesh slot 12 (0x351CBE20).
#[allow(clippy::too_many_arguments)]
pub fn box_volumetric_pressure(
    _g: &BoxGeom,
    _gwd: &GeomWorldData,
    _epicenter: Vec3,
    _k: f32,
    _rmin: f32,
    _com: Vec3,
    _p: &mut Vec3,
    _l: &mut Vec3,
) {
    todo!("P1a: port 0x3513B0F0 / 0x351CBE20")
}

/// Sphere slot 12 (0x351AEE70).
#[allow(clippy::too_many_arguments)]
pub fn sphere_volumetric_pressure(
    _g: &SphereGeom,
    _gwd: &GeomWorldData,
    _epicenter: Vec3,
    _k: f32,
    _rmin: f32,
    _com: Vec3,
    _p: &mut Vec3,
    _l: &mut Vec3,
) {
    todo!("P1a: port 0x351AEE70")
}

/// Cylinder / capsule slot 12 (0x35142EE0).
#[allow(clippy::too_many_arguments)]
pub fn cylinder_volumetric_pressure(
    _d: &CylinderData,
    _gwd: &GeomWorldData,
    _epicenter: Vec3,
    _k: f32,
    _rmin: f32,
    _com: Vec3,
    _p: &mut Vec3,
    _l: &mut Vec3,
) {
    todo!("P1a: port 0x35142EE0")
}
