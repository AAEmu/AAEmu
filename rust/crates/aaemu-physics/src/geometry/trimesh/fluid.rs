//! `CTriMesh` fluid slots: 12 `CalcVolumetricPressure` (0x351CBE20), 13 `CalculateBuoyancy`
//! (0x351DA620) and 14 `CalculateMediumResistance` (0x351CD0C0), `geometry.md` §6.
//!
//! The three routines only read the vertices (`+80`, stride `+88` = 12), the indices (`+56`),
//! the per-triangle normals (`+96`) and `nTris` (`+120`), so they are written over a
//! [`MeshView`]; [`TriMesh`] exposes them as inherent methods named after the slots.
//!
//! **Origin of the code.** The buoyancy, pressure and polygon-drag routines were first ported
//! (and verified through the box's temporary 12-triangle mesh) by package P1a on branch
//! `p7-phys-shapes` (`geometry/shapes/buoyancy/mesh.rs`, `mesh_pressure.rs`, `drag.rs`, plus
//! `quat_rotation_arc` / `orthogonal` from `geometry/shapes.rs`). That branch is not merged
//! here, so the code is copied into this module (adapted to `TriMesh`: `nTris` is the explicit
//! `+120` count, not `indices.len() / 3`) and re-verified on real meshes
//! (`tests/p1c_fluid.rs`). The slot 14 driver 0x351CD0C0 is new.
//!
//! **On a heightfield.** `CHeightfield` (and `CVoxelGeom`) inherit slots 13 and 14 unchanged
//! (same vtable entries). A `CHeightfield` keeps `nTris` (`+120`) at 0 (oracle-checked), so on
//! a heightfield slot 13 returns volume 0 with centre `(0,0,0)` and slot 14 just zeroes `dP` and
//! `dL`. Slot 12 of `CHeightfield` is its own empty function (0x35154CB0,
//! [`super::closest::heightfield_calc_volumetric_pressure`]).

mod drag;
mod pressure;

pub use drag::{clip_polygon_by_plane, polygon_drag_integrate, rotate_axis_cos_sin, MAX_POLY};

use super::TriMesh;
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Quat, Vec3};

/// Index tables at 0x3520A770 / 0x3520A780 (and their copies): `inc = {1,2,0}`.
pub const INC_MOD3: [usize; 3] = [1, 2, 0];
/// `dec = {2,0,1}`.
pub const DEC_MOD3: [usize; 3] = [2, 0, 1];

/// The parts of a `CTriMesh` the fluid routines read: vertices (`+80`), 3 indices per triangle
/// (`+56`), one normal per triangle (`+96`) and `nTris` (`+120`).
#[derive(Clone, Copy, Debug)]
pub struct MeshView<'a> {
    pub vertices: &'a [Vec3],
    pub indices: &'a [i32],
    pub normals: &'a [Vec3],
    /// `+120`; `<= 0` means no triangle is visited.
    pub n_tris: i32,
}

impl MeshView<'_> {
    /// The triangle count as a loop bound (`for (i = 0; i < nTris; i++)`).
    #[inline]
    #[must_use]
    pub fn count(&self) -> usize {
        self.n_tris.max(0) as usize
    }

    /// Vertex `j` of triangle `k`.
    #[inline]
    #[must_use]
    pub fn vtx(&self, k: usize, j: usize) -> Vec3 {
        self.vertices[self.indices[3 * k + j] as usize]
    }
}

