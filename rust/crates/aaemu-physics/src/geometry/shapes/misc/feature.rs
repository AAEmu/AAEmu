//! Slot 20 `GetFeature(iPrim, iFeature, Vec3 pts[])` of the box (0x3513A780). The sphere,
//! cylinder, capsule and ray use the `CGeometry` stub (`return 0`).
//!
//! ## Feature codes
//!
//! `iFeature & 0x60` selects the kind, the low bits the instance; `iPrim` is ignored. Points
//! are returned in the box's parent frame: `Basisᵀ·p + center` for the local corner `p`
//! (applied whether or not the box is oriented), each component summed
//! `((p.x·B[0][j] + p.y·B[1][j]) + p.z·B[2][j]) + c[j]`. Signs below are `±1 → ±size`.
//!
//! * **vertex** (`0x00`): bit 0 → x sign, bit 1 → y sign, bit 2 → z sign (set = `+`):
//!   1 point.
//! * **edge** (`0x20`): axis `a = (f >> 2) & 3`, the edge parallel to `a`; bit 0 → sign of
//!   the `inc(a)` coordinate, bit 1 → sign of `dec(a)` (`inc = {1,2,0}`, `dec = {2,0,1}`):
//!   2 points, `−size[a]` then `+size[a]` along the axis.
//! * **face** (`0x40`): axis `a = (f >> 1) & 3`, bit 0 → the `+a` face: 4 points. Corner `k`
//!   (`k = 0..3`) has `(inc, dec)` signs `(+,+), (−,+), (−,−), (+,−)` and is stored at index
//!   `k ^ (sgn & 3)` — i.e. `[(−,+), (+,+), (+,−), (−,−)]` for a `+` face and the reversed
//!   order `[(+,−), (−,−), (−,+), (+,+)]` for a `−` face (both counter-clockwise seen from
//!   outside).
//! * anything else (`0x60`): 0 points.
//!
//! ## Quirk kept: axis index 3
//!
//! The axis fields are two bits wide but there are only three axes. The index tables
//! (0x35209A50 `{1,2,0,0}`, 0x35209A60 `{2,0,1,0}`) give `inc(3) = dec(3) = 0`, and
//! `size[3]` reads the dword after the size, the low half of the embedded `CSingleBoxTree`'s
//! vptr ([`BOX_TREE_VPTR_LO`] with the DLL at its preferred base). The writes then overlap and
//! spill past the points the function reports: an edge with axis 3 writes `pts[2].x`, a face
//! with axis 3 writes up to `pts[4].x`, and coordinates the code does not write keep the
//! caller's values. [`box_get_feature`] reproduces all of this on the first five points of
//! `pts` (stray writes beyond `pts.len()` are dropped).

use crate::geometry::shapes::BoxGeom;
use crate::math::Vec3;

/// Low dword of the `CSingleBoxTree` vptr embedded at box `+112` (0x351EE4A8 at the image base
/// 0x35000000), read as a float by slot 20 for axis index 3.
pub const BOX_TREE_VPTR_LO: u32 = 0x351E_E4A8;

/// 0x35209A50 / 0x35209A60 (four entries each, the fourth 0).
const INC4: [usize; 4] = [1, 2, 0, 0];
const DEC4: [usize; 4] = [2, 0, 1, 0];

