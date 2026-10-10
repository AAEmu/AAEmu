//! The triangle-mesh buoyancy and pressure the box goes through (the box converts itself to a
//! temporary 12-triangle `CTriMesh`). `CTriMesh` itself belongs to package P1c; these are ports
//! of its slot 13 (0x351DA620) and slot 12 (0x351CBE20) over a plain triangle view, so P1c
//! can reuse them.

use super::super::{BoxGeom, DEC_MOD3, INC_MOD3};
use crate::geometry::primitives::Plane;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};

#[path = "mesh_pressure.rs"]
mod mesh_pressure;

/// The parts of a `CTriMesh` the buoyancy/pressure code reads: vertices (`+80`), 3 indices per
/// triangle (`+56`), one normal per triangle (`+96`).
#[derive(Clone, Copy, Debug)]
pub struct TriMeshView<'a> {
    pub vertices: &'a [Vec3],
    pub indices: &'a [i32],
    pub normals: &'a [Vec3],
}

impl TriMeshView<'_> {
    /// `nTris` (`+120`).
    #[must_use]
    pub fn n_tris(&self) -> usize {
        self.indices.len() / 3
    }
}

/// The temporary mesh of a box (`sub_3513AC00` with `bTemp = 1`).
#[derive(Clone, Debug, PartialEq)]
pub struct BoxMesh {
    pub vertices: [Vec3; 8],
    pub indices: [i32; 36],
    pub normals: [Vec3; 12],
}

impl BoxMesh {
    /// As a mesh view.
    #[must_use]
    pub fn view(&self) -> TriMeshView<'_> {
        TriMeshView {
            vertices: &self.vertices,
            indices: &self.indices,
            normals: &self.normals,
        }
    }
}

/// The box index table at 0x35209A70 (12 triangles, two per face, faces −x, +x, −y, +y, −z, +z).
pub const BOX_INDICES: [i32; 36] = [
    0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0, 4, 5,
    7, 7, 6, 4,
];

/// `sub_3513AC00` BoxToTempTriMesh (inferred name): vertex `i` is
/// `((2(i&1)−1)·sx, ((i&2)−1)·sy, (((i>>1)&2)−1)·sz)`, rotated by `Basisᵀ` when oriented,
/// plus the centre; face `j` normal is `±e_{j/2}` (sign `2(j&1)−1`, rotated the same way),
/// shared by triangles `2j` and `2j+1`.
#[must_use]
pub fn box_to_mesh(g: &BoxGeom) -> BoxMesh {
    let p = &g.prim;
    let oriented = p.oriented != 0;
    let rot = |v: Vec3| -> Vec3 {
        if oriented {
            p.basis.t_mul_vec(v)
        } else {
            v
        }
    };
    let mut vertices = [Vec3::ZERO; 8];
    for (i, v) in vertices.iter_mut().enumerate() {
        let i = i as i32;
        let l = Vec3::new(
            (2 * (i & 1) - 1) as f32 * p.size.x,
            ((i & 2) - 1) as f32 * p.size.y,
            (((i >> 1) & 2) - 1) as f32 * p.size.z,
        );
        let w = rot(l);
        *v = Vec3::new(p.center.x + w.x, p.center.y + w.y, p.center.z + w.z);
    }
    let mut normals = [Vec3::ZERO; 12];
    for j in 0..6 {
        let mut n = Vec3::ZERO;
        n[j >> 1] = (2 * (j as i32 & 1) - 1) as f32;
        let n = rot(n);
        normals[2 * j] = n;
        normals[2 * j + 1] = n;
    }
    BoxMesh {
        vertices,
        indices: BOX_INDICES,
        normals,
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

/// The mesh-to-plane-frame transform of slots 12/13: `T = Rp·R`, `off = Rp·(offset − origin)`
/// (slot 13) where `Rp = matrix(QuatRotationArc(n, (0,0,1)))`.
struct PlaneFrame {
    t: Mat33,
    off: Vec3,
    scale: f32,
}

impl PlaneFrame {
    /// `sub_351C27A0` GetTriangleWorld (inferred name): the three vertices `(T·v)·s + off` and
    /// the rotated normal `T·n` (unscaled).
    fn triangle(&self, m: &TriMeshView, k: usize) -> ([Vec3; 3], Vec3) {
        let mut v = [Vec3::ZERO; 3];
        for (j, p) in v.iter_mut().enumerate() {
            let src = m.vertices[m.indices[3 * k + j] as usize];
            *p = self.t.mul_vec(src) * self.scale + self.off;
        }
        (v, self.t.mul_vec(m.normals[k]))
    }
}

/// `CTriMesh` slot 13 (0x351DA620) CalculateBuoyancy over a triangle view.
///
/// Every triangle goes to the water frame (`z` = height above the plane). Per triangle:
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
/// `M = Σ V_i·c_i`. Returns `V` — **even when `V <= 0`** (then the centre is zero) — and the
/// centre `M·(1/V) + plane.origin`, **not rotated back to the world frame** (quirk kept: exact
/// only for `n = (0,0,1)`).
#[must_use]
pub fn mesh_buoyancy(m: &TriMeshView, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
    let q = super::super::quat_rotation_arc(plane.n, Vec3::Z);
    let rp = Mat33::from_quat(q);
    let fr = PlaneFrame {
        off: rp.mul_vec(gwd.offset - plane.origin),
        t: rp.mul_mat(&gwd.r),
        scale: gwd.scale,
    };
    let mut vt = 0.0f32;
    let mut mx = 0.0f32;
    let mut my = 0.0f32;
    let mut mz = 0.0f32;
    for k in 0..m.n_tris() {
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

/// `CTriMesh` slot 12 (0x351CBE20) CalcVolumetricPressure over a triangle view; see
/// [`mesh_pressure::volumetric_pressure`] for the algorithm and the quirks kept.
#[allow(clippy::too_many_arguments)]
pub fn mesh_volumetric_pressure(
    m: &TriMeshView,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    com: Vec3,
    p: &mut Vec3,
    l: &mut Vec3,
) {
    mesh_pressure::volumetric_pressure(m, gwd, epicenter, k, rmin, com, p, l);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::BoxPrim;

    #[test]
    fn unit_box_mesh_volume() {
        let g = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(0.5)));
        let m = box_to_mesh(&g);
        // fully submerged: water plane far above
        let plane = Plane {
            n: Vec3::Z,
            origin: Vec3::new(0.0, 0.0, 10.0),
        };
        let (v, c) = mesh_buoyancy(&m.view(), &plane, &GeomWorldData::default());
        assert!((v - 1.0).abs() < 1e-5, "{v}");
        assert!((c - Vec3::ZERO).len() < 1e-4, "{c:?}");
        // half submerged
        let plane = Plane {
            n: Vec3::Z,
            origin: Vec3::ZERO,
        };
        let (v, c) = mesh_buoyancy(&m.view(), &plane, &GeomWorldData::default());
        assert!((v - 0.5).abs() < 1e-5, "{v}");
        assert!((c.z + 0.25).abs() < 1e-4, "{c:?}");
        let (tv, tc) = tet_volume_centroid(Vec3::ZERO, Vec3::X, Vec3::Y, Vec3::Z);
        assert!((tv - 1.0 / 6.0).abs() < 1e-7);
        assert_eq!(tc, Vec3::splat(0.25));
    }
}
