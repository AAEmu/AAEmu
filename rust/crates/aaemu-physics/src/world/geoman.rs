//! **P3 — the geometry manager** embedded in the world (`CGeomManager`, `world+16`, 27 slots):
//! `CreatePrimitive` (slot 7, 0x3514B9F0) and `RegisterGeometry` (slot 9, 0x3514AE40), and
//! (P1c-world) the `CreateMesh` family: slot 3 (0x3514B710, [`World::create_mesh`]), slot 4
//! (0x35095CB0, [`World::create_mesh_ex`]) and slot 5 (0x35095BB0,
//! [`World::create_mesh_default`]); slot 6 (0x35098490, the mesh → primitive approximation)
//! is [`crate::geometry::trimesh::approx::try_primitive`].

use super::World;
use crate::geometry::primitives::Primitive;
use crate::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use crate::geometry::trimesh::build::{MeshDesc, MeshIndices};
use crate::geometry::trimesh::TriMesh;
use crate::geometry::{Geometry, PhysGeometry};
use crate::math::{Quat, Vec3};
use std::sync::Arc;

/// `CGeomManager::CreatePrimitive(type, prim*)` (0x3514B9F0) for the primitive types: the
/// class ctor + `SetData`. Heightfields go through P1c; triangles are not geometries.
#[must_use]
pub fn create_primitive(prim: &Primitive) -> Option<Arc<dyn Geometry>> {
    Some(match prim {
        Primitive::Box(b) => Arc::new(BoxGeom::new(b)),
        Primitive::Sphere(s) => Arc::new(SphereGeom::new(s)),
        Primitive::Cylinder(c) => Arc::new(CylinderGeom::new(c)),
        Primitive::Capsule(c) => Arc::new(CapsuleGeom::new(c)),
        Primitive::Ray(r) => Arc::new(RayGeom::new(r)),
        // P1c: `CHeightfield` (ctor 0x35103E50 + SetData 0x35154F40); SetData's write of
        // `stepr` into the caller's primitive goes to a copy here (world slot 17 recomputes
        // the same values for every caller).
        Primitive::Heightfield(h) => {
            let mut p = *h;
            Arc::new(
                crate::geometry::heightfield::protocol::SharedHeightfield::new(
                    crate::geometry::heightfield::Heightfield::create(&mut p).ok()?,
                ),
            )
        }
        Primitive::Triangle(_) => return None,
    })
}

/// `CGeomManager::RegisterGeometry(geom, defSurfaceIdx, matMapping, nMats)` (0x3514AE40): a
/// `phys_geometry` from the pool (zeroed), filled by the geometry's `CalcPhysicalProperties`
/// (slot 7; the ray writes nothing), the inertia floored at `0.01·max(I)` with the min/max
/// idioms, refcount 1.
#[must_use]
pub fn register_geometry(
    geom: Arc<dyn Geometry>,
    surface_idx: i32,
    mat_mapping: Option<&[i32]>,
) -> Arc<PhysGeometry> {
    let mut pg = PhysGeometry {
        geom: geom.clone(),
        inertia: Vec3::ZERO,
        q: Quat::new(0.0, 0.0, 0.0, 0.0),
        origin: Vec3::ZERO,
        volume: 0.0,
        surface_idx,
        mat_mapping: mat_mapping.map(<[i32]>::to_vec).unwrap_or_default(),
    };
    if let Some(p) = geom.calc_physical_properties() {
        pg.inertia = p.inertia;
        pg.q = p.q;
        pg.origin = p.origin;
        pg.volume = p.volume;
    }
    floor_inertia(&mut pg);
    Arc::new(pg)
}

impl World {
    /// Convenience: `CreatePrimitive` + `RegisterGeometry(geom, 0, null, 0)`, what the oracle
    /// scenarios and most callers do.
    #[must_use]
    pub fn register_primitive(&self, prim: &Primitive) -> Option<Arc<PhysGeometry>> {
        create_primitive(prim).map(|g| register_geometry(g, 0, None))
    }
}

