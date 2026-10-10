//! `CTriMesh` serialization: slot 25 `Save` (0x351CECB0), slot 26 `Load(stream, pVtx, pIdx,
//! pMats)` (0x351D83B0) and slot 27 `Load(stream)` (0x351033E0, slot 26 without render arrays).
//! The CGF phys-geometry blob around it (version, 64-byte header, type id) is read and written
//! by [`crate::geometry::shapes::stream`] (`load_phys_geometry_any` / `save_phys_geometry_mesh`).
//!
//! Stream layout (little endian):
//!
//! ```text
//! u32 nVertices, u32 nTris, u32 maxVertexValency, u32 flags (Load ORs 0x8000)
//! u8 hasVtxMap;    [u16 vtxMap[nVertices]]           (low halves)
//! u8 hasForeignIdx; [u16 foreignIdx[nTris]]          (low halves)
//! if !hasForeignIdx || flags & 0x400000:
//!     [Vec3 vertices[nVertices]]  unless flags & 1 (shared vertices)
//!     u16 indices[3·nTris]
//!     u8 hasMats; [i8 mats[nTris]]
//! 16 bytes        Save: uninitialised stack (kept in `TriMesh::save_pad`); Load: skipped
//! u32 0           Load: ≠ 0 → degenerate mesh (see below)
//! 4 × {u32 convex result, f32 tolerance}             (the IsConvex cache, +480 / +496)
//! u32 tree type (0 OBB, 1 AABB, 2 single box), tree slot 21 payload
//! ```
//!
//! **Load**, in order: header (flags |= 0x8000, which also keeps the vertex map); with foreign
//! ids and no 0x400000 the geometry is the **render mesh's**: vertices from `pVtx`, triangle `t`
//! = render triangle `foreignIdx[t]` of `pIdx` (16-bit), material `pMats[foreignIdx[t]]`;
//! otherwise vertices (or `pVtx` with flag 1), indices and materials from the stream. Indices are
//! mapped through the vertex map; normals (sub_350FDBE0) and topology (sub_351BFA60) are
//! rebuilt. A non-zero "single box" word makes the mesh degenerate: **nTris = 0** and an
//! identity zero-size single box, and nothing else is read or computed (the islands, the convex
//! cache, the valency fix-up, `m_minVtxDist` and the vertex trimming are skipped). Otherwise:
//! islands (sub_351C0D80) and the multi-island flag; the convex cache is read with each result
//! masked by `!multiIsland`; `IsConvex(0.02)`; the tree (slot 22) and, for a single box only,
//! `SetGeomConvex` when convex; a stored valency below 3 is recounted (8-bit counters);
//! `m_minVtxDist` from the tree's box; then, without flag 1, **vertex trimming**: `+600 =
//! min(nVertices, min over indices of min(i, map[i]))`, and when `4·(+600) > nVertices` the
//! vertices below it are dropped (indices and map shifted down).
//!
//! **Save** writes what Load reads; the vertex map and foreign ids as their low 16 bits; the
//! geometry is omitted when the mesh has foreign ids and no 0x400000 (render-mesh geometry);
//! the 16 pad bytes are uninitialised stack in the original — the port writes
//! [`TriMesh::save_pad`], which `Load` fills with the bytes it skipped, so a load followed by a
//! save reproduces the original's output for the same input.
#![allow(clippy::field_reassign_with_default)] // fields are read from the stream in order

use super::build::{max_valency_u8, min_vtx_dist_of};
use super::TriMesh;
use crate::geometry::bvtree::select::MeshTree;
use crate::geometry::bvtree::{
    AabbTree, MemReader, MemWriter, ObbTree, SingleBoxTree, StreamError,
};
use crate::geometry::primitives::BoxPrim;
use crate::math::{Mat33, Vec3};

/// The render-mesh arrays `Load` (slot 26) takes: `pVtx` (stride 12), `pIdx` (16-bit, stride 2)
/// and the per-face materials. `CStatObj` passes its render mesh's positions and indices and
/// the per-face material ids of its subsets; slot 27 passes none.
#[derive(Clone, Copy, Debug, Default)]
pub struct RenderArrays<'a> {
    pub vertices: Option<&'a [Vec3]>,
    pub indices: Option<&'a [u16]>,
    pub mats: Option<&'a [i8]>,
}

