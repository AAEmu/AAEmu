//! `CTriMesh`: the default constructor (0x351BF470, [`TriMesh::default`]) and the constructor
//! from data (sub_351CF1B0, x86 0x351E53D0): [`TriMesh::from_data`].
//!
//! The constructor, in the original's order:
//!
//! 1. `nVertices = 1 + max` over the triangles' indices, **read as 16-bit values** even when the
//!    caller passes 32-bit indices (the low half of each `int`; kept as in the original — a
//!    32-bit index ≥ 65536 makes the vertex count too small and the original then reads out of
//!    bounds; the port panics instead).
//! 2. Vertices: shared with flag 0x1 (the port copies them, the flag is kept), else copied.
//!    Indices: used in place with flag 0x2 when the stride is 4, else copied as 16-bit values
//!    (flag 0x2 cleared). Materials: shared with flag 0x4 (or absent), else copied. Foreign ids:
//!    copied; shared with flag 0x8; with flag 0x8 and none given, identity ids are created and
//!    0x8 is cleared. Stored flags = `flags & 0x10800F` (minus the cleared bits).
//! 3. Maximum vertex valency (+168): 8 with flag 0x80000, else counted with **8-bit counters**
//!    (a vertex used by 256 triangles counts 0; kept).
//! 4. Vertex welding (skipped with flag 0x20000): the vertices are sorted on each axis
//!    (sub_351DB420, a Hoare quicksort); for each unwelded vertex the vertices with the same x,
//!    the same y and the same z (`ucomiss` equality: `-0.0 == 0.0`, NaN equal to anything) are
//!    collected per axis
//!    (sorted with sub_351BDE80), intersected (sub_351BD620) and mapped to it. Vertices are not
//!    compacted; the indices are remapped to the lowest equal vertex.
//! 5. Degenerate triangles (two equal welded vertices) are removed by swapping with the last
//!    triangle (indices, materials, foreign ids) unless flag 0x2000000; only when welding ran.
//! 6. Vertex map (+104): kept with flag 0x8000, or with 0x10000 when it is not the identity.
//! 7. BV tree: [`build_mesh_tree`] (principal axes for 0x40000 / 0x80, AABB / OBB / single box,
//!    cheapest wins) — the P1b port of the same inline code.
//! 8. Normals ([`tri_normal`]), topology ([`TriMesh::build_topology`]); then, unless flag
//!    0x80000: islands ([`TriMesh::build_islands`], which also fills the cached volume and
//!    centre), `IsConvex(0.02)` (slot 21) and the tree's `SetGeomConvex` when convex, and
//!    `m_minVtxDist = 0.0002 · max half-size of the tree's box`. With flag 0x80000 the mesh is
//!    marked multi-island and non-convex and `m_minVtxDist` stays 0.
//!
//! **Deviations (storage only):** shared arrays (flags 0x1 / 0x2 / 0x4 / 0x8) are copied; the
//! flags still say "shared", which only matters to `Save` (vertices are not written with flag
//! 0x1). Where the original swaps degenerate triangles *inside a shared caller array*
//! (materials / foreign ids with 0x4 / 0x8, indices with 0x2), the caller's array is left
//! untouched. The arrays the original allocates for the original triangle count are truncated
//! to the final count (the removed triangles' entries are never read).
// NaN semantics of the original compares (`comiss` + jcc) are spelled out on purpose.
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::double_comparisons)]

use super::TriMesh;
use crate::geometry::bvtree::select::{build_mesh_tree, MeshTree};
use crate::geometry::bvtree::BuildMesh;
use crate::math::Vec3;

