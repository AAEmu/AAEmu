//! The tree choice of the `CTriMesh` constructor (sub_351CF1B0, from the principal-axes fit to
//! the flag update): which BV trees are built for a mesh's build flags, their costs, and which
//! one is kept. Exposed for P1c's `CTriMesh`; the tree classes do the work.

use super::fit::{mesh_principal_axes, PrincipalAxes};
use super::idiom::{sel_max, sel_min};
use super::single_box::SingleBoxTree;
use super::{AabbTree, BuildMesh, BvTree, ObbTree};
use crate::geometry::primitives::mesh_flags;
use crate::geometry::primitives::BoxPrim;
use crate::math::{max_idiom, Mat33, Vec3};

/// Mesh flag 0x80: a single box (`CSingleBoxTree`) instead of a tree.
pub const SINGLE_BOX: u32 = 0x80;
/// Mesh flag 0x400: maxSkipDim 0.
pub const NO_SKIP: u32 = 0x400;
/// Mesh flag 0x100: maxSkipDim 10.
pub const BIG_SKIP: u32 = 0x100;

/// The tree a mesh keeps (`CTriMesh +48`), with its concrete type.
#[derive(Clone, Debug)]
pub enum MeshTree {
    Obb(ObbTree),
    Aabb(AabbTree),
    SingleBox(SingleBoxTree),
}

impl MeshTree {
    /// The tree behind the slot interface.
    #[must_use]
    pub fn as_dyn(&self) -> &dyn BvTree {
        match self {
            MeshTree::Obb(t) => t,
            MeshTree::Aabb(t) => t,
            MeshTree::SingleBox(t) => t,
        }
    }

    /// Boxed, for storage as `Box<dyn BvTree>`.
    #[must_use]
    pub fn into_dyn(self) -> Box<dyn BvTree> {
        match self {
            MeshTree::Obb(t) => Box::new(t),
            MeshTree::Aabb(t) => Box::new(t),
            MeshTree::SingleBox(t) => Box::new(t),
        }
    }
}

/// The constructor's single box (mesh flag 0x80): the triangles' vertices (every reference,
/// duplicates included) projected on the fitted axes (rounded to single precision);
/// `centre = ((Σmin+max)·axes)·½`, half-sizes floored at `0.001·max`, then `SetBox` (so
/// `bOriented = !IsIdentity(axes)`), `Build` and `nPrims = nTris`.
#[must_use]
pub fn single_box_for_mesh(
    vertices: &[Vec3],
    indices: &[i32],
    fit: &PrincipalAxes,
) -> SingleBoxTree {
    let b = fit.axes_f32().m;
    let (mut mn, mut mx) = ([3.3e38f32; 3], [-3.3e38f32; 3]);
    for &i in indices {
        let p = vertices[i as usize];
        for k in 0..3 {
            let q = (p.x * b[k][0] + p.y * b[k][1]) + p.z * b[k][2];
            mn[k] = sel_min(mn[k], q);
            mx[k] = sel_max(mx[k], q);
        }
    }
    let s0 = mx[0] + mn[0];
    let s1 = mx[1] + mn[1];
    let s2 = mx[2] + mn[2];
    let center = Vec3::new(
        ((s0 * b[0][0] + s1 * b[1][0]) + s2 * b[2][0]) * 0.5,
        ((s1 * b[1][1] + s0 * b[0][1]) + s2 * b[2][1]) * 0.5,
        ((s1 * b[1][2] + s0 * b[0][2]) + s2 * b[2][2]) * 0.5,
    );
    let hx = (mx[0] - mn[0]) * 0.5;
    let hy = (mx[1] - mn[1]) * 0.5;
    let hz = (mx[2] - mn[2]) * 0.5;
    let floor = max_idiom(max_idiom(hx, hy), hz) * 0.001;
    let size = Vec3::new(
        ((hx - floor).abs() + (hx + floor)) * 0.5,
        ((hy - floor).abs() + (floor + hy)) * 0.5,
        ((hz - floor).abs() + (floor + hz)) * 0.5,
    );
    let mut t = SingleBoxTree::default();
    t.set_box(&BoxPrim {
        basis: Mat33::from_array([
            b[0][0], b[0][1], b[0][2], b[1][0], b[1][1], b[1][2], b[2][0], b[2][1], b[2][2],
        ]),
        oriented: 1,
        center,
        size,
    });
    t.n_prims = (indices.len() / 3) as i32;
    t
}