impl TriMesh {
    /// The fields the fluid and closest-point routines read.
    #[must_use]
    pub fn view(&self) -> MeshView<'_> {
        MeshView {
            vertices: &self.vertices,
            indices: &self.indices,
            normals: &self.normals,
            n_tris: self.n_tris,
        }
    }

    /// Slot 13 `CalculateBuoyancy(plane, gwd, massCenter&)` (0x351DA620): `(submerged volume,
    /// centre)`; see [`mesh_buoyancy`].
    #[must_use]
    pub fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        mesh_buoyancy(&self.view(), plane, gwd)
    }

    /// Slot 14 `CalculateMediumResistance(plane, gwd, dP&, dL&)` (0x351CD0C0); see
    /// [`mesh_medium_resistance`].
    pub fn calculate_medium_resistance(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        mesh_medium_resistance(&self.view(), plane, gwd, dp, dl);
    }

    /// Slot 12 `CalcVolumetricPressure(gwd, epicenter, k, rmin, centerOfMass, P&, L&)`
    /// (0x351CBE20); see [`pressure::volumetric_pressure`].
    #[allow(clippy::too_many_arguments)]
    pub fn calc_volumetric_pressure(
        &self,
        gwd: &GeomWorldData,
        epicenter: Vec3,
        k: f32,
        rmin: f32,
        com: Vec3,
        p: &mut Vec3,
        l: &mut Vec3,
    ) {
        pressure::volumetric_pressure(&self.view(), gwd, epicenter, k, rmin, com, p, l);
    }
}

/// `CTriMesh` slot 12 over a view; see [`pressure::volumetric_pressure`] for the algorithm and
/// the quirks kept.
#[allow(clippy::too_many_arguments)]
pub fn mesh_volumetric_pressure(
    m: &MeshView,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    com: Vec3,
    p: &mut Vec3,
    l: &mut Vec3,
) {
    pressure::volumetric_pressure(m, gwd, epicenter, k, rmin, com, p, l);
}

/// The engine's `GetOrthogonal` idiom (inlined in `QuatRotationArc` 0x35040F80):
/// `i = isneg(|a|²·0.81 − a.x²)`; `v[i] = 0, v[inc i] = a[dec i], v[dec i] = −a[inc i]`
/// (unnormalised). Copied from P1a.
#[must_use]
pub fn orthogonal(a: Vec3) -> Vec3 {
    let k = f32::from_bits(0x3F4F_5C28); // 0.80999994
    let t = a.len2() * k - a.x * a.x;
    let i = (t.to_bits() >> 31) as usize;
    let mut v = Vec3::ZERO;
    v[i] = 0.0;
    v[INC_MOD3[i]] = a[DEC_MOD3[i]];
    v[DEC_MOD3[i]] = -a[INC_MOD3[i]];
    v
}

/// `sub_35040F80` QuatRotationArc(from, to): the shortest-arc rotation taking unit `from` to
/// unit `to` (copied from P1a).
///
/// `d = (from·to) + 1` (single, `to.x·from.x + to.y·from.y` first) widened to double; if
/// `d > 1e-4` (double compare): `q = (from × to, d)` normalised in double with the squared
/// norm summed `((x² + w²) + y²) + z²`; else `w = 0` and `v` = the normalised orthogonal of
/// `from` (`1/sqrtss(|o|² + FLT_MIN)`).
#[must_use]
pub fn quat_rotation_arc(from: Vec3, to: Vec3) -> Quat {
    let dotp = (to.x * from.x + to.y * from.y) + to.z * from.z;
    let d = f64::from(dotp + 1.0);
    if d > f64::from(1e-4_f32) {
        let cx = f64::from(to.z * from.y - to.y * from.z);
        let cy = f64::from(from.z * to.x - to.z * from.x);
        let cz = f64::from(to.y * from.x - from.y * to.x);
        let n2 = ((cx * cx + d * d) + cy * cy) + cz * cz;
        let inv = 1.0 / n2.sqrt();
        Quat::new(
            (inv * cx) as f32,
            (inv * cy) as f32,
            (inv * cz) as f32,
            (inv * d) as f32,
        )
    } else {
        let o = orthogonal(from);
        let inv = 1.0 / (o.len2() + f32::MIN_POSITIVE).sqrt();
        Quat::new(inv * o.x, inv * o.y, inv * o.z, 0.0)
    }
}