/// The constructor's flag bits (`CTriMesh` build flags; the tree bits are in
/// [`crate::geometry::primitives::mesh_flags`] and [`crate::geometry::bvtree::select`]).
pub mod flags {
    /// The vertices are the caller's (not copied by the original; `Save` omits them).
    pub const SHARED_VERTICES: u32 = 0x1;
    /// 32-bit indices used in place (cleared when the indices are copied).
    pub const SHARED_INDICES: u32 = 0x2;
    /// The materials are the caller's.
    pub const SHARED_MATS: u32 = 0x4;
    /// The foreign ids are the caller's; with no foreign ids given, identity ids are created.
    pub const SHARED_FOREIGN_IDX: u32 = 0x8;
    /// Keep the vertex map (+104).
    pub const FULL_VTX_MAP: u32 = 0x8000;
    /// Keep the vertex map when welding changed something.
    pub const VTX_MAP: u32 = 0x1_0000;
    /// No vertex welding (and no degenerate-triangle removal).
    pub const NO_VTX_MERGE: u32 = 0x2_0000;
    /// Skip the valency count (valency 8), the islands, `IsConvex` and `m_minVtxDist`.
    pub const NO_VALENCY: u32 = 0x8_0000;
    /// `CalcPhysicalProperties` returns zeroed properties.
    pub const NO_PHYS_PROPS: u32 = 0x10_0000;
    /// `Save` writes the geometry even though the mesh has foreign ids.
    pub const SAVE_GEOMETRY: u32 = 0x40_0000;
    /// Keep degenerate triangles.
    pub const KEEP_DEGENERATE: u32 = 0x200_0000;
    /// The bits the constructor stores in the mesh flags (+152).
    pub const STORED: u32 = 0x10_800F;
}

/// The constructor's index array (the original takes a strided pointer).
#[derive(Clone, Copy, Debug)]
pub enum MeshIndices<'a> {
    /// 16-bit indices (stride 2).
    U16(&'a [u16]),
    /// 32-bit indices (stride 4): used in place with [`flags::SHARED_INDICES`], else truncated to
    /// their low 16 bits.
    I32(&'a [i32]),
}

impl MeshIndices<'_> {
    /// The 16-bit value at the start of element `k` (what the original reads).
    fn low16(&self, k: usize) -> u16 {
        match self {
            MeshIndices::U16(a) => a[k],
            MeshIndices::I32(a) => a[k] as u16,
        }
    }

    fn len(&self) -> usize {
        match self {
            MeshIndices::U16(a) => a.len(),
            MeshIndices::I32(a) => a.len(),
        }
    }
}

/// The arguments of the constructor from data (sub_351CF1B0):
/// `(pVertices, pIndices, pMats, pForeignIdx, nTris, flags, nMinTrisPerNode,
/// nMaxTrisPerNode, favorAABB)`.
#[derive(Clone, Copy, Debug)]
pub struct MeshDesc<'a> {
    /// The vertices; at least `1 + max index` of them (missing ones read as zero).
    pub vertices: &'a [Vec3],
    /// `3 · n_tris` vertex indices.
    pub indices: MeshIndices<'a>,
    /// Per-triangle material ids (`n_tris`), or none.
    pub mats: Option<&'a [i8]>,
    /// Per-triangle foreign ids (`n_tris`), or none.
    pub foreign_idx: Option<&'a [i32]>,
    pub n_tris: i32,
    /// Build flags ([`flags`], tree bits 0x20 / 0x40 / 0x80 / 0x40000 / 0x100 / 0x400 / 0x4000).
    pub flags: u32,
    /// Minimum triangles per tree node side.
    pub min_tris: i32,
    /// Maximum triangles per tree leaf.
    pub max_tris: i32,
    /// The OBB tree's cost factor (the last argument).
    pub obb_factor: f32,
}

