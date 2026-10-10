//! Box transforms shared by the BV trees, and the swept box of sub_35135490.

use crate::geometry::primitives::BoxPrim;
use crate::math::{Mat33, Vec3, QUOTIENT_EPS};

/// A box (in the tree frame) placed into another frame: `basis·Rᵀ`, `bOriented = 1`,
/// `center = (R·c)·scale + offset`, `size·scale` (slot 9 of every box tree, e.g. 0x351AE3F0).
#[must_use]
pub fn transform_box(
    basis: &Mat33,
    center: Vec3,
    size: Vec3,
    r: &Mat33,
    offset: Vec3,
    scale: f32,
) -> BoxPrim {
    BoxPrim {
        basis: basis.mul_mat(&r.transpose()),
        oriented: 1,
        center: r.mul_vec(center) * scale + offset,
        size: size * scale,
    }
}

/// sub_35135490: the box `b` swept along the unit direction `d` by `len` (signed).
///
/// The result is oriented with its third axis along `d`; its first axis is the component
/// perpendicular to `d` of the box axis with the largest `|axis − d·(d·axis)|²·size` (normalised;
/// `(0, 0, 1)` when that is ≤ 1e-20), the second is `d × first`. Its half-sizes are the box's
/// extents along these axes, plus `|len|/2` along `d`, and its centre moves by `d·len/2`.
/// All single precision.
#[must_use]
pub fn sweep_box(b: &BoxPrim, d: Vec3, len: f32) -> BoxPrim {
    let r = &b.basis.m;
    let s = b.size;
    let perp2 = |row: usize| -> f32 {
        let p = (d.x * r[row][0] + d.y * r[row][1]) + d.z * r[row][2];
        let a = r[row][0] - d.x * p;
        let bb = r[row][1] - d.y * p;
        let c = r[row][2] - d.z * p;
        (a * a + bb * bb) + c * c
    };
    let e0 = perp2(0) * s.x;
    let e1 = perp2(1) * s.y;
    let e2 = perp2(2) * s.z;
    let i = ((e0 - e1).to_bits() >> 31) as i32;
    let m = ((e1 - e0).abs() + (e1 + e0)) * 0.5;
    let j = ((m - e2).to_bits() >> 31) as i32;
    let a = i | (j << 1);
    let idx = (a & (((a ^ 2) | 4) >> 1)) as usize;

    let p = (d.x * r[idx][0] + d.y * r[idx][1]) + d.z * r[idx][2];
    let v21 = r[idx][0] - d.x * p;
    let v22 = r[idx][1] - d.y * p;
    let v23 = r[idx][2] - d.z * p;
    let l2 = (v21 * v21 + v22 * v22) + v23 * v23;
    let row0 = if l2 <= QUOTIENT_EPS {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(v21 * inv, v22 * inv, v23 * inv)
    };
    let row1 = Vec3::new(
        row0.z * d.y - row0.y * d.z,
        row0.x * d.z - row0.z * d.x,
        row0.y * d.x - row0.x * d.y,
    );
    let basis = Mat33::from_rows(row0, row1, d);
    let m = basis.mul_mat(&b.basis.transpose()).m;
    let half = len * 0.5;
    BoxPrim {
        basis,
        oriented: 1,
        center: Vec3::new(
            d.x * half + b.center.x,
            d.y * half + b.center.y,
            d.z * half + b.center.z,
        ),
        size: Vec3::new(
            (s.x * m[0][0].abs() + s.y * m[0][1].abs()) + s.z * m[0][2].abs(),
            (s.x * m[1][0].abs() + s.y * m[1][1].abs()) + s.z * m[1][2].abs(),
            len.abs() * 0.5 + ((s.x * m[2][0].abs() + s.y * m[2][1].abs()) + s.z * m[2][2].abs()),
        ),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sweep_axis_aligned_box_along_x() {
        let b = BoxPrim::aligned(Vec3::new(1.0, 2.0, 3.0), Vec3::new(1.0, 2.0, 3.0));
        let s = sweep_box(&b, Vec3::X, 4.0);
        // Third axis = x; first = the box axis with the largest perpendicular extent (z).
        assert_eq!(s.basis.row(2), Vec3::X);
        assert_eq!(s.basis.row(0), Vec3::Z);
        assert_eq!(s.basis.row(1), Vec3::new(0.0, -1.0, 0.0));
        assert_eq!(s.size, Vec3::new(3.0, 2.0, 3.0));
        assert_eq!(s.center, Vec3::new(3.0, 2.0, 3.0));
        assert_eq!(s.oriented, 1);
    }

    #[test]
    fn transform_identity() {
        let b = transform_box(
            &Mat33::IDENTITY,
            Vec3::new(1.0, 2.0, 3.0),
            Vec3::splat(1.0),
            &Mat33::IDENTITY,
            Vec3::new(10.0, 0.0, 0.0),
            2.0,
        );
        assert_eq!(b.center, Vec3::new(12.0, 4.0, 6.0));
        assert_eq!(b.size, Vec3::splat(2.0));
    }
}
