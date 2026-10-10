//! Principal axes of a triangle set (sub_35195400) and the symmetric Jacobi eigen-solver it uses
//! (sub_35122FA0). Used by the OBB builder (node axes) and by the `CTriMesh` constructor (the
//! principal-axes basis of build flags 0x40000 / 0x80).
//!
//! Everything is double precision, in the binary's evaluation order (checked against the
//! disassembly; the Hex-Rays output misplaces the centring subtraction of the second pass).

use crate::math::Vec3;

/// `9.999999999999999e-21` (double).
const EPS20: f64 = f64::from_bits(0x3BC7_9CA1_0C92_4223);

/// sub_35122FA0 for a 3×3 symmetric matrix (the generic `matrix` routine with the symmetric
/// flag 0x20): cyclic Jacobi with the largest off-diagonal pivot. Returns
/// `(V, eigenvalues, iterations)`: `V`'s **rows** are the eigenvectors; `iterations` is 0 when
/// the 90-iteration limit was hit.
///
/// * Pivot: the first `(p, q)`, `p < q`, row by row, with `A[p][q]² > thr` (strict), where
///   `thr = eps` (+1e-20 once, at iteration 10).
/// * `θ = ((A_qq − A_pp)·½) / A_pq`; if `|θ| < 1e20`: `r = √(θ² + 1)`,
///   `t = θ > 0 ? −θ − r : r − θ`, `c = 1/√(t² + 1)`, `s = c·t`; columns, rows and `V`'s rows
///   `p`, `q` are rotated `(x_p·c − x_q·s, x_p·s + x_q·c)`. Then `A[p][q] = 0` (always).
/// * Finally each `V` entry with `|x| < 1e-6` becomes 0, and one within 1e-20 of ±1 becomes
///   exactly ±1.
#[must_use]
#[allow(clippy::needless_range_loop)] // index form mirrors the matrix code
pub fn jacobi3(a_in: &[[f64; 3]; 3], eps: f64) -> ([[f64; 3]; 3], [f64; 3], i32) {
    let n = 3usize;
    let mut a = *a_in;
    let mut v = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]];
    let mut eps = eps;
    let mut iter = 0i32;
    let max_iter = 10 * 3 * 3;
    let mut result;
    loop {
        let mut thr = eps;
        let mut pq: Option<(usize, usize)> = None;
        for p in 0..n - 1 {
            for q in p + 1..n {
                if a[p][q] * a[p][q] > thr {
                    thr = a[p][q] * a[p][q];
                    pq = Some((p, q));
                }
            }
        }
        let Some((p, q)) = pq else {
            result = iter;
            break;
        };
        let theta = (a[q][q] - a[p][p]) * 0.5 / a[p][q];
        if theta.abs() < 1.0e20 {
            let r = (theta * theta + 1.0).sqrt();
            let t = if theta <= 0.0 { r - theta } else { -theta - r };
            let c = 1.0 / (t * t + 1.0).sqrt();
            let s = c * t;
            for row in a.iter_mut() {
                let (xp, xq) = (row[p], row[q]);
                row[p] = xp * c - xq * s;
                row[q] = xp * s + xq * c;
            }
            for k in 0..n {
                let (xp, xq) = (a[p][k], a[q][k]);
                a[p][k] = xp * c - xq * s;
                a[q][k] = xp * s + xq * c;
            }
            for k in 0..n {
                let (xp, xq) = (v[p][k], v[q][k]);
                v[p][k] = xp * c - xq * s;
                v[q][k] = xp * s + xq * c;
            }
        }
        a[p][q] = 0.0;
        if iter == 9 + 1 {
            eps += EPS20;
        }
        iter += 1;
        if iter >= max_iter {
            result = 0;
            break;
        }
    }
    for row in &mut v {
        for x in row.iter_mut() {
            let ax = x.abs();
            if ax >= 0.000_001 {
                if (ax - 1.0).abs() < EPS20 {
                    // `sgnnz((float)x)`.
                    *x = if (*x as f32).is_sign_negative() {
                        -1.0
                    } else {
                        1.0
                    };
                }
            } else {
                *x = 0.0;
            }
        }
    }
    if result < 0 {
        result = 0;
    }
    ([v[0], v[1], v[2]], [a[0][0], a[1][1], a[2][2]], result)
}

