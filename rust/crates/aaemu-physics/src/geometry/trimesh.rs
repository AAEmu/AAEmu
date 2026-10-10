//! **P1c — triangle meshes**: `CTriMesh` (x64 vtable 0x351F2C18 region, 62 slots, object
//! 0x260 bytes, ctor 0x351BF470, constructor-from-data 0x351CF1B0, `geometry.md` §1.3, §2, §3.5).
//!
//! The struct mirrors the binary's object field by field (offsets in the field docs); arrays the
//! original keeps as raw pointers are `Vec`s / `Option<Vec>`s, and the element counts the
//! original keeps separately (`+120` nTris, `+124` nVertices) are kept as explicit fields
//! because several routines change them independently of the arrays (Load trims vertices,
//! a degenerate loaded mesh sets nTris to 0, …).
//!
//! Submodules (each ports a group of slots / helpers):
//!
//! | module | content |
//! |---|---|
//! | [`build`] | constructor from data 0x351CF1B0: vertex welding, degenerate-triangle removal, vertex map, tree choice, normals |
//! | [`topology`] | neighbour topology 0x351BFA60, islands 0x351C0D80, `IsConvex` (slot 21) |
//! | [`stream`] | `Save` (slot 25, 0x351CECB0), `Load` (slot 26, 0x351D83B0) |
//! | [`props`] | `GetBBox`, `CalcPhysicalProperties`, volume, centre, extent, feature and per-triangle accessors |
//! | [`inside`] | `PointInsideStatus` (slots 8 / 61) |
//! | [`fluid`] | slots 12 / 13 / 14 (pressure, buoyancy, medium resistance) |
//! | [`closest`] | slot 11 `FindClosestPoint` |
//! | [`protocol`] | the `geometry_under_test` protocol: slots 48, 53, 55, 56, 57 (mesh and primitives) |
//! | [`register`] | slot 54 `RegisterIntersection` and the feature-polygon helpers (slots 58 / 59) |
//! | [`intersect`] | the generic `CGeometry::Intersect` (0x3514FD70) with its tree traversals and the mesh's slot 10 |
//! | [`raytest`] | slot 22 `PrepareForRayTest` (ray grid) and the mesh ray path |
//! | [`unproject_sphere`] | slot 49 `UnprojectSphere` |

pub mod build;
pub mod closest;
pub mod fluid;
pub mod inside;
pub mod intersect;
pub mod props;
pub mod protocol;
pub mod raytest;
pub mod register;
pub mod stream;
pub mod topology;
pub mod unproject_sphere;

use crate::geometry::bvtree::select::MeshTree;
use crate::math::Vec3;

/// One entry of the island list (`+128`, 40 bytes each, built by sub_351C0D80, see
/// [`topology`]): `+0` first triangle of the island's triangle list (linked through
/// [`TriMesh::tri_island`]), `+4` triangle count, `+8`/`+12`/`+16` three ints set to −1 (not
/// read by the ported code), `+20` signed volume, `+24` volume-weighted centre, `+36` 0.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct MeshIsland {
    /// The 10 dwords of the record as the original stores them.
    pub words: [u32; 10],
}

impl MeshIsland {
    /// `+0` the island's first triangle (head of its list in [`TriMesh::tri_island`]).
    #[must_use]
    pub fn first_tri(&self) -> i32 {
        self.words[0] as i32
    }

    /// `+4` the island's triangle count.
    #[must_use]
    pub fn n_tris(&self) -> i32 {
        self.words[1] as i32
    }

    /// `+20` the island's signed volume.
    #[must_use]
    pub fn volume(&self) -> f32 {
        f32::from_bits(self.words[5])
    }

    /// `+24` the island's centre (the mean of its vertex references).
    #[must_use]
    pub fn center(&self) -> Vec3 {
        Vec3::new(
            f32::from_bits(self.words[6]),
            f32::from_bits(self.words[7]),
            f32::from_bits(self.words[8]),
        )
    }
}