/// The triangle normal of the constructor's normal loop and of sub_350FDBE0 (single
/// precision): `n = (v1−v0)×(v2−v0)`, normalised by `1/√(|n|²)` with `|n|² = (y² + x²) + z²`;
/// `(0, 0, 1)` when `|n|² ≤ 1e-20` (or NaN).
#[must_use]
pub fn tri_normal(p0: Vec3, p1: Vec3, p2: Vec3) -> Vec3 {
    let e1x = p1.x - p0.x;
    let e1y = p1.y - p0.y;
    let e1z = p1.z - p0.z;
    let e2x = p2.x - p0.x;
    let e2y = p2.y - p0.y;
    let e2z = p2.z - p0.z;
    let x = e1y * e2z - e1z * e2y;
    let y = e1z * e2x - e2z * e1x;
    let z = e2y * e1x - e1y * e2x;
    let len2 = (y * y + x * x) + z * z;
    if len2 > f32::from_bits(0x1E3C_E508) {
        let inv = 1.0 / len2.sqrt();
        Vec3::new(x * inv, y * inv, z * inv)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}

/// The maximum vertex valency as the original counts it: one **8-bit** counter per vertex
/// over `indices` (wrapping at 256), then the maximum.
#[must_use]
pub fn max_valency_u8(indices: &[i32], n_verts: i32) -> i32 {
    let mut cnt = vec![0u8; n_verts.max(0) as usize];
    for &i in indices.iter().rev() {
        let c = &mut cnt[i as usize];
        *c = c.wrapping_add(1);
    }
    cnt.iter().fold(0i32, |m, &c| m.max(i32::from(c)))
}

/// sub_351DB420: Hoare quicksort of vertex ids `a[lo..=hi]` by coordinate `axis` (pivot the
/// middle element; the left scan advances while `pivot > x`, the right scan retreats while
/// `!(pivot >= x)` — NaN coordinates make it run off the array in the original; the port panics).
pub(crate) fn sort_by_coord(a: &mut [i32], v: &[Vec3], axis: usize, lo: i32, hi: i32) {
    let c = |a: &[i32], i: i32| v[a[i as usize] as usize].to_array()[axis];
    let pivot = c(a, (lo + hi) / 2);
    let (mut i, mut j) = (lo, hi);
    while i <= j {
        if pivot > c(a, i) {
            loop {
                i += 1;
                if !(pivot > c(a, i)) {
                    break;
                }
            }
        }
        while !(pivot >= c(a, j)) {
            j -= 1;
        }
        if i > j {
            break;
        }
        a.swap(i as usize, j as usize);
        i += 1;
        j -= 1;
    }
    if lo < j {
        sort_by_coord(a, v, axis, lo, j);
    }
    if i < hi {
        sort_by_coord(a, v, axis, i, hi);
    }
}

/// sub_351BDE80: Hoare quicksort of `a[lo..=hi]` (ints, ascending; the right half is the loop).
pub(crate) fn sort_ints(a: &mut [i32], mut lo: i32, hi: i32) {
    loop {
        let pivot = a[((lo + hi) / 2) as usize];
        let (mut i, mut j) = (lo, hi);
        while i <= j {
            while a[i as usize] < pivot {
                i += 1;
            }
            while pivot < a[j as usize] {
                j -= 1;
            }
            if i > j {
                break;
            }
            a.swap(i as usize, j as usize);
            i += 1;
            j -= 1;
        }
        if lo < j {
            sort_ints(a, lo, j);
        }
        if i >= hi {
            break;
        }
        lo = i;
    }
}

/// sub_351BD620: the intersection of the ascending lists `a[..na]` and `b[..nb]` into `out`
/// (branch-free merge; `out[n]` is written before every step, so `out` needs `min(na, nb) + 1`
/// entries). Returns the count.
pub(crate) fn intersect_sorted(a: &[i32], na: i32, b: &[i32], nb: i32, out: &mut [i32]) -> i32 {
    let (mut n, mut ia, mut ib) = (0i32, 0i32, 0i32);
    if (na.wrapping_neg() & nb.wrapping_neg()) < 0 {
        loop {
            let x = a[ia as usize];
            let y = b[ib as usize];
            let d = x.wrapping_sub(y);
            out[n as usize] = x;
            let eq = (d ^ d.wrapping_sub(1)) >> 31;
            n -= eq;
            ia += ((d as u32) >> 31) as i32 - eq;
            ib += ((y.wrapping_sub(x) as u32) >> 31) as i32 - eq;
            if ((ia - na) & (ib - nb)) >= 0 {
                break;
            }
        }
    }
    n
}

/// `ucomiss` + `je`/`jne` equality: true when equal **or unordered** (a NaN compares equal),
/// as the original's welding scans and `IsConvex` cache lookup test it.
#[inline]
pub(crate) fn ueq(a: f32, b: f32) -> bool {
    !(a < b || a > b)
}

/// `(a ^ b ^ ((a ^ b) − 1)) < 0`: the original's "equal ids" test (also true when `a ^ b` is
/// `i32::MIN`).
fn same_id(a: i32, b: i32) -> bool {
    let x = a ^ b;
    (x ^ x.wrapping_sub(1)) < 0
}

impl TriMesh {
    /// The constructor from data, sub_351CF1B0 (see the module docs for the steps and the
    /// flags).
    ///
    /// # Panics
    /// When `indices` holds fewer than `3 · n_tris` entries, `mats` / `foreign_idx` fewer than
    /// `n_tris`, or a 32-bit index exceeds the vertex count derived from the low 16 bits (the
    /// original reads out of bounds there).
    #[must_use]
    pub fn from_data(d: &MeshDesc<'_>) -> TriMesh {
        let mut m = TriMesh::default();
        let nt = d.n_tris.max(0);
        let ntu = nt as usize;
        assert!(d.indices.len() >= 3 * ntu, "CTriMesh: too few indices");
        m.n_tris = nt;

        // nVertices = 1 + max of the 16-bit index values.
        let mut nv = 0i32;
        for t in 0..ntu {
            let mut p = d.indices.low16(3 * t);
            for k in 1..3 {
                let q = d.indices.low16(3 * t + k);
                if p < q {
                    p = q;
                }
            }
            nv = nv.max(i32::from(p));
        }
        nv += 1;
        m.n_verts = nv;
        let nvu = nv as usize;
        let mut fl = d.flags & flags::STORED;
        m.vertices = (0..nvu)
            .map(|i| d.vertices.get(i).copied().unwrap_or(Vec3::ZERO))
            .collect();
        m.indices = match d.indices {
            MeshIndices::I32(a) if d.flags & flags::SHARED_INDICES != 0 => a[..3 * ntu].to_vec(),
            _ => {
                fl &= !flags::SHARED_INDICES;
                (0..3 * ntu)
                    .map(|k| i32::from(d.indices.low16(k)))
                    .collect()
            }
        };
        m.max_vertex_valency = if d.flags & flags::NO_VALENCY != 0 {
            8
        } else {
            max_valency_u8(&m.indices, nv)
        };
        m.mats = d.mats.map(|a| a[..ntu].to_vec());
        m.foreign_idx = match (d.foreign_idx, d.flags & flags::SHARED_FOREIGN_IDX != 0) {
            (Some(f), _) => Some(f[..ntu].to_vec()),
            (None, true) => {
                fl &= !flags::SHARED_FOREIGN_IDX;
                Some((0..nt).collect())
            }
            (None, false) => None,
        };
        m.flags = fl;

        // Vertex welding: weld[i] = the vertex i is merged into (word 0 of the original's
        // 16-byte records; words 1..3 are the vertex's rank on each axis).
        let mut weld = vec![0i32; nvu];
        if d.flags & flags::NO_VTX_MERGE != 0 {
            for (i, w) in weld.iter_mut().enumerate() {
                *w = i as i32;
            }
        } else {
            m.weld_vertices(&mut weld);
            if d.flags & flags::KEEP_DEGENERATE == 0 {
                m.remove_degenerate(&weld);
            }
        }
        let n3 = 3 * m.n_tris as usize;
        m.indices.truncate(n3);
        for k in 0..n3 {
            m.indices[k] = weld[m.indices[k] as usize];
        }
        let ntf = m.n_tris as usize;
        if let Some(a) = m.mats.as_mut() {
            a.truncate(ntf);
        }
        if let Some(a) = m.foreign_idx.as_mut() {
            a.truncate(ntf);
        }
        if d.flags & (flags::FULL_VTX_MAP | flags::VTX_MAP) != 0 {
            let keep = d.flags & flags::FULL_VTX_MAP != 0 || {
                let mut acc = 0i32;
                for (i, &w) in weld.iter().enumerate() {
                    let x = w.wrapping_sub(i as i32);
                    acc |= x.wrapping_mul(x);
                }
                acc != 0
            };
            if keep {
                m.vtx_map = Some(weld);
            }
        }

        // BV tree (principal axes, candidates, cheapest), then the tree bit in the flags.
        let (tree, out) = build_mesh_tree(
            &mut BuildMesh {
                vertices: &m.vertices,
                indices: &mut m.indices,
                mats: m.mats.as_deref_mut(),
                foreign_idx: m.foreign_idx.as_deref_mut(),
                aux: None,
            },
            d.flags,
            d.min_tris,
            d.max_tris,
            d.obb_factor,
        );
        m.tree = tree;
        m.flags = (m.flags & 0xFFFB_FF1F) | (out & 0xE0);

        m.normals = (0..ntf).map(|t| m.compute_normal(t)).collect();
        m.build_topology();
        if d.flags & flags::NO_VALENCY != 0 {
            m.multi_island = 1;
            m.is_convex = 0;
        } else {
            m.multi_island = i32::from(m.build_islands() > 1);
            m.is_convex = m.is_convex(f32::from_bits(0x3CA3_D70A));
            if m.is_convex != 0 {
                m.set_tree_convex();
            }
            if let Some(t) = &m.tree {
                m.min_vtx_dist = min_vtx_dist_of(t);
            }
        }
        m
    }

    /// The normal of triangle `t` from the current vertices and indices (sub_350FDBE0 without
    /// the store).
    #[must_use]
    pub fn compute_normal(&self, t: usize) -> Vec3 {
        let v = |k: usize| self.vertices[self.indices[3 * t + k] as usize];
        tri_normal(v(0), v(1), v(2))
    }

    /// sub_350FDBE0: recomputes the stored normal of triangle `t`.
    pub fn update_normal(&mut self, t: usize) {
        let n = self.compute_normal(t);
        self.normals[t] = n;
    }

    /// Tree slot 5 `SetGeomConvex` on the mesh's tree.
    pub(crate) fn set_tree_convex(&mut self) {
        use crate::geometry::bvtree::BvTree;
        match self.tree.as_mut() {
            Some(MeshTree::Obb(t)) => t.set_geom_convex(),
            Some(MeshTree::Aabb(t)) => t.set_geom_convex(),
            Some(MeshTree::SingleBox(t)) => t.set_geom_convex(),
            None => {}
        }
    }

    /// The constructor's vertex welding loop (see the module docs). `weld` receives, for each
    /// vertex, the lowest vertex with identical coordinates (−1 stays for vertices whose
    /// coordinates are NaN).
    fn weld_vertices(&self, weld: &mut [i32]) {
        let nv = self.n_verts;
        let nvu = nv as usize;
        let v = &self.vertices;
        let mut rank = vec![[0i32; 3]; nvu];
        let mut sorted: [Vec<i32>; 3] = std::array::from_fn(|_| (0..nv).collect());
        for (axis, s) in sorted.iter_mut().enumerate() {
            sort_by_coord(s, v, axis, 0, nv - 1);
            for (pos, &i) in s.iter().enumerate() {
                rank[i as usize][axis] = pos as i32;
            }
        }
        for w in weld.iter_mut() {
            *w = -1;
        }
        let mut group: [Vec<i32>; 3] = std::array::from_fn(|_| vec![0i32; nvu + 1]);
        let mut cnt = [0i32; 3];
        let mut scratch = vec![0i32; nvu + 1];
        for i in 0..nvu {
            if weld[i] >= 0 {
                continue;
            }
            let p = v[i].to_array();
            for axis in 0..3 {
                let s = &sorted[axis];
                let x = p[axis];
                let coord = |k: i32| v[s[k as usize] as usize].to_array()[axis];
                let mut k = rank[i][axis];
                while k > 0 && ueq(x, coord(k - 1)) {
                    k -= 1;
                }
                let mut n = 0i32;
                while k < nv && ueq(x, coord(k)) {
                    group[axis][n as usize] = s[k as usize];
                    n += 1;
                    k += 1;
                }
                sort_ints(&mut group[axis], 0, n - 1);
                cnt[axis] = n;
            }
            let n1 = intersect_sorted(&group[0], cnt[0], &group[1], cnt[1], &mut scratch);
            let [g0, _, g2] = &mut group;
            let n2 = intersect_sorted(&scratch, n1, g2, cnt[2], g0);
            for &j in &g0[..n2.max(0) as usize] {
                weld[j as usize] = i as i32;
            }
        }
    }

    /// The constructor's degenerate-triangle removal (two equal welded ids; swap with the last
    /// triangle, re-test the swapped-in one).
    fn remove_degenerate(&mut self, weld: &[i32]) {
        let mut n = self.n_tris as usize;
        let mut i = 0usize;
        while i < n {
            let id = |k: usize| weld[self.indices[3 * i + k] as usize];
            let (a, b) = (id(0), id(1));
            let deg = same_id(b, a) || {
                let c = id(2);
                same_id(b, c) || same_id(a, c)
            };
            if deg {
                n -= 1;
                for k in 0..3 {
                    self.indices.swap(3 * n + k, 3 * i + k);
                }
                if let Some(m) = self.mats.as_mut() {
                    m.swap(n, i);
                }
                if let Some(f) = self.foreign_idx.as_mut() {
                    f.swap(n, i);
                }
            } else {
                i += 1;
            }
        }
        self.n_tris = n as i32;
    }
}

/// `m_minVtxDist` from the tree's box: `0.0002 · max(max(sx, sy), sz)` with the
/// `(|a−b| + (a+b))·½` idiom (constant 0x3951B717).
pub(crate) fn min_vtx_dist_of(t: &MeshTree) -> f32 {
    let s = t.as_dyn().bbox().size;
    let m = ((s.x - s.y).abs() + (s.x + s.y)) * 0.5;
    (((m - s.z).abs() + (s.z + m)) * 0.5) * f32::from_bits(0x3951_B717)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn int_sort_and_intersect() {
        let mut a = vec![5, 3, 9, 1, 3, 7];
        sort_ints(&mut a, 0, 5);
        assert_eq!(a, [1, 3, 3, 5, 7, 9]);
        let mut out = [0i32; 8];
        let n = intersect_sorted(&[1, 3, 5, 7], 4, &[2, 3, 7, 8], 4, &mut out);
        assert_eq!(&out[..n as usize], &[3, 7]);
        assert_eq!(intersect_sorted(&[1], 0, &[1], 1, &mut out), 0);
    }

    #[test]
    fn welds_equal_vertices() {
        let v = [
            Vec3::new(0.0, 0.0, 0.0),
            Vec3::new(1.0, 0.0, 0.0),
            Vec3::new(0.0, 1.0, 0.0),
            Vec3::new(-0.0, 0.0, 0.0),
            Vec3::new(1.0, 1.0, 0.0),
        ];
        let idx: [u16; 6] = [0, 1, 2, 3, 4, 2];
        let m = TriMesh::from_data(&MeshDesc {
            vertices: &v,
            indices: MeshIndices::U16(&idx),
            mats: None,
            foreign_idx: None,
            n_tris: 2,
            flags: 0x8040,
            min_tris: 2,
            max_tris: 4,
            obb_factor: 1.0,
        });
        assert_eq!(m.vtx_map.as_deref(), Some(&[0, 1, 2, 0, 4][..]));
        assert_eq!(m.indices, [0, 1, 2, 0, 4, 2]);
        assert_eq!(m.n_tris, 2);
    }
}
