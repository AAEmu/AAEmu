//! Symmetric eigen-decomposition: the cyclic-max Jacobi solver `matrixT<float>::jacobi`
//! (`sub_3511EAA0`), bit-exact.
//!
//! The binary works on a general `n×n` `matrixT<float>` (`{nRows, nCols, flags, data*}`); its
//! only physics caller, `RigidBody::Add` (0x3509D460), passes a 3×3 inertia tensor with a
//! threshold of 0. The loops that the compiler unrolled by four (`n ≥ 4`) evaluate every
//! element with the same operations as the scalar tails, so the scalar form here is exact for
//! any `N`.
//!
//! Algorithm (per iteration):
//! 1. Search the strict upper triangle row by row for the largest `a[i][j]²` that is
//!    **strictly** greater than the running maximum, which starts at the threshold (first hit
//!    wins ties; NaN never wins). None → done, return the iteration count.
//! 2. `θ = ((a[q][q] − a[p][p])·0.5) / a[p][q]`. Only when `!(|θ| ≥ 1e10)` (NaN rotates):
//!    `r = sqrt(θ² + 1)`, `t = θ > 0 ? −θ − r : r − θ`, `c = 1/sqrt(t² + 1)`, `s = c·t`, then
//!    rotate the columns `p, q` of `a`, the rows `p, q` of `a`, and the rows `p, q` of the
//!    eigenvector matrix `V` (initially identity), each pair as
//!    `x_p' = x_p·c − x_q·s`, `x_q' = x_p·s + x_q·c`.
//! 3. `a[p][q] = 0` (only the upper element; the lower one keeps its rotated value and is
//!    never read by the search).
//! 4. On iteration `n² + 1` the threshold grows by `1e-10`; after `10·n²` iterations the
//!    solver gives up and returns 0 (the results are still written).
//!
//! Afterwards every element of `V` with `!(|v| ≥ 1e-6)` (NaN included) becomes `+0`, and
//! elements with `|(|v|) − 1| < 1e-10` become `±1` (a no-op in single precision, kept for
//! fidelity). The eigenvalues are the diagonal of the rotated copy of `a`; the eigenvectors
//! are the **rows** of `V`.

/// Result of [`jacobi_sym`].
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Jacobi<const N: usize> {
    /// The binary's return value: iterations used, or 0 when the iteration limit was hit.
    pub iters: i32,
    /// Eigenvalues (diagonal of the rotated matrix).
    pub values: [f32; N],
    /// Eigenvectors as rows (`V[k]` belongs to `values[k]`), row-major.
    pub vectors: [[f32; N]; N],
}