/// The output of [`principal_axes`]: the axes (rows) and the area-weighted centroid, double.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PrincipalAxes {
    pub axes: [[f64; 3]; 3],
    pub center: [f64; 3],
}

/// sub_35195400: area-weighted centroid and surface "inertia" eigenvectors of the triangles
/// `indices[3k..3k+3]` (k < `n_tris`) over `vertices`.
///
/// Pass 1: `area = √(A² + B² + C²)·½` of each triangle's cross product; centroid
/// `Σ((v1+v0)+v2)·⅓·area / Σarea`. If `Σarea == 0` (or NaN): centre = the first vertex (zero
/// when `n_tris ≤ 0`), axes = identity. Pass 2 on centred points `p' = p − c`:
/// `C[i][j] += (((S_i·S_j + p0_i·p0_j) + p1_i·p1_j) + p2_i·p2_j)·(area·1/12)` with
/// `S = (p1' + p0') + p2'`, then [`jacobi3`] with `eps = 0`.
#[must_use]
pub fn principal_axes(vertices: &[Vec3], indices: &[i32], n_tris: i32) -> PrincipalAxes {
    let n = n_tris.max(0) as usize;
    let p = |k: usize| -> [f64; 3] {
        let v = vertices[indices[k] as usize];
        [f64::from(v.x), f64::from(v.y), f64::from(v.z)]
    };
    let (mut sx, mut sy, mut sz, mut total) = (0.0f64, 0.0f64, 0.0f64, 0.0f64);
    for t in 0..n {
        let (p0, p1, p2) = (p(3 * t), p(3 * t + 1), p(3 * t + 2));
        let dz1 = p1[2] - p0[2];
        let dx1 = p1[0] - p0[0];
        let dy1 = p1[1] - p0[1];
        let dz2 = p2[2] - p0[2];
        let dx2 = p2[0] - p0[0];
        let dy2 = p2[1] - p0[1];
        let a = dy1 * dz2 - dz1 * dy2;
        let b = dz1 * dx2 - dz2 * dx1;
        let c = dy2 * dx1 - dy1 * dx2;
        let area = ((a * a + b * b) + c * c).sqrt() * 0.5;
        sx += ((p1[0] + p0[0]) + p2[0]) * 0.333_333_333_333_333_3 * area;
        sz += ((p1[2] + p0[2]) + p2[2]) * 0.333_333_333_333_333_3 * area;
        sy += ((p1[1] + p0[1]) + p2[1]) * 0.333_333_333_333_333_3 * area;
        total += area;
    }
    if n == 0 || total == 0.0 || total.is_nan() {
        let c = if n == 0 { [0.0; 3] } else { p(0) };
        return PrincipalAxes {
            axes: [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]],
            center: c,
        };
    }
    let inv = 1.0 / total;
    let c = [sx * inv, sy * inv, sz * inv];
    let mut cov = [[0.0f64; 3]; 3];
    for t in 0..n {
        let q = |k: usize| {
            let v = p(3 * t + k);
            [v[0] - c[0], v[1] - c[1], v[2] - c[2]]
        };
        let (p0, p1, p2) = (q(0), q(1), q(2));
        let dy1 = p1[1] - p0[1];
        let dx1 = p1[0] - p0[0];
        let dz1 = p1[2] - p0[2];
        let dz2 = p2[2] - p0[2];
        let dx2 = p2[0] - p0[0];
        let dy2 = p2[1] - p0[1];
        let b = dz1 * dx2 - dz2 * dx1;
        let a = dy1 * dz2 - dz1 * dy2;
        let cc = dy2 * dx1 - dy1 * dx2;
        let area = ((b * b + a * a) + cc * cc).sqrt() * 0.5;
        let s = [
            (p1[0] + p0[0]) + p2[0],
            (p1[1] + p0[1]) + p2[1],
            (p1[2] + p0[2]) + p2[2],
        ];
        let w = area * 0.083_333_333_333_333_33;
        for i in 0..3 {
            for j in i..3 {
                let v = (((s[i] * s[j] + p0[i] * p0[j]) + p1[i] * p1[j]) + p2[i] * p2[j]) * w;
                cov[i][j] += v;
                if j != i {
                    cov[j][i] += v;
                }
            }
        }
    }
    let (v, _, _) = jacobi3(&cov, 0.0);
    PrincipalAxes { axes: v, center: c }
}