/// The constructor's tree choice for build `flags` (`min_tris` per side, `max_tris` per leaf,
/// `obb_cost_factor` = its last argument). Returns the tree (`None` when no tree flag is set)
/// and the updated flags (0x400E0 cleared, then 0x20 OBB / 0x40 AABB / 0x80 SingleBox).
///
/// * 0x40: AABB in the identity basis (flat split with 0x4000); 0x40000: AABB in the fitted
///   principal axes, cost × 1.01f; 0x20: OBB, cost × `obb_cost_factor`. maxSkipDim 0 / 10 / 0.1
///   for 0x400 / 0x100 / neither.
/// * Each later build starts from the triangle order the previous one left. The cheapest tree
///   (first on ties) is kept and the indices, materials and foreign ids are restored to the
///   order its build produced. **The auxiliary per-triangle array (+160) is not backed up**, so
///   it keeps the last build's order (kept as in the original).
pub fn build_mesh_tree(
    mesh: &mut BuildMesh<'_>,
    flags: u32,
    min_tris: i32,
    max_tris: i32,
    obb_cost_factor: f32,
) -> (Option<MeshTree>, u32) {
    let fit = if flags & (mesh_flags::AABB_PRINCIPAL_AXES | SINGLE_BOX) != 0 {
        Some(mesh_principal_axes(mesh.vertices, mesh.indices))
    } else {
        None
    };
    let tree: Option<MeshTree> = if flags & SINGLE_BOX == 0 {
        if flags & (mesh_flags::AABB_TREE | mesh_flags::OBB_TREE | mesh_flags::AABB_PRINCIPAL_AXES)
            != 0
        {
            let skip = if flags & NO_SKIP != 0 {
                0.0
            } else if flags & BIG_SKIP != 0 {
                10.0
            } else {
                0.1
            };
            let flat = flags & mesh_flags::FLAT_SPLIT != 0;
            let mut trees: Vec<MeshTree> = Vec::new();
            let mut costs: Vec<f32> = Vec::new();
            type Backup = (Vec<i32>, Option<Vec<i8>>, Option<Vec<i32>>);
            let mut backups: Vec<Backup> = Vec::new();
            let snapshot = |m: &BuildMesh<'_>| -> Backup {
                (
                    m.indices.to_vec(),
                    m.mats.as_deref().map(<[i8]>::to_vec),
                    m.foreign_idx.as_deref().map(<[i32]>::to_vec),
                )
            };
            if flags & mesh_flags::AABB_TREE != 0 {
                let mut t = AabbTree::new(min_tris, max_tris, skip, Mat33::IDENTITY, flat);
                costs.push(t.build(mesh));
                trees.push(MeshTree::Aabb(t));
            }
            if flags & mesh_flags::AABB_PRINCIPAL_AXES != 0 {
                if !trees.is_empty() {
                    backups.push(snapshot(mesh));
                }
                let basis = fit
                    .as_ref()
                    .map_or(Mat33::IDENTITY, PrincipalAxes::axes_f32);
                let mut t = AabbTree::new(min_tris, max_tris, skip, basis, flat);
                costs.push(t.build(mesh) * 1.01);
                trees.push(MeshTree::Aabb(t));
            }
            if flags & mesh_flags::OBB_TREE != 0 {
                if !trees.is_empty() {
                    backups.push(snapshot(mesh));
                }
                let mut t = ObbTree::new(min_tris, max_tris, skip);
                costs.push(t.build(mesh) * obb_cost_factor);
                trees.push(MeshTree::Obb(t));
            }
            let mut best = 0usize;
            for (i, &c) in costs.iter().enumerate().skip(1) {
                if costs[best] > c {
                    best = i;
                }
            }
            if best != trees.len() - 1 {
                let (idx, mats, fids) = &backups[best];
                mesh.indices.copy_from_slice(idx);
                if let (Some(m), Some(b)) = (mesh.mats.as_deref_mut(), mats) {
                    m.copy_from_slice(b);
                }
                if let (Some(f), Some(b)) = (mesh.foreign_idx.as_deref_mut(), fids) {
                    f.copy_from_slice(b);
                }
            }
            Some(trees.swap_remove(best))
        } else {
            None
        }
    } else {
        let f = fit.unwrap_or(PrincipalAxes {
            axes: [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]],
            center: [0.0; 3],
        });
        Some(MeshTree::SingleBox(single_box_for_mesh(
            mesh.vertices,
            mesh.indices,
            &f,
        )))
    };
    let mut out = flags & 0xFFFB_FF1F;
    if let Some(t) = &tree {
        out |= match t {
            MeshTree::Obb(_) => mesh_flags::OBB_TREE,
            MeshTree::Aabb(_) => mesh_flags::AABB_TREE,
            MeshTree::SingleBox(_) => SINGLE_BOX,
        };
    }
    (tree, out)
}