/// `sub_3511EAA0` `matrixT<float>::jacobi(eigenvectors&, eigenvalues*, threshold)` on a
/// symmetric `N×N` matrix (row-major). See the module docs.
#[must_use]
#[allow(clippy::needless_range_loop, clippy::neg_cmp_op_on_partial_ord)]
pub fn jacobi_sym<const N: usize>(m: &[[f32; N]; N], threshold: f32) -> Jacobi<N> {
    let n = N as i32;
    let mut a = *m;
    let mut v = [[0.0f32; N]; N];
    for (i, row) in v.iter_mut().enumerate() {
        row[i] = 1.0;
    }
    let mut thr = threshold;
    let limit = 10 * n * n;
    let mut iters = 0i32;
    if limit > 0 {
        let mut iter = 0i32;
        loop {
            if n - 1 <= 0 {
                iters = 0;
                break;
            }
            let mut best = thr;
            let mut found: Option<(usize, usize)> = None;
            for i in 0..N - 1 {
                for j in i + 1..N {
                    let sq = a[i][j] * a[i][j];
                    if sq > best {
                        best = sq;
                        found = Some((i, j));
                    }
                }
            }
            let Some((p, q)) = found else {
                iters = iter;
                break;
            };
            let theta = ((a[q][q] - a[p][p]) * 0.5) / a[p][q];
            if !(theta.abs() >= 1.0e10) {
                let r = (theta * theta + 1.0).sqrt();
                let t = if theta > 0.0 { -theta - r } else { r - theta };
                let c = 1.0 / (t * t + 1.0).sqrt();
                let s = c * t;
                for row in a.iter_mut() {
                    let (xp, xq) = (row[p], row[q]);
                    row[p] = xp * c - xq * s;
                    row[q] = xp * s + xq * c;
                }
                for k in 0..N {
                    let (xp, xq) = (a[p][k], a[q][k]);
                    a[p][k] = xp * c - xq * s;
                    a[q][k] = xp * s + xq * c;
                }
                for k in 0..N {
                    let (xp, xq) = (v[p][k], v[q][k]);
                    v[p][k] = xp * c - xq * s;
                    v[q][k] = xp * s + xq * c;
                }
            }
            a[p][q] = 0.0;
            if iter == n * n + 1 {
                thr += f32::from_bits(0x2EDB_E6FF); // 1e-10
            }
            iter += 1;
            if iter >= limit {
                iters = 0;
                break;
            }
        }
    }
    for row in v.iter_mut() {
        for x in row.iter_mut() {
            let ax = x.abs();
            if !(ax >= f32::from_bits(0x3586_37BD)) {
                // 1e-6
                *x = 0.0;
            } else if !((ax - 1.0).abs() >= f32::from_bits(0x2EDB_E6FF)) {
                // sign(x)·1 from the bits: ((bits >> 30) & ~1) + 1
                *x = (((x.to_bits() as i32 >> 30) & !1) + 1) as f32;
            }
        }
    }
    let mut values = [0.0f32; N];
    for (k, val) in values.iter_mut().enumerate() {
        *val = a[k][k];
    }
    Jacobi {
        iters,
        values,
        vectors: v,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[allow(clippy::needless_range_loop)]
    fn check_decomposition(m: &[[f32; 3]; 3], r: &Jacobi<3>, tol: f32) {
        // V·M·Vᵀ ≈ diag(values)
        for i in 0..3 {
            for j in 0..3 {
                let mut s = 0.0f64;
                for k in 0..3 {
                    for l in 0..3 {
                        s += f64::from(r.vectors[i][k])
                            * f64::from(m[k][l])
                            * f64::from(r.vectors[j][l]);
                    }
                }
                let want = if i == j { f64::from(r.values[i]) } else { 0.0 };
                assert!(
                    (s - want).abs() <= f64::from(tol),
                    "[{i}][{j}] {s} vs {want}"
                );
            }
        }
    }

    #[test]
    fn diagonal_matrix_needs_no_rotation() {
        let m = [[3.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 2.0]];
        let r = jacobi_sym(&m, 0.0);
        assert_eq!(r.iters, 0);
        assert_eq!(r.values, [3.0, 1.0, 2.0]);
        assert_eq!(
            r.vectors,
            [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
        );
    }

    #[test]
    fn zero_and_degenerate_matrices() {
        let r = jacobi_sym(&[[0.0; 3]; 3], 0.0);
        assert_eq!(r.iters, 0);
        assert_eq!(r.values, [0.0; 3]);
        // rank 1: all ones
        let m = [[1.0, 1.0, 1.0], [1.0, 1.0, 1.0], [1.0, 1.0, 1.0]];
        let r = jacobi_sym(&m, 0.0);
        assert!(r.iters > 0);
        let mut v = r.values;
        v.sort_by(f32::total_cmp);
        assert!(
            v[0].abs() < 1e-6 && v[1].abs() < 1e-6 && (v[2] - 3.0).abs() < 1e-5,
            "{v:?}"
        );
        check_decomposition(&m, &r, 1e-5);
    }

    #[test]
    fn single_off_diagonal_pair_is_one_rotation() {
        // [[2,1],[1,2]] block: θ = 0 → t = 1, c = s = 1/√2, eigenvalues 1 and 3.
        let m = [[2.0, 1.0, 0.0], [1.0, 2.0, 0.0], [0.0, 0.0, 5.0]];
        let r = jacobi_sym(&m, 0.0);
        assert_eq!(r.iters, 1);
        let c = 1.0f32 / 2.0f32.sqrt();
        assert_eq!(r.vectors[0], [c, -c, 0.0]);
        assert_eq!(r.vectors[1], [c, c, 0.0]);
        assert_eq!(r.vectors[2], [0.0, 0.0, 1.0]);
        // a_pp' = (2c − 1·c)·c − (2c − c)·... evaluated in f32 like the binary
        let app = (2.0 * c - c) * c - (1.0 * c - 2.0 * c) * c;
        assert_eq!(r.values[0], app);
        check_decomposition(&m, &r, 1e-6);
    }

    #[test]
    fn repeated_eigenvalues() {
        // eigenvalues 1, 1, 4 (rotated): [[2,1,1],[1,2,1],[1,1,2]]
        let m = [[2.0, 1.0, 1.0], [1.0, 2.0, 1.0], [1.0, 1.0, 2.0]];
        let r = jacobi_sym(&m, 0.0);
        assert!(r.iters > 0);
        let mut v = r.values;
        v.sort_by(f32::total_cmp);
        assert!(
            (v[0] - 1.0).abs() < 1e-5 && (v[1] - 1.0).abs() < 1e-5 && (v[2] - 4.0).abs() < 1e-5
        );
        check_decomposition(&m, &r, 1e-5);
        // the eigenvector rows stay orthonormal (a proper rotation: det = +1)
        let d = r.vectors;
        let det = d[0][0] * (d[1][1] * d[2][2] - d[1][2] * d[2][1])
            - d[0][1] * (d[1][0] * d[2][2] - d[1][2] * d[2][0])
            + d[0][2] * (d[1][0] * d[2][1] - d[1][1] * d[2][0]);
        assert!((det - 1.0).abs() < 1e-5);
    }

    #[test]
    fn huge_theta_skips_the_rotation_but_zeroes_the_element() {
        let m = [[0.0, 1e-12, 0.0], [1e-12, 1.0, 0.0], [0.0, 0.0, 1.0]];
        let r = jacobi_sym(&m, 0.0);
        assert_eq!(r.iters, 1);
        assert_eq!(r.values, [0.0, 1.0, 1.0]);
        assert_eq!(
            r.vectors,
            [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
        );
    }

    #[test]
    fn nan_off_diagonal_never_wins_the_search() {
        let n = f32::NAN;
        let m = [[1.0, n, 0.0], [n, 1.0, 0.5], [0.0, 0.5, 1.0]];
        let r = jacobi_sym(&m, 0.0);
        // NaN² never wins the search; the finite pair is rotated, after which the search
        // finds nothing above zero only if it got exactly zero: the result must not panic and
        // NaN eigenvector entries are flushed to +0 by the cleanup.
        for row in r.vectors {
            for x in row {
                assert!(!x.is_nan());
            }
        }
    }
}