fn missing(what: &str) -> StreamError {
    StreamError::BadValue(format!("CTriMesh::Load: {what}"))
}

impl TriMesh {
    /// Slot 25 `Save` (0x351CECB0). The tree is required (the original dereferences it).
    pub fn save(&self, w: &mut MemWriter) {
        let nt = self.n_tris.max(0) as usize;
        let nv = self.n_verts.max(0) as usize;
        w.i32(self.n_verts);
        w.i32(self.n_tris);
        w.i32(self.max_vertex_valency);
        w.u32(self.flags);
        match &self.vtx_map {
            Some(map) => {
                w.u8(1);
                for &m in &map[..nv] {
                    w.u16(m as u16);
                }
            }
            None => w.u8(0),
        }
        match &self.foreign_idx {
            Some(f) => {
                w.u8(1);
                for &x in &f[..nt] {
                    w.u16(x as u16);
                }
            }
            None => w.u8(0),
        }
        if self.foreign_idx.is_none() || self.flags & super::build::flags::SAVE_GEOMETRY != 0 {
            if self.flags & super::build::flags::SHARED_VERTICES == 0 {
                for &v in &self.vertices[..nv] {
                    w.vec3(v);
                }
            }
            for &i in &self.indices[..3 * nt] {
                w.u16(i as u16);
            }
            match &self.mats {
                Some(m) => {
                    w.u8(1);
                    for &x in &m[..nt] {
                        w.u8(x as u8);
                    }
                }
                None => w.u8(0),
            }
        }
        w.bytes(&self.save_pad);
        w.u32(0);
        for k in 0..4 {
            w.i32(self.convex_cache[k]);
            w.f32(self.convex_tol[k]);
        }
        if let Some(t) = &self.tree {
            w.i32(t.as_dyn().tree_type().id());
            t.as_dyn().save(w);
        }
    }

    /// The bytes [`TriMesh::save`] writes.
    #[must_use]
    pub fn save_to_vec(&self) -> Vec<u8> {
        let mut w = MemWriter::new();
        self.save(&mut w);
        w.into_bytes()
    }

    /// Slot 27 `Load(stream)` (0x351033E0): slot 26 without render arrays.
    ///
    /// # Errors
    /// See [`TriMesh::load`].
    pub fn load_plain(r: &mut MemReader<'_>) -> Result<TriMesh, StreamError> {
        Self::load(r, &RenderArrays::default())
    }