/// Box slot 20 (0x3513A780): see the module docs for the feature numbering. Returns the number
/// of points (1, 2, 4 or 0).
pub fn box_get_feature(g: &BoxGeom, _i_prim: i32, i_feature: i32, pts: &mut [Vec3]) -> i32 {
    let mut a = [0f32; 15];
    let n = pts.len().min(5);
    for (k, p) in pts.iter().take(n).enumerate() {
        a[3 * k] = p.x;
        a[3 * k + 1] = p.y;
        a[3 * k + 2] = p.z;
    }
    let sz = g.prim.size;
    let size = |i: usize| -> f32 {
        if i < 3 {
            sz[i]
        } else {
            f32::from_bits(BOX_TREE_VPTR_LO)
        }
    };
    let b = &g.prim.basis.m;
    let c = g.prim.center;
    let xform = |a: &mut [f32; 15], o: usize| {
        let (x, y, z) = (a[o], a[o + 1], a[o + 2]);
        a[o] = ((x * b[0][0] + y * b[1][0]) + z * b[2][0]) + c.x;
        a[o + 1] = ((x * b[0][1] + y * b[1][1]) + z * b[2][1]) + c.y;
        a[o + 2] = ((x * b[0][2] + y * b[1][2]) + z * b[2][2]) + c.z;
    };
    let f = i_feature;
    let r = match f & 0x60 {
        0 => {
            a[0] = (2 * (f & 1) - 1) as f32 * size(0);
            a[1] = ((f & 2) - 1) as f32 * size(1);
            a[2] = (((f >> 1) & 2) - 1) as f32 * size(2);
            xform(&mut a, 0);
            1
        }
        0x20 => {
            let ax = ((f >> 2) & 3) as usize;
            let (t2, t1) = (DEC4[ax], INC4[ax]);
            let v1 = (2 * (f & 1) - 1) as f32 * size(t1);
            a[t1 + 3] = v1;
            a[t1] = v1;
            let v2 = ((f & 2) - 1) as f32 * size(t2);
            a[t2 + 3] = v2;
            a[t2] = v2;
            a[ax] = -size(ax);
            a[ax + 3] = size(ax);
            xform(&mut a, 0);
            xform(&mut a, 3);
            2
        }
        0x40 => {
            let ax = ((f >> 1) & 3) as usize;
            let sgn = 2 * (f & 1) - 1;
            let (t1, t2) = (INC4[ax], DEC4[ax]);
            let rot = (sgn & 3) as usize;
            let sa = sgn as f32;
            for k in 0..4usize {
                let o = 3 * (k ^ rot);
                let ki = k as i32;
                a[o + t1] = (1 - ((ki ^ (2 * ki)) & 2)) as f32 * size(t1);
                a[o + t2] = (1 - (ki & 2)) as f32 * size(t2);
                a[o + ax] = sa * size(ax);
                xform(&mut a, o);
            }
            4
        }
        _ => 0,
    };
    for (k, p) in pts.iter_mut().take(n).enumerate() {
        *p = Vec3::new(a[3 * k], a[3 * k + 1], a[3 * k + 2]);
    }
    r
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::BoxPrim;
    use crate::math::Mat33;

    fn unit_box() -> BoxGeom {
        BoxGeom::new(&BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: Vec3::new(10.0, 20.0, 30.0),
            size: Vec3::new(1.0, 2.0, 3.0),
        })
    }

    #[test]
    fn numbering() {
        let g = unit_box();
        let mut p = [Vec3::ZERO; 5];
        assert_eq!(box_get_feature(&g, 0, 0b101, &mut p), 1);
        assert_eq!(p[0], Vec3::new(11.0, 18.0, 33.0));
        // edge along y (axis 1): inc = z (bit 0), dec = x (bit 1)
        assert_eq!(box_get_feature(&g, 0, 0x20 | (1 << 2) | 0b01, &mut p), 2);
        assert_eq!(p[0], Vec3::new(9.0, 18.0, 33.0));
        assert_eq!(p[1], Vec3::new(9.0, 22.0, 33.0));
        // +z face
        assert_eq!(box_get_feature(&g, 0, 0x40 | (2 << 1) | 1, &mut p), 4);
        assert_eq!(p[0], Vec3::new(9.0, 22.0, 33.0));
        assert_eq!(p[1], Vec3::new(11.0, 22.0, 33.0));
        assert_eq!(p[2], Vec3::new(11.0, 18.0, 33.0));
        assert_eq!(p[3], Vec3::new(9.0, 18.0, 33.0));
        assert_eq!(box_get_feature(&g, 0, 0x60, &mut p), 0);
    }

    #[test]
    fn axis_three_spills() {
        let g = unit_box();
        let mut p = [Vec3::splat(7.0); 5];
        assert_eq!(box_get_feature(&g, 0, 0x20 | (3 << 2), &mut p), 2);
        // pts[2].x got `size[3]` before nothing transformed it
        assert_eq!(p[2].x, f32::from_bits(BOX_TREE_VPTR_LO));
        assert_eq!(p[2].y, 7.0);
    }
}
