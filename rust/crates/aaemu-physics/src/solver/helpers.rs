//! Small math helpers of the solver, each a literal port of one routine of the binary (operation
//! order as in the disassembly). Matrices are row-major `[f32; 9]`.

use crate::math::{crt, Quat, Vec3};

/// `sub_35020320`: `a = a · b`, element `[i][j] = (a[i][1]·b[1][j] + a[i][0]·b[0][j]) +
/// a[i][2]·b[2][j]`.
#[must_use]
pub fn mat_mul(a: &[f32; 9], b: &[f32; 9]) -> [f32; 9] {
    let mut r = [0.0; 9];
    for i in 0..3 {
        for j in 0..3 {
            r[3 * i + j] =
                (a[3 * i + 1] * b[3 + j] + a[3 * i] * b[j]) + a[3 * i + 2] * b[6 + j];
        }
    }
    r
}

/// `sub_35098FA0`: add the point inverse-mass matrix of one body to `k`:
/// `K = (K − [r]ₓ·Iinv·[r]ₓ) + Minv·I` (the skew products through `sub_35020320`).
pub fn k_accum(k: &mut [f32; 9], iinv: &[f32; 9], minv: f32, r: [f32; 3]) {
    let (x, y, z) = (r[0], r[1], r[2]);
    let s = [0.0, -z, y, z, 0.0, -x, -y, x, 0.0];
    let m = mat_mul(&mat_mul(&s, iinv), &s);
    for i in 0..9 {
        k[i] -= m[i];
    }
    k[0] += minv;
    k[4] += minv;
    k[8] += minv;
}

/// `sub_3501FCD0`: in-place 3×3 inverse by cofactors; returns false (with `a` = the adjugate)
/// when `|det| < 1e-20`.
pub fn inv33(a: &mut [f32; 9]) -> bool {
    let (v1, v2, v3, v4) = (a[4], a[7], a[5], a[8]);
    let (v5, v6, v7, v8, v9) = (a[2], a[3], a[6], a[1], a[0]);
    let v10 = v1 * v4 - v2 * v3;
    let v11 = v5 * v2 - v8 * v4;
    let v12 = v8 * v3 - v5 * v1;
    let v13 = v7 * v3 - v6 * v4;
    let v14 = v9 * v4 - v7 * v5;
    let v15 = v6 * v5 - v9 * v3;
    let v16 = v6 * v2 - v7 * v1;
    let v17 = v7 * v8 - v9 * v2;
    let v18 = v9 * v1 - v6 * v8;
    *a = [v10, v11, v12, v13, v14, v15, v16, v17, v18];
    let det = (v11 * v6 + v10 * v9) + v12 * v7;
    if det.abs() < f32::from_bits(0x1E3C_E508) {
        return false;
    }
    let inv = 1.0 / det;
    for x in a.iter_mut() {
        *x *= inv;
    }
    true
}

/// `sub_3504FC30`: outer product `a·bᵀ`.
#[must_use]
pub fn outer(a: [f32; 3], b: [f32; 3]) -> [f32; 9] {
    [
        a[0] * b[0],
        b[1] * a[0],
        b[2] * a[0],
        a[1] * b[0],
        a[1] * b[1],
        a[1] * b[2],
        b[0] * a[2],
        b[1] * a[2],
        b[2] * a[2],
    ]
}

/// `sub_35020680`: `M·v` as `x = (v.y·m1 + v.x·m0) + v.z·m2`, `y = (v.x·m3 + v.y·m4) + v.z·m5`,
/// `z = (v.x·m6 + v.y·m7) + v.z·m8`.
#[inline]
#[must_use]
pub fn mat_vec(m: &[f32; 9], v: [f32; 3]) -> [f32; 3] {
    [
        (v[1] * m[1] + v[0] * m[0]) + v[2] * m[2],
        (v[0] * m[3] + v[1] * m[4]) + v[2] * m[5],
        (v[0] * m[6] + v[1] * m[7]) + v[2] * m[8],
    ]
}

