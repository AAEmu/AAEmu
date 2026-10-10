//! Slots 20 `GetFeature`, 43 `GetRandomPos`, 51 (occlusion cubemap rasteriser), 15
//! `DrawWireframe`.

use super::BoxGeom;
use crate::math::Vec3;

/// Box slot 20 (0x3513A780).
pub fn box_get_feature(_g: &BoxGeom, _i_prim: i32, _i_feature: i32, _pts: &mut [Vec3]) -> i32 {
    todo!("P1a: port 0x3513A780")
}