/// `sub_351CCF20` TetVolumeCentroid (inferred name): `|det(b−a, c−a, d−a)|·(1/6)` with
/// `det = ((bz·cx − cz·bx)·dy + (by·cz − bz·cy)·dx) + (cy·bx − by·cx)·dz`; centroid
/// `(((a + b) + c) + d)·0.25` per component.
#[must_use]
pub fn tet_volume_centroid(a: Vec3, b: Vec3, c: Vec3, d: Vec3) -> (f32, Vec3) {
    let cen = Vec3::new(
        (((a.x + b.x) + c.x) + d.x) * 0.25,
        (((a.y + b.y) + c.y) + d.y) * 0.25,
        (((a.z + b.z) + c.z) + d.z) * 0.25,
    );
    let (bx, by, bz) = (b.x - a.x, b.y - a.y, b.z - a.z);
    let (cx, cy, cz) = (c.x - a.x, c.y - a.y, c.z - a.z);
    let (dx, dy, dz) = (d.x - a.x, d.y - a.y, d.z - a.z);
    (det6(bx, by, bz, cx, cy, cz, dx, dy, dz), cen)
}

#[allow(clippy::too_many_arguments)]
#[inline]
fn det6(bx: f32, by: f32, bz: f32, cx: f32, cy: f32, cz: f32, dx: f32, dy: f32, dz: f32) -> f32 {
    let det = ((bz * cx - cz * bx) * dy + (by * cz - bz * cy) * dx) + (cy * bx - by * cx) * dz;
    det.abs() * f32::from_bits(0x3E2A_AAAB)
}

/// The frame `sub_351C27A0` GetTriangleWorld (inferred name) reads: a row-major matrix, an
/// offset and a scale; a vertex goes to `(M·v)·s + off`, the normal to `M·n` (unscaled).
#[derive(Clone, Copy, Debug)]
pub struct TriFrame {
    pub m: Mat33,
    pub off: Vec3,
    pub scale: f32,
}

impl TriFrame {
    /// The gwd's own frame (slot 14): `M = R`, `off = offset`, `s = scale`.
    #[must_use]
    pub fn world(gwd: &GeomWorldData) -> Self {
        Self {
            m: gwd.r,
            off: gwd.offset,
            scale: gwd.scale,
        }
    }

    /// `sub_351C27A0`: the three vertices of triangle `k` and its rotated normal.
    #[must_use]
    pub fn triangle(&self, m: &MeshView, k: usize) -> ([Vec3; 3], Vec3) {
        let mut v = [Vec3::ZERO; 3];
        for (j, p) in v.iter_mut().enumerate() {
            *p = self.m.mul_vec(m.vtx(k, j)) * self.scale + self.off;
        }
        (v, self.m.mul_vec(m.normals[k]))
    }
}