    /// A default-constructed mesh (0x351BF470) loaded by slot 26 (0x351D83B0) from `r`, with
    /// the render arrays `ra` (see the module docs).
    ///
    /// # Errors
    /// A truncated stream, an unknown tree type (the original crashes), or render arrays
    /// missing / too short where the stream needs them (the original reads through null or
    /// past the arrays).
    pub fn load(r: &mut MemReader<'_>, ra: &RenderArrays<'_>) -> Result<TriMesh, StreamError> {
        use super::build::flags as fl;
        let mut m = TriMesh::default();
        m.n_verts = r.i32()?;
        m.n_tris = r.i32()?;
        m.max_vertex_valency = r.i32()?;
        m.flags = r.u32()? | fl::FULL_VTX_MAP;
        let nv = m.n_verts.max(0) as usize;
        let nt = m.n_tris.max(0) as usize;
        m.indices = vec![0; 3 * nt];
        let has_map = r.u8()? != 0;
        let map: Option<Vec<i32>> = if has_map {
            Some(
                (0..nv)
                    .map(|_| r.u16().map(i32::from))
                    .collect::<Result<_, _>>()?,
            )
        } else {
            None
        };
        let has_foreign = r.u8()? != 0;
        let render_geometry = has_foreign && m.flags & fl::SAVE_GEOMETRY == 0;
        let shared_vertices = |nv: usize| -> Result<Vec<Vec3>, StreamError> {
            let v = ra.vertices.ok_or_else(|| missing("no pVtx"))?;
            v.get(..nv)
                .map(<[Vec3]>::to_vec)
                .ok_or_else(|| missing("pVtx too short"))
        };
        if render_geometry {
            m.vertices = shared_vertices(nv)?;
            let ridx = ra.indices.ok_or_else(|| missing("no pIdx"))?;
            if let Some(rm) = ra.mats {
                m.mats = Some(
                    rm.get(..nt)
                        .ok_or_else(|| missing("pMats too short"))?
                        .to_vec(),
                );
            }
            let mut fids = Vec::with_capacity(nt);
            for t in 0..nt {
                let f = r.u16()? as usize;
                fids.push(f as i32);
                for k in 0..3 {
                    m.indices[3 * t + k] = i32::from(
                        *ridx
                            .get(3 * f + k)
                            .ok_or_else(|| missing("pIdx too short"))?,
                    );
                }
                if let (Some(dst), Some(rm)) = (m.mats.as_mut(), ra.mats) {
                    dst[t] = *rm.get(f).ok_or_else(|| missing("pMats too short"))?;
                }
            }
            m.foreign_idx = Some(fids);
        } else {
            if has_foreign {
                m.foreign_idx = Some(
                    (0..nt)
                        .map(|_| r.u16().map(i32::from))
                        .collect::<Result<_, _>>()?,
                );
            }
            m.vertices = if m.flags & fl::SHARED_VERTICES != 0 {
                shared_vertices(nv)?
            } else {
                (0..nv).map(|_| r.vec3()).collect::<Result<_, _>>()?
            };
            for i in m.indices.iter_mut() {
                *i = i32::from(r.u16()?);
            }
            m.mats = if r.u8()? != 0 {
                Some(r.bytes(nt)?.iter().map(|&b| b as i8).collect())
            } else {
                None
            };
        }
        if let Some(map) = &map {
            for i in m.indices.iter_mut() {
                *i = *map
                    .get(*i as usize)
                    .ok_or_else(|| missing("index outside the vertex map"))?;
            }
        }
        m.vtx_map = map;
        if m.indices.iter().any(|&i| i as usize >= nv) {
            return Err(missing("index outside the vertices"));
        }
        m.normals = (0..nt).map(|t| m.compute_normal(t)).collect();
        m.build_topology();
        m.save_pad.copy_from_slice(r.bytes(16)?);
        if r.u32()? != 0 {
            m.n_tris = 0;
            let mut t = SingleBoxTree::default();
            t.set_box(&BoxPrim {
                basis: Mat33::IDENTITY,
                oriented: 0,
                center: Vec3::ZERO,
                size: Vec3::ZERO,
            });
            t.n_prims = 0;
            m.tree = Some(MeshTree::SingleBox(t));
            return Ok(m);
        }
        m.multi_island = i32::from(m.build_islands() > 1);
        for k in 0..4 {
            m.convex_cache[k] = r.i32()? & (m.multi_island ^ 1);
            m.convex_tol[k] = r.f32()?;
        }
        m.is_convex = m.is_convex(f32::from_bits(0x3CA3_D70A));
        let tt = r.i32()?;
        m.tree = Some(match tt {
            0 => MeshTree::Obb(ObbTree::load(r, m.n_tris)?),
            1 => MeshTree::Aabb(AabbTree::load(r, m.n_tris)?),
            2 => MeshTree::SingleBox(SingleBoxTree::load(r)?),
            t => return Err(StreamError::BadValue(format!("BV tree type {t}"))),
        });
        if tt == 2 && m.is_convex != 0 {
            m.set_tree_convex();
        }
        if m.max_vertex_valency < 3 {
            m.max_vertex_valency = max_valency_u8(&m.indices, m.n_verts);
        }
        if let Some(t) = &m.tree {
            m.min_vtx_dist = min_vtx_dist_of(t);
        }
        if m.flags & fl::SHARED_VERTICES == 0 {
            let mut vmin = m.n_verts;
            for &i in &m.indices {
                let x = match &m.vtx_map {
                    Some(map) => map[i as usize].min(i),
                    None => i,
                };
                vmin = vmin.min(x);
            }
            m.i_vtx_min = vmin;
            if vmin.wrapping_mul(4) > m.n_verts {
                let s = vmin as usize;
                m.vertices.drain(..s);
                for i in m.indices.iter_mut() {
                    *i -= vmin;
                }
                if let Some(map) = m.vtx_map.as_mut() {
                    map.drain(..s);
                    for x in map.iter_mut() {
                        *x -= vmin;
                    }
                }
                m.n_verts -= vmin;
            }
        }
        Ok(m)
    }
}