/// RegisterGeometry's inertia floor: `I_k = max(I_k, 0.01·max(max(Ix, Iy), Iz))` with the
/// `(|a − b| + (a + b))·0.5` max idiom.
fn floor_inertia(pg: &mut PhysGeometry) {
    let (ix, iy, iz) = (pg.inertia.x, pg.inertia.y, pg.inertia.z);
    let v14 = ((ix - iy).abs() + (ix + iy)) * 0.5;
    let v15 = (((v14 - iz).abs() + (iz + v14)) * 0.5) * 0.01;
    pg.inertia = Vec3::new(
        ((ix - v15).abs() + (ix + v15)) * 0.5,
        ((iy - v15).abs() + (iy + v15)) * 0.5,
        ((iz - v15).abs() + (iz + v15)) * 0.5,
    );
}

/// [`register_geometry`] for a mesh that is not shared yet: `CalcPhysicalProperties` runs on
/// the mesh object itself, as in the original, so its side effect (the cached volume `+516`
/// takes the computed value when it is still unused) is kept; then the caches are warmed
/// ([`TriMesh::warm_caches`], which only fills caches that are still unused) and the mesh is
/// shared.
#[must_use]
pub fn register_mesh(
    mut mesh: TriMesh,
    surface_idx: i32,
    mat_mapping: Option<&[i32]>,
) -> Arc<PhysGeometry> {
    let p = TriMesh::calc_physical_properties(&mut mesh);
    mesh.warm_caches();
    let mut pg = PhysGeometry {
        geom: Arc::new(mesh),
        inertia: p.inertia,
        q: p.q,
        origin: p.origin,
        volume: p.volume,
        surface_idx,
        mat_mapping: mat_mapping.map(<[i32]>::to_vec).unwrap_or_default(),
    };
    floor_inertia(&mut pg);
    Arc::new(pg)
}

/// `SMeshBVParams` (the last argument of `CreateMesh`, slot 3): `+0` nMinTrisPerNode, `+4`
/// nMaxTrisPerNode, `+8` favorAABB (the OBB cost factor).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MeshBvParams {
    pub min_tris: i32,
    pub max_tris: i32,
    pub favor_aabb: f32,
}

/// What `CreateMesh` returned: a `CTriMesh`, or the primitive geometry slot 6 replaced it with
/// (built by `CreatePrimitive`, slot 7).
#[derive(Debug)]
pub enum CreatedMesh {
    Mesh(Box<TriMesh>),
    Primitive(Arc<dyn Geometry>),
}

impl CreatedMesh {
    /// `RegisterGeometry(geom, surface_idx, mapping)` of the result (slot 9).
    #[must_use]
    pub fn register(self, surface_idx: i32, mat_mapping: Option<&[i32]>) -> Arc<PhysGeometry> {
        match self {
            CreatedMesh::Mesh(m) => register_mesh(*m, surface_idx, mat_mapping),
            CreatedMesh::Primitive(g) => register_geometry(g, surface_idx, mat_mapping),
        }
    }
}

/// The arguments of `CreateMesh` common to slots 3 / 4 / 5: the (strided) vertices, the
/// indices, the per-triangle materials and foreign ids, the triangle count and the flags.
#[derive(Clone, Copy, Debug)]
pub struct MeshSource<'a> {
    pub vertices: &'a [Vec3],
    pub indices: MeshIndices<'a>,
    pub mats: Option<&'a [i8]>,
    pub foreign_idx: Option<&'a [i32]>,
    pub n_tris: i32,
    pub flags: u32,
}

impl World {
    /// The geometry manager's "creation blocked" test of slots 3 and 5: `GetPhysVars()+496`
    /// (world `+600`, [`crate::physvars::PhysVars::unk_600`]) non-zero → null.
    fn geoman_blocked(&self) -> bool {
        self.vars.unk_600 != 0
    }