/// `CTriMesh` slot 13 (0x351DA620) CalculateBuoyancy over a mesh view.
///
/// Every triangle goes to the water frame (`z` = height above the plane): `T = Rp·R`
/// (`Rp = matrix(QuatRotationArc(n, (0,0,1)))`), `off = Rp·(offset − origin)`. Per triangle:
/// sign bits of the three `z` (`+0.0` counts as above), `nA` = vertices with `z >= 0`, `top` /
/// `low` = highest / lowest vertex (sign-bit selects, ties to the lower index), crossing points
/// on the edges whose signs differ (`t = (−1/(zj − zi))·zi`, `(1−t)·vi + t·vj`, in edge order
/// 0-1, 1-2, 2-0). Pieces:
/// * `nA = 0`: `Tet(v_top, P'_inc, v_inc, P'_dec)`, `Tet(v_top, P'_dec, v_dec, v_inc)` and the
///   prism `|area_xy·0.5·z_top|` (`P'` = vertices projected to `z_top`);
/// * `nA = 1`: the same with the prism negated, plus `Tet(v_top, (x_top, y_top, 0), c0, c1)`;
/// * `nA = 2`: `Tet(v_low, (x_low, y_low, 0), c0, c1)` (inlined, its own summation order);
/// * `nA = 3`: nothing.
///
/// Each piece is weighted by `w = −sign(n'_z)` (`0` for `±0`) and summed in order into `V` and
/// `M = Σ V_i·c_i`. Returns `V` — **even when `V <= 0`** (then the centre is zero; NaN too:
/// `comiss; jbe`) — and the centre `M·(1/V) + plane.origin`, **not rotated back to the world
/// frame** (original bug kept: exact only for `n = (0,0,1)`).
#[must_use]
pub fn mesh_buoyancy(m: &MeshView, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
    let q = quat_rotation_arc(plane.n, Vec3::Z);
    let rp = Mat33::from_quat(q);
    let fr = TriFrame {
        off: rp.mul_vec(gwd.offset - plane.origin),
        m: rp.mul_mat(&gwd.r),
        scale: gwd.scale,
    };
    let mut vt = 0.0f32;
    let mut mx = 0.0f32;
    let mut my = 0.0f32;
    let mut mz = 0.0f32;
    for k in 0..m.count() {
        let (v, n) = fr.triangle(m, k);
        let sg = [
            v[0].z.to_bits() >> 31,
            v[1].z.to_bits() >> 31,
            v[2].z.to_bits() >> 31,
        ];
        let n_above = 3 - (sg[0] + sg[1] + sg[2]) as usize;
        let neg = |x: f32| x.to_bits() >> 31 != 0;
        let i01_low = usize::from(neg(v[1].z - v[0].z));
        let i01_top = usize::from(neg(v[0].z - v[1].z));
        let low = if neg(v[2].z - v[i01_low].z) {
            2
        } else {
            i01_low
        };
        let top = if neg(v[i01_top].z - v[2].z) {
            2
        } else {
            i01_top
        };
        // crossings, compacted in edge order
        let mut cr = [Vec3::ZERO; 3];
        let mut ncr = 0;
        for i in 0..3 {
            let j = INC_MOD3[i];
            if sg[j] != sg[i] {
                let t = (-1.0 / (v[j].z - v[i].z)) * v[i].z;
                let u = 1.0 - t;
                cr[ncr] = Vec3::new(
                    u * v[i].x + v[j].x * t,
                    u * v[i].y + v[j].y * t,
                    u * v[i].z + v[j].z * t,
                );
                ncr += 1;
            }
        }
        let mut vols = [0f32; 4];
        let mut cens = [Vec3::ZERO; 4];
        let npieces;
        if n_above < 2 {
            let zm = v[top].z;
            let mut pp = [
                Vec3::new(v[0].x, v[0].y, zm),
                Vec3::new(v[1].x, v[1].y, zm),
                Vec3::new(v[2].x, v[2].y, zm),
            ];
            let (inc, dec) = (INC_MOD3[top], DEC_MOD3[top]);
            (vols[0], cens[0]) = tet_volume_centroid(v[top], pp[inc], v[inc], pp[dec]);
            (vols[1], cens[1]) = tet_volume_centroid(v[top], pp[dec], v[dec], v[inc]);
            let area = ((v[2].y - v[0].y) * (v[1].x - v[0].x)
                - (v[1].y - v[0].y) * (v[2].x - v[0].x))
                * 0.5;
            let third = f32::from_bits(0x3EAA_AAAB);
            cens[2] = Vec3::new(
                (v[2].x + (v[1].x + v[0].x)) * third,
                (v[2].y + (v[1].y + v[0].y)) * third,
                zm * 0.5,
            );
            vols[2] = (area * zm).abs();
            if n_above == 1 {
                vols[2] *= -1.0;
                pp[top].z = 0.0;
                (vols[3], cens[3]) = tet_volume_centroid(v[top], pp[top], cr[0], cr[1]);
                npieces = 4;
            } else {
                npieces = 3;
            }
        } else if n_above == 2 {
            let l = v[low];
            let (c0, c1) = (cr[0], cr[1]);
            cens[0] = Vec3::new(
                (((l.x + l.x) + c0.x) + c1.x) * 0.25,
                ((c0.y + (l.y + l.y)) + c1.y) * 0.25,
                ((c0.z + l.z) + c1.z) * 0.25,
            );
            // `L − L` is computed by the binary (0, or NaN for non-finite input)
            #[allow(clippy::eq_op)]
            let (bx, by) = (l.x - l.x, l.y - l.y);
            vols[0] = det6(
                bx,
                by,
                -l.z,
                c0.x - l.x,
                c0.y - l.y,
                c0.z - l.z,
                c1.x - l.x,
                c1.y - l.y,
                c1.z - l.z,
            );
            npieces = 1;
        } else {
            npieces = 0;
        }
        let nb = n.z.to_bits() as i32;
        let w = (-1 - (nb.wrapping_sub(1) >> 31) - (nb >> 31)) as f32;
        for i in 0..npieces {
            let vi = w * vols[i];
            vt += vi;
            mx += vi * cens[i].x;
            my += vi * cens[i].y;
            mz += vi * cens[i].z;
        }
    }
    if vt > 0.0 {
        let inv = 1.0 / vt;
        (
            vt,
            Vec3::new(
                inv * mx + plane.origin.x,
                my * inv + plane.origin.y,
                mz * inv + plane.origin.z,
            ),
        )
    } else {
        (vt, Vec3::ZERO)
    }
}