/// The `CTriMesh` constructor's fit for build flags 0x40000 / 0x80 (sub_351CF1B0): the
/// principal axes of the convex hull of **all** the mesh's vertices
/// ([`super::hull::convex_hull`]), or of the triangles themselves when the mesh has ≤ 20
/// triangles or the hull is degenerate.
#[must_use]
pub fn mesh_principal_axes(vertices: &[Vec3], indices: &[i32]) -> PrincipalAxes {
    let n = (indices.len() / 3) as i32;
    if n > 20 {
        if let Some(tris) = super::hull::convex_hull(vertices) {
            let idx: Vec<i32> = tris.iter().flatten().copied().collect();
            return principal_axes(vertices, &idx, tris.len() as i32);
        }
    }
    principal_axes(vertices, indices, n)
}

impl PrincipalAxes {
    /// The axes rounded to single precision (`cvtsd2ss` per element), as the trees store them.
    #[must_use]
    pub fn axes_f32(&self) -> crate::math::Mat33 {
        let a = &self.axes;
        crate::math::Mat33::from_array([
            a[0][0] as f32,
            a[0][1] as f32,
            a[0][2] as f32,
            a[1][0] as f32,
            a[1][1] as f32,
            a[1][2] as f32,
            a[2][0] as f32,
            a[2][1] as f32,
            a[2][2] as f32,
        ])
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn jacobi_diagonalises() {
        let a = [[4.0, 1.0, 0.5], [1.0, 3.0, 0.25], [0.5, 0.25, 1.0]];
        let (v, e, it) = jacobi3(&a, 0.0);
        assert!(it > 0);
        // V·A·Vᵀ ≈ diag(e).
        for i in 0..3 {
            for j in 0..3 {
                let mut s = 0.0;
                for k in 0..3 {
                    for l in 0..3 {
                        s += v[i][k] * a[k][l] * v[j][l];
                    }
                }
                let want = if i == j { e[i] } else { 0.0 };
                assert!((s - want).abs() < 1e-9, "{i}{j}: {s} vs {want}");
            }
        }
    }

    #[test]
    fn identity_stays_identity() {
        let (v, e, it) = jacobi3(&[[2.0, 0.0, 0.0], [0.0, 3.0, 0.0], [0.0, 0.0, 1.0]], 0.0);
        assert_eq!(v, [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]);
        assert_eq!(e, [2.0, 3.0, 1.0]);
        assert_eq!(it, 0);
    }

    #[test]
    fn flat_quad_axes() {
        // A 4×1 rectangle in the xy plane: the normal (z) is an eigenvector.
        let v = [
            Vec3::new(0.0, 0.0, 0.0),
            Vec3::new(4.0, 0.0, 0.0),
            Vec3::new(4.0, 1.0, 0.0),
            Vec3::new(0.0, 1.0, 0.0),
        ];
        let pa = principal_axes(&v, &[0, 1, 2, 0, 2, 3], 2);
        assert_eq!(pa.center, [2.0, 0.5, 0.0]);
        assert!(pa.axes.iter().any(|r| r[2].abs() == 1.0));
    }
}