    /// `CGeomManager::CreateMesh` (slot 3, 0x3514B710):
    ///
    /// 1. null when the world blocks creation ([`Self::geoman_blocked`]) or `n_tris <= 0`;
    /// 2. slot 6 (0x35098490, [`crate::geometry::trimesh::approx::try_primitive`]): with
    ///    `tol > 0` (and uniform materials, or `tol > 0.5`) the mesh may be replaced by a box,
    ///    cylinder, sphere or capsule (flags 0x1000 / 0x800 / 0x2000 / 0x200000), which is then
    ///    built by `CreatePrimitive` (slot 7);
    /// 3. otherwise a `CTriMesh` from the data (sub_351CF1B0) with the BV parameters.
    ///
    /// Not ported: flag 0x80000 builds a voxel geometry (`operator new(0x370)`,
    /// sub_3514DA60 + sub_351CF1B0 with (2, 4, 1.0) + sub_351DCB70 with a grid built from
    /// `params`); the port returns `None` for it. Slot 4 (the entry the oracle and the game's
    /// mesh loading use) clears that flag.
    #[must_use]
    pub fn create_mesh(
        &self,
        src: &MeshSource<'_>,
        tol: f32,
        params: &MeshBvParams,
    ) -> Option<CreatedMesh> {
        if self.geoman_blocked() || src.n_tris <= 0 {
            return None;
        }
        let ntu = 3 * src.n_tris as usize;
        // slot 6 reads the indices as 16-bit values at the caller's stride
        let idx16: Vec<u16> = match src.indices {
            MeshIndices::U16(a) => a[..ntu].to_vec(),
            MeshIndices::I32(a) => a[..ntu].iter().map(|&i| i as u16).collect(),
        };
        if let Some(prim) = crate::geometry::trimesh::approx::try_primitive(
            src.vertices,
            &idx16,
            src.mats,
            src.n_tris,
            src.flags,
            tol,
        ) {
            return create_primitive(&prim).map(CreatedMesh::Primitive);
        }
        if src.flags & 0x8_0000 != 0 {
            return None;
        }
        Some(CreatedMesh::Mesh(Box::new(TriMesh::from_data(&MeshDesc {
            vertices: src.vertices,
            indices: src.indices,
            mats: src.mats,
            foreign_idx: src.foreign_idx,
            n_tris: src.n_tris,
            flags: src.flags,
            min_tris: params.min_tris,
            max_tris: params.max_tris,
            obb_factor: params.favor_aabb,
        }))))
    }

    /// `CGeomManager::CreateMesh` (slot 4, 0x35095CB0): slot 3 with the BV parameters as
    /// arguments and flag 0x80000 (voxel) cleared (`btr eax, 0x13`).
    #[must_use]
    pub fn create_mesh_ex(
        &self,
        src: &MeshSource<'_>,
        tol: f32,
        min_tris: i32,
        max_tris: i32,
        favor_aabb: f32,
    ) -> Option<CreatedMesh> {
        let s = MeshSource {
            flags: src.flags & !0x8_0000,
            ..*src
        };
        self.create_mesh(
            &s,
            tol,
            &MeshBvParams {
                min_tris,
                max_tris,
                favor_aabb,
            },
        )
    }

    /// `CGeomManager::CreateMesh` (slot 5, 0x35095BB0): null when the world blocks creation;
    /// `approx_mode == 7` adds flags 0x203800 (try every primitive); then slot 4 with
    /// `tol 0.05`, 2 / 4 triangles per node and favorAABB 1.
    #[must_use]
    pub fn create_mesh_default(
        &self,
        src: &MeshSource<'_>,
        approx_mode: i32,
    ) -> Option<CreatedMesh> {
        if self.geoman_blocked() {
            return None;
        }
        let mut s = *src;
        if approx_mode == 7 {
            s.flags |= 0x20_3800;
        }
        self.create_mesh_ex(&s, 0.05, 2, 4, 1.0)
    }
}