/// `sub_350210A0`: normalise, `(0, 0, 1)` when `|v|² ≤ 1e-20`.
#[must_use]
pub fn normalize_or_z(v: [f32; 3]) -> [f32; 3] {
    let l2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
    if l2 <= f32::from_bits(0x1E3C_E508) {
        return [0.0, 0.0, 1.0];
    }
    let inv = 1.0 / l2.sqrt();
    [v[0] * inv, inv * v[1], inv * v[2]]
}

/// `sub_35023C00`: rotate `v` by the conjugate of `q`.
#[must_use]
pub fn rotate_inv(q: Quat, v: [f32; 3]) -> [f32; 3] {
    let (x, y, z, w) = (q.x, q.y, q.z, q.w);
    let t0 = (z * v[1] - y * v[2]) + v[0] * w;
    let t1 = (x * v[2] - v[0] * z) + w * v[1];
    let t2 = (v[0] * y - x * v[1]) + w * v[2];
    let u0 = z * t1 - y * t2;
    let u2 = y * t0 - x * t1;
    let u1 = x * t2 - z * t0;
    [(u0 + v[0]) + u0, (u1 + v[1]) + u1, (u2 + v[2]) + u2]
}

/// `sub_35022240` (quaternion rotation, [`Quat::rotate`]) on arrays.
#[inline]
#[must_use]
pub fn rotate(q: Quat, v: [f32; 3]) -> [f32; 3] {
    q.rotate(Vec3::from_array(v)).to_array()
}

/// `sub_35024B50`: rotation of `angle` around the unit `axis` (MSVCR100 `sinf`/`cosf`).
#[must_use]
pub fn quat_axis_angle(angle: f32, axis: [f32; 3]) -> Quat {
    let h = angle * 0.5;
    let s = crt::sinf(angle * 0.5);
    let c = crt::cosf(h);
    Quat::new(axis[0] * s, axis[1] * s, axis[2] * s, c)
}

/// `sub_35020600`: `q / sqrt(((x² + w²) + y²) + z²)`.
#[inline]
#[must_use]
pub fn quat_normalize(q: Quat) -> Quat {
    q.normalized_xwyz()
}

/// The 2×2 case of the generic matrix inverse `sub_35126C90` (row-major `[a, b, c, d]`):
/// unchanged when the determinant is exactly 0.
pub fn inv22(m: &mut [f32; 4]) {
    let det = m[3] * m[0] - m[2] * m[1];
    if det != 0.0 {
        let (a, c, inv, b) = (m[0], m[2], 1.0 / det, m[1]);
        m[0] = m[3] * inv;
        m[1] = -(b * inv);
        m[2] = -(c * inv);
        m[3] = a * inv;
    }
}

/// `max(a, b)` idiom in single precision.
#[inline]
#[must_use]
pub fn fmax(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// `min(a, b)` idiom in single precision.
#[inline]
#[must_use]
pub fn fmin(a: f32, b: f32) -> f32 {
    ((a + b) - (a - b).abs()) * 0.5
}

/// `x >> 31` sign mask of an integer (−1 or 0).
#[inline]
#[must_use]
pub fn sm(x: i32) -> i32 {
    x >> 31
}

/// Branch-free `max(a, b)` on integers: `a − ((a − b) & ((a − b) >> 31))`.
#[inline]
#[must_use]
pub fn imax(a: i32, b: i32) -> i32 {
    let d = a.wrapping_sub(b);
    a.wrapping_sub(d & (d >> 31))
}

/// Branch-free `min(a, b)`: `b + ((a − b) & ((a − b) >> 31))`.
#[inline]
#[must_use]
pub fn imin(a: i32, b: i32) -> i32 {
    let d = a.wrapping_sub(b);
    b.wrapping_add(d & (d >> 31))
}