/// `CTriMesh` slot 14 (0x351CD0C0) CalculateMediumResistance over a mesh view.
///
/// **Zeroes** `dP` and `dL`, then sends every triangle — world vertices `(R·v)·s + offset` and
/// rotated normal `R·n` (`sub_351C27A0` with the gwd's own frame) — through
/// [`polygon_drag_integrate`] as a 3-point polygon with the gwd's `v`, `w` and
/// `centerOfMass`, which subtracts its drag. No lock is taken.
pub fn mesh_medium_resistance(
    m: &MeshView,
    plane: &Plane,
    gwd: &GeomWorldData,
    dp: &mut Vec3,
    dl: &mut Vec3,
) {
    *dp = Vec3::ZERO;
    *dl = Vec3::ZERO;
    let fr = TriFrame::world(gwd);
    for k in 0..m.count() {
        let (v, n) = fr.triangle(m, k);
        polygon_drag_integrate(&v, n, plane, gwd.v, gwd.w, gwd.center_of_mass, dp, dl);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn cube() -> (Vec<Vec3>, Vec<i32>, Vec<Vec3>) {
        let mut v = Vec::new();
        for i in 0..8 {
            let c = |b: i32| if i & b != 0 { 0.5 } else { -0.5 };
            v.push(Vec3::new(c(1), c(2), c(4)));
        }
        let idx = vec![
            0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1,
            0, 4, 5, 7, 7, 6, 4,
        ];
        let mut n = Vec::new();
        for j in 0..6 {
            let mut e = Vec3::ZERO;
            e[j >> 1] = if j & 1 != 0 { 1.0 } else { -1.0 };
            n.push(e);
            n.push(e);
        }
        (v, idx, n)
    }

    #[test]
    fn unit_cube_volume() {
        let (v, idx, n) = cube();
        let m = MeshView {
            vertices: &v,
            indices: &idx,
            normals: &n,
            n_tris: 12,
        };
        let plane = Plane {
            n: Vec3::Z,
            origin: Vec3::new(0.0, 0.0, 10.0),
        };
        let (vol, c) = mesh_buoyancy(&m, &plane, &GeomWorldData::default());
        assert!((vol - 1.0).abs() < 1e-5, "{vol}");
        assert!(c.len() < 1e-4, "{c:?}");
        let plane = Plane {
            n: Vec3::Z,
            origin: Vec3::ZERO,
        };
        let (vol, c) = mesh_buoyancy(&m, &plane, &GeomWorldData::default());
        assert!((vol - 0.5).abs() < 1e-5, "{vol}");
        assert!((c.z + 0.25).abs() < 1e-4, "{c:?}");
        let empty = MeshView { n_tris: 0, ..m };
        assert_eq!(
            mesh_buoyancy(&empty, &plane, &GeomWorldData::default()),
            (0.0, Vec3::ZERO)
        );
        // a cube moving down into the water meets resistance pointing up
        let gwd = GeomWorldData {
            v: Vec3::new(0.0, 0.0, -1.0),
            ..GeomWorldData::default()
        };
        let (mut dp, mut dl) = (Vec3::splat(5.0), Vec3::splat(5.0));
        mesh_medium_resistance(&m, &plane, &gwd, &mut dp, &mut dl);
        assert!(dp.z > 0.0, "{dp:?}");
        let (tv, tc) = tet_volume_centroid(Vec3::ZERO, Vec3::X, Vec3::Y, Vec3::Z);
        assert!((tv - 1.0 / 6.0).abs() < 1e-7);
        assert_eq!(tc, Vec3::splat(0.25));
    }
}