/// `CTriMesh` (0x260 bytes). Field offsets are those of the x64 object.
#[derive(Clone, Debug)]
pub struct TriMesh {
    /// `+16` m_minVtxDist (inferred): `0.0002 · max half-size of the tree's root box`.
    pub min_vtx_dist: f32,
    /// `+20` collision priority: 1 for meshes.
    pub priority: i32,
    /// `+24` convexity flag (result of slot 21 at build/load time).
    pub is_convex: i32,
    /// `+48` the BV tree.
    pub tree: Option<MeshTree>,
    /// `+56` 3 vertex indices per triangle.
    pub indices: Vec<i32>,
    /// `+64` per-triangle material ids (slot 16), `None` = null pointer.
    pub mats: Option<Vec<i8>>,
    /// `+72` per-triangle foreign ids (slot 18).
    pub foreign_idx: Option<Vec<i32>>,
    /// `+80` vertices (stride `+88` = 12).
    pub vertices: Vec<Vec3>,
    /// `+96` per-triangle normals (slot 19).
    pub normals: Vec<Vec3>,
    /// `+104` per-vertex map (welded vertex of each original vertex).
    pub vtx_map: Option<Vec<i32>>,
    /// `+112` neighbour triangle across each edge (`< 0` = open edge).
    pub topology: Vec<[i32; 3]>,
    /// `+120` nTris.
    pub n_tris: i32,
    /// `+124` nVertices.
    pub n_verts: i32,
    /// `+128` island list (`+136` count; null until first computed).
    pub islands: Option<Vec<MeshIsland>>,
    /// `+144` per-triangle island links (sub_351C0D80).
    pub tri_island: Option<Vec<u32>>,
    /// `+152` mesh flags (`primitives::mesh_flags`, `bvtree::select`).
    pub flags: u32,
    /// `+160` auxiliary per-triangle ints (permuted with the indices at build).
    pub aux: Option<Vec<i32>>,
    /// `+168` maximum vertex valency.
    pub max_vertex_valency: i32,
    /// `+476` ray-grid build count (slot 22).
    pub ray_grid_ready: i32,
    /// `+176..+392` the ray grid (up to two layers) built by slot 22 (`PrepareForRayTest`, 0x351D7C40) and read by
    /// `PointInsideStatus` (slot 61) and the mesh ray path; `None` until built.
    pub ray_grid: Option<inside::RayGrid>,
    /// `+32` foreign data pointer (slots 35 / 37; opaque to the engine).
    pub foreign_data: crate::sentinel::RawPtr,
    /// `+40` iForeignData (slots 36 / 37).
    pub i_foreign_data: i32,
    /// `+544` the foreign data returned by slot 35 for id −1.
    pub foreign_data_mesh: crate::sentinel::RawPtr,
    /// `+480` IsConvex cache: results for 4 tolerances.
    pub convex_cache: [i32; 4],
    /// `+496` IsConvex cache: the tolerances (init −1).
    pub convex_tol: [f32; 4],
    /// `+512` set when the mesh has more than one island.
    pub multi_island: i32,
    /// `+516` cached volume (unused sentinel 0xFFBFFFFF until computed).
    pub cached_volume: f32,
    /// `+520` / `+524` cached extents (slot 42).
    pub cached_extent: [f32; 2],
    /// `+528` cached centre (slot 32); `.x` holds the sentinel until computed.
    pub cached_center: Vec3,
    /// `+540` number of open plus non-manifold edges found by the last topology pass
    /// (sub_351BFA60); slot 38 returns it.
    pub n_bad_edges: i32,
    /// `+588` error / invalid count (slot 34).
    pub n_errors: i32,
    /// `+600` lowest referenced vertex index (Load's vertex trimming).
    pub i_vtx_min: i32,
    /// The 16 bytes `Save` writes from uninitialised stack memory (kept from `Load` so that a
    /// loaded mesh saves back to the identical blob; the original writes stack garbage).
    pub save_pad: [u8; 16],
}

impl Default for TriMesh {
    /// The state after the constructor 0x351BF470.
    fn default() -> Self {
        let unused = f32::from_bits(crate::sentinel::UNUSED_F32_BITS);
        Self {
            min_vtx_dist: 0.0,
            priority: 1,
            is_convex: 0,
            tree: None,
            indices: Vec::new(),
            mats: None,
            foreign_idx: None,
            vertices: Vec::new(),
            normals: Vec::new(),
            vtx_map: None,
            topology: Vec::new(),
            n_tris: 0,
            n_verts: 0,
            islands: None,
            tri_island: None,
            flags: 0,
            aux: None,
            max_vertex_valency: 0,
            ray_grid_ready: 0,
            ray_grid: None,
            foreign_data: crate::sentinel::RawPtr::NULL,
            i_foreign_data: 0,
            foreign_data_mesh: crate::sentinel::RawPtr::NULL,
            convex_cache: [0; 4],
            convex_tol: [-1.0; 4],
            multi_island: 0,
            cached_volume: unused,
            cached_extent: [unused; 2],
            cached_center: Vec3::new(unused, 0.0, 0.0),
            n_bad_edges: 0,
            n_errors: 0,
            i_vtx_min: 0,
            save_pad: [0; 16],
        }
    }
}
