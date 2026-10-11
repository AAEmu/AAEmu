//! The temporary 12-triangle `CTriMesh` the box's slots 12 / 13 go through (`sub_3513AC00`).
//! The mesh slots themselves (0x351DA620, 0x351CBE20) live in
//! [`crate::geometry::trimesh::fluid`] (one implementation for meshes and boxes).

use super::super::BoxGeom;
use crate::geometry::trimesh::fluid::MeshView;
use crate::math::Vec3;

pub use crate::geometry::trimesh::fluid::{
    mesh_buoyancy, mesh_volumetric_pressure, tet_volume_centroid,
};

/// Former name of [`MeshView`] (P1a).
pub type TriMeshView<'a> = MeshView<'a>;

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
    pub fn view(&self) -> MeshView<'_> {
        MeshView {
            vertices: &self.vertices,
            indices: &self.indices,
            normals: &self.normals,
            n_tris: 12,
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::BoxPrim;
    use crate::geometry::primitives::Plane;
    use crate::geometry::GeomWorldData;

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
