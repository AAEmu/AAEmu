//! sph–sph linear unprojection (x64 0x35178110, x86 0x3518C690).

use super::common::{as_sph, le_or_nan_d, ucomiss_eq};
use super::{UnprojTables, MODE_LINEAR};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{Primitive, SpherePrim};
use crate::ids::GeomType;
use crate::math::Vec3;

/// sub_35178110: move sphere A along `dir` until it stops touching sphere B.
///
/// * `dir = 0` (squared length compared with `ucomiss`/`jne`, so a NaN length also takes this
///   branch): `dir = normalize(cA − cB)` in double (zero distance → (0,0,1)) and written back,
///   `t = (rA + rB) − |cA − cB|`, `n = −dir`, `pt = cB − n·rB`. Always succeeds.
/// * otherwise: solve `|d + dir·t|² = (rA+rB)²` in double and take the larger root
///   `(√disc − b)/a`. Fails unless `disc ≥ 0` (sign bit of the float-rounded value) and either
///   `b < 0` or the spheres overlap now (sign bit of the float-rounded `b² − disc`), or if
///   `t ≤ tmin`. `n = normalize(cB − (cA + dir·t))` ((0,0,1) if `|·|² ≤ 1e-20`),
///   `pt = cB − n·rB`.
///
/// Features are set to 0x40 / 0x40 before anything else (also on failure).
pub fn unproject_sph_sph(
    p: &mut UnprojParams,
    a: &SpherePrim,
    _fa: i32,
    b: &SpherePrim,
    _fb: i32,
    out: &mut UnprojResult,
) -> i32 {
    let dx = f64::from(a.center.x - b.center.x);
    let dy = f64::from(a.center.y - b.center.y);
    let dz = f64::from(a.center.z - b.center.z);
    out.i_feature[1] = 0x40;
    out.i_feature[0] = 0x40;
    let dl2 = (p.dir.x * p.dir.x + p.dir.y * p.dir.y) + p.dir.z * p.dir.z;
    if ucomiss_eq(dl2, 0.0) {
        let len = ((dy * dy + dx * dx) + dz * dz).sqrt();
        let (ux, uy, uz) = if len > 0.0 {
            let inv = 1.0 / len;
            (inv * dx, dy * inv, dz * inv)
        } else {
            (0.0, 0.0, 1.0)
        };
        p.dir = Vec3::new(ux as f32, uy as f32, uz as f32);
        out.t = f64::from(a.r + b.r) - len;
        out.n = Vec3::new(-p.dir.x, -p.dir.y, -p.dir.z);
        let r = b.r;
        out.pt = Vec3::new(
            b.center.x - out.n.x * r,
            b.center.y - out.n.y * r,
            b.center.z - out.n.z * r,
        );
        return 1;
    }
    let ex = f64::from(p.dir.x);
    let ey = f64::from(p.dir.y);
    let ez = f64::from(p.dir.z);
    let qa = (ey * ey + ex * ex) + ez * ez;
    let qb = (ey * dy + ex * dx) + ez * dz;
    let rs = a.r + b.r;
    let c0 = (dy * dy + dx * dx) + dz * dz;
    let disc = qb * qb - (c0 - f64::from(rs * rs)) * qa;
    let b_f = qb as f32;
    let disc_f = disc as f32;
    let x_f = (qb * qb - disc) as f32;
    if (!disc_f.to_bits() & (x_f.to_bits() | b_f.to_bits())) as i32 >= 0 {
        return 0;
    }
    let t = (disc.sqrt() - qb) / qa;
    // comisd + jbe: t <= tmin or unordered fails
    if le_or_nan_d(t, f64::from(p.tmin)) {
        return 0;
    }
    out.t = t;
    let tf = t as f32;
    let mut nx = b.center.x - (p.dir.x * tf + a.center.x);
    let mut ny = b.center.y - (p.dir.y * tf + a.center.y);
    let mut nz = b.center.z - (p.dir.z * tf + a.center.z);
    let l2 = (ny * ny + nx * nx) + nz * nz;
    if l2 > 1e-20 {
        let inv = 1.0 / l2.sqrt();
        nx *= inv;
        ny *= inv;
        nz *= inv;
    } else {
        nx = 0.0;
        ny = 0.0;
        nz = 1.0;
    }
    out.n = Vec3::new(nx, ny, nz);
    let r = b.r;
    out.pt = Vec3::new(
        b.center.x - out.n.x * r,
        b.center.y - out.n.y * r,
        b.center.z - out.n.z * r,
    );
    1
}

fn cell_sph_sph(
    p: &mut UnprojParams,
    a: &Primitive,
    fa: i32,
    b: &Primitive,
    fb: i32,
    out: &mut UnprojResult,
    _area: Option<&mut ContactArea>,
) -> i32 {
    unproject_sph_sph(p, as_sph(a), fa, as_sph(b), fb, out)
}

/// Fill the cells this group owns: `[0][sph][sph]`.
pub fn fill(t: &mut UnprojTables) {
    t.set(
        MODE_LINEAR,
        GeomType::Sphere,
        GeomType::Sphere,
        cell_sph_sph,
    );
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sph(x: f32, r: f32) -> SpherePrim {
        SpherePrim {
            center: Vec3::new(x, 0.0, 0.0),
            r,
        }
    }

    #[test]
    fn min_distance_and_directed() {
        let mut p = UnprojParams::default();
        let mut out = UnprojResult::default();
        assert_eq!(
            unproject_sph_sph(&mut p, &sph(0.0, 1.0), -1, &sph(1.5, 1.0), -1, &mut out),
            1
        );
        assert_eq!(p.dir, Vec3::new(-1.0, 0.0, 0.0));
        assert_eq!(out.t, 0.5);
        assert_eq!(out.pt, Vec3::new(0.5, 0.0, 0.0));
        p.dir = Vec3::new(-1.0, 0.0, 0.0);
        let mut out = UnprojResult::default();
        assert_eq!(
            unproject_sph_sph(&mut p, &sph(0.0, 1.0), -1, &sph(1.5, 1.0), -1, &mut out),
            1
        );
        assert_eq!(out.t, 0.5);
        assert_eq!(out.n, Vec3::new(1.0, 0.0, 0.0));
        assert_eq!(out.i_feature, [0x40, 0x40]);
    }
}
