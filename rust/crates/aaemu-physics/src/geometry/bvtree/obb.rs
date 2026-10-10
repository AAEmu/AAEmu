//! `COBBTree` (vtable 0x351F3008, sizeof 72): a tree of oriented boxes, each fitted to its
//! triangles' surface (principal axes, sub_35195400).
//!
//! Layout: `+8` geometry, `+16` nodes (72 bytes each), `+24` nNodes, `+28` nNodesAlloc (256,
//! grows by 256), `+32` int[nTris] triangle → leaf, `+40` largest leaf, `+44` min tris per side
//! (2), `+48` max tris per leaf (4), `+52` maxSkipDim, `+56/+64` build-only scratch (vertex
//! bitmap, unique-vertex buffer).
//!
//! Node 0 is the root, node 1 is unused, children are allocated in pairs (`i ^ 1` = sibling).

// `!(a >= b)` keeps the original's NaN behaviour (a NaN comparison takes the other branch).
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::fit::principal_axes;
use super::hull::convex_hull;
use super::idiom::{idxmax3, DEC_MOD3, INC_MOD3};
use super::stream::{MemReader, MemWriter, StreamError};
use super::swept::{sweep_box, transform_box};
use super::{
    alloc_used_map_and_list, box_from_words, box_to_words, cleanup_used_map, rec, BuildMesh,
    BvArena, BvHandle, BvTestState, BvTree, NodeContents, UsedNodeMap,
};
use crate::geometry::primitives::BoxPrim;
use crate::ids::BvTreeType;
use crate::math::{max_idiom, min_idiom, Mat33, Vec3};

/// A 72-byte OBB node.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ObbNode {
    /// `+0` rows = unit axes.
    pub axes: Mat33,
    /// `+36`.
    pub center: Vec3,
    /// `+48` half-sizes along the axes.
    pub size: Vec3,
    /// `+60` (−1 for the root).
    pub i_parent: i32,
    /// `+64` first child, or first triangle of a leaf.
    pub ichild: i32,
    /// `+68` triangle count, 0 = internal.
    pub ntris: i32,
}

impl ObbNode {
    /// The 72 raw bytes.
    #[must_use]
    pub fn to_bytes(&self) -> [u8; 72] {
        let mut w = MemWriter::new();
        w.mat33(&self.axes);
        w.vec3(self.center);
        w.vec3(self.size);
        w.i32(self.i_parent);
        w.i32(self.ichild);
        w.i32(self.ntris);
        let mut b = [0u8; 72];
        b.copy_from_slice(&w.buf);
        b
    }

    fn read(r: &mut MemReader<'_>) -> Result<Self, StreamError> {
        Ok(Self {
            axes: r.mat33()?,
            center: r.vec3()?,
            size: r.vec3()?,
            i_parent: r.i32()?,
            ichild: r.i32()?,
            ntris: r.i32()?,
        })
    }

    fn as_box(&self) -> BoxPrim {
        BoxPrim {
            basis: self.axes,
            oriented: 1,
            center: self.center,
            size: self.size,
        }
    }
}

/// `COBBTree`.
#[derive(Clone, Debug, PartialEq)]
pub struct ObbTree {
    /// `+16` (`len` = nNodes).
    pub nodes: Vec<ObbNode>,
    /// `+28`.
    pub n_nodes_alloc: i32,
    /// `+32` triangle → leaf.
    pub tri_leaf: Vec<i32>,
    /// `+40`.
    pub max_leaf_tris: i32,
    /// `+44`.
    pub min_tris_per_side: i32,
    /// `+48`.
    pub max_tris_per_leaf: i32,
    /// `+52`: relative before the build, absolute after.
    pub max_skip_dim: f32,
}

impl Default for ObbTree {
    /// The constructor's defaults (min 2 per side, max 4 per leaf, maxSkipDim 0).
    fn default() -> Self {
        Self::new(2, 4, 0.0)
    }
}

/// Per-node build scratch (`+56` vertex bitmap, `+64` unique-vertex buffer).
struct Scratch {
    vbits: Vec<u32>,
    pts: Vec<Vec3>,
}

impl ObbTree {
    /// sub_35191E80: `min_tris` per side, `max_tris` per leaf, relative maxSkipDim.
    #[must_use]
    pub fn new(min_tris: i32, max_tris: i32, max_skip_rel: f32) -> Self {
        Self {
            nodes: Vec::new(),
            n_nodes_alloc: 0,
            tri_leaf: Vec::new(),
            max_leaf_tris: 0,
            min_tris_per_side: min_tris,
            max_tris_per_leaf: max_tris,
            max_skip_dim: max_skip_rel,
        }
    }

    /// Slot 4 `Build` (0x35195210): builds over `mesh` (permuting its triangles) and returns
    /// `8·Σ leaf volumes` (returned in xmm0; Hex-Rays shows the function as `void`).
    pub fn build(&mut self, mesh: &mut BuildMesh<'_>) -> f32 {
        let nv = mesh.vertices.len() as i32;
        let n = mesh.n_tris();
        self.n_nodes_alloc = 256;
        self.nodes = vec![ObbNode::default(); 2];
        self.nodes[0].i_parent = -1;
        self.nodes[1].i_parent = -1;
        self.tri_leaf = vec![0; n.max(0) as usize];
        self.max_leaf_tris = 0;
        let mut sc = Scratch {
            vbits: vec![0; ((((nv - 1) >> 5) + 1).max(0)) as usize],
            pts: Vec::with_capacity(nv.max(0) as usize),
        };
        let cost = self.build_node(mesh, &mut sc, 0, 0, n, 0);
        if self.n_nodes_alloc > self.nodes.len() as i32 {
            self.n_nodes_alloc = self.nodes.len() as i32;
        }
        let s = self.nodes[0].size;
        self.max_skip_dim *= max_idiom(max_idiom(s.x, s.y), s.z);
        cost * 8.0
    }

    fn make_leaf(&mut self, i_node: i32, i_start: i32, n: i32) -> f32 {
        let nd = &mut self.nodes[i_node as usize];
        nd.ichild = i_start;
        nd.ntris = n;
        let d = self.max_leaf_tris.wrapping_sub(n);
        self.max_leaf_tris = self.max_leaf_tris.wrapping_sub(d & (d >> 31));
        for t in i_start..i_start + n {
            self.tri_leaf[t as usize] = i_node;
        }
        let s = self.nodes[i_node as usize].size;
        (s.y * s.x) * s.z
    }

    /// sub_35192660.
    #[allow(clippy::too_many_lines)]
    fn build_node(
        &mut self,
        mesh: &mut BuildMesh<'_>,
        sc: &mut Scratch,
        i_node: i32,
        i_start: i32,
        n: i32,
        depth: i32,
    ) -> f32 {
        // Unique vertices of the node's triangles, in vertex-id order.
        let (mut vmin, mut vmax) = (0x4000_0000i32, -1i32);
        for k in 3 * i_start as usize..3 * (i_start + n) as usize {
            let v = mesh.indices[k];
            sc.vbits[(v >> 5) as usize] |= 1 << (v & 31);
            vmin = vmin.min(v);
            vmax = vmax.max(v);
        }
        sc.pts.clear();
        for v in vmin..=vmax {
            let w = &mut sc.vbits[(v >> 5) as usize];
            if *w & (1 << (v & 31)) != 0 {
                sc.pts.push(mesh.vertices[v as usize]);
                *w &= !(1 << (v & 31));
            }
        }
        let npts = sc.pts.len();

        // Axes: principal axes of the hull (depth ≤ 4, > 100 triangles) or of the triangles.
        // BUG kept: the hull's indices refer to the unique-vertex buffer but the fit reads them
        // from the mesh's vertex array.
        let hull = if depth <= 4 && n > 100 {
            convex_hull(&sc.pts)
        } else {
            None
        };
        let fit = match &hull {
            Some(tris) if !tris.is_empty() => {
                let idx: Vec<i32> = tris.iter().flatten().copied().collect();
                Some(principal_axes(mesh.vertices, &idx, tris.len() as i32))
            }
            _ if n != 0 => {
                let s = 3 * i_start as usize;
                Some(principal_axes(
                    mesh.vertices,
                    &mesh.indices[s..s + 3 * n as usize],
                    n,
                ))
            }
            _ => None,
        };
        let mut ax = match fit {
            Some(f) => f.axes,
            None => [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]],
        };

        // Extents of the unique vertices along the axes (each projection rounded to float).
        let (mut lo, mut hi) = ([1.7e308f64; 3], [-1.7e308f64; 3]);
        for p in &sc.pts {
            let (x, y, z) = (f64::from(p.x), f64::from(p.y), f64::from(p.z));
            for k in 0..3 {
                let q = f64::from(((x * ax[k][0] + y * ax[k][1]) + z * ax[k][2]) as f32);
                if lo[k] >= q {
                    lo[k] = q;
                }
                if hi[k] <= q {
                    hi[k] = q;
                }
            }
        }
        if i_node == 0 {
            // The root may fall back to the axis-aligned box if that is not larger.
            let p0 = sc.pts.first().copied().unwrap_or(Vec3::ZERO);
            let (mut amn, mut amx) = (p0, p0);
            for p in sc.pts.iter().skip(1) {
                for k in 0..3 {
                    amn[k] = min_idiom(amn[k], p[k]);
                    amx[k] = max_idiom(amx[k], p[k]);
                }
            }
            let avol = ((amx.x - amn.x) * (amx.y - amn.y)) * (amx.z - amn.z);
            let ovol = ((hi[0] - lo[0]) * (hi[1] - lo[1])) * (hi[2] - lo[2]);
            if !(f64::from(avol) >= ovol) {
                ax = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]];
                lo = [f64::from(amn.x), f64::from(amn.y), f64::from(amn.z)];
                hi = [f64::from(amx.x), f64::from(amx.y), f64::from(amx.z)];
            }
        }
        let _ = npts;

        let c = [
            (hi[0] + lo[0]) * 0.5,
            (hi[1] + lo[1]) * 0.5,
            (hi[2] + lo[2]) * 0.5,
        ];
        // The size floor (0.001 · max half-size of the unclamped box) also sets the split
        // threshold below.
        let floor;
        {
            let nd = &mut self.nodes[i_node as usize];
            for (r, row) in ax.iter().enumerate() {
                for (k, v) in row.iter().enumerate() {
                    nd.axes.m[r][k] = *v as f32;
                }
            }
            nd.size = Vec3::new(
                ((hi[0] - lo[0]) * 0.5) as f32,
                ((hi[1] - lo[1]) * 0.5) as f32,
                ((hi[2] - lo[2]) * 0.5) as f32,
            );
            nd.center = Vec3::new(
                ((c[1] * ax[1][0] + c[0] * ax[0][0]) + c[2] * ax[2][0]) as f32,
                ((c[0] * ax[0][1] + c[1] * ax[1][1]) + c[2] * ax[2][1]) as f32,
                ((c[0] * ax[0][2] + c[1] * ax[1][2]) + c[2] * ax[2][2]) as f32,
            );
            let s = nd.size;
            let m = max_idiom(s.x, s.y);
            floor = (((m - s.z).abs() + (s.z + m)) * 0.5) * 0.001;
            nd.size.x = ((floor - s.x).abs() + (s.x + floor)) * 0.5;
            nd.size.y = ((floor - nd.size.y).abs() + (nd.size.y + floor)) * 0.5;
            nd.size.z = ((floor - nd.size.z).abs() + (nd.size.z + floor)) * 0.5;
            if n <= self.max_tris_per_leaf {
                return self.make_leaf(i_node, i_start, n);
            }
        }
        let nd = self.nodes[i_node as usize];
        let s = nd.size;
        let thr = floor * 10.0;
        let min_t = self.min_tris_per_side;
        let mut crit = [0usize; 3];
        let mut score = [0f32; 3];
        let proj = |p: Vec3, a: Vec3, o: f32| ((a.y * p.y + a.x * p.x) + a.z * p.z) - o;
        for k in 0..3usize {
            let sk = s[k];
            if !(sk >= thr) {
                score[k] = f32::from_bits(0xD015_02F9); // −1e10
                continue;
            }
            let a = nd.axes.row(k);
            let c0 = (nd.center.x * a.x + nd.center.y * a.y) + nd.center.z * a.z;
            let mut cnt = [0i32; 3];
            // (left max, right min) for the max / min / centroid criteria.
            let mut b = [[-sk, sk]; 3];
            for t in i_start as usize..(i_start + n) as usize {
                let (v0, v1, v2) = (mesh.vtx(t, 0), mesh.vtx(t, 1), mesh.vtx(t, 2));
                let sum = Vec3::new(
                    (v0.x + v1.x) + v2.x,
                    (v0.y + v1.y) + v2.y,
                    (v0.z + v1.z) + v2.z,
                );
                let p0 = proj(v0, a, c0);
                let p1 = proj(v1, a, c0);
                let p2 = proj(v2, a, c0);
                let s01 = p1 + p0;
                let d01 = (p0 - p1).abs();
                let m = (s01 - d01) * 0.5;
                let tmin = ((m + p2) - (m - p2).abs()) * 0.5;
                let mx = (d01 + s01) * 0.5;
                let tmax = ((mx - p2).abs() + (mx + p2)) * 0.5;
                let cen = (((sum.x * a.x + sum.y * a.y) + sum.z * a.z) * 0.333_333_34) - c0;
                let pair = [tmin, tmax];
                for (j, t) in [tmax, tmin, cen].into_iter().enumerate() {
                    let right = (!t.to_bits() >> 31) as usize;
                    cnt[j] += right as i32;
                    let v = pair[right ^ 1];
                    let cur = b[j][right];
                    let sign = (2 * (right ^ 1) as i32 - 1) as f32;
                    b[j][right] = ((cur - v).abs() * sign + (cur + v)) * 0.5;
                }
            }
            let mut gap = [0f32; 3];
            for j in 0..3 {
                let bad = ((cnt[j].wrapping_sub(min_t)) as u32
                    | (n.wrapping_sub(min_t).wrapping_sub(cnt[j])) as u32)
                    >> 31;
                gap[j] = (b[j][1] - b[j][0]) - bad as f32 * sk;
            }
            crit[k] = idxmax3(gap);
            score[k] = (s[DEC_MOD3[k]] * gap[crit[k]]) * s[INC_MOD3[k]];
        }
        let axis = idxmax3(score);
        let a = nd.axes.row(axis);
        let c0 = (a.x * nd.center.x + a.y * nd.center.y) + a.z * nd.center.z;
        let cr = crit[axis];
        let mut pos = i_start as usize;
        for t in i_start as usize..(i_start + n) as usize {
            let p0 = proj(mesh.vtx(t, 0), a, c0);
            let p1 = proj(mesh.vtx(t, 1), a, c0);
            let p2 = proj(mesh.vtx(t, 2), a, c0);
            let v = match cr {
                0 => {
                    let d = (p0 - p1).abs();
                    let m = (d + (p1 + p0)) * 0.5;
                    ((m - p2).abs() + (m + p2)) * 0.5
                }
                1 => {
                    let m = ((p1 + p0) - (p0 - p1).abs()) * 0.5;
                    ((m + p2) - (m - p2).abs()) * 0.5
                }
                _ => (p1 + p0) + p2,
            };
            if v.to_bits() >> 31 != 0 {
                mesh.swap_tris(t, pos);
                pos += 1;
            }
        }
        let left = pos as i32 - i_start;
        if left < min_t || left > n - min_t {
            return self.make_leaf(i_node, i_start, n);
        }
        let nn = self.nodes.len() as i32;
        if nn + 2 > self.n_nodes_alloc {
            self.n_nodes_alloc += 256;
        }
        self.nodes[i_node as usize].ichild = nn;
        for _ in 0..2 {
            self.nodes.push(ObbNode {
                i_parent: i_node,
                ..ObbNode::default()
            });
        }
        let right = self.build_node(mesh, sc, nn + 1, left + i_start, n - left, depth + 1);
        let l = self.build_node(mesh, sc, nn, i_start, left, depth + 1);
        l + right
    }

    /// Slot 22 `Load` (0x35192510): `nNodes, nodes[72·n], +40, +44, +48, +52`; the triangle →
    /// leaf map is rebuilt for `n_tris` triangles.
    ///
    /// # Errors
    /// Truncated stream.
    pub fn load(r: &mut MemReader<'_>, n_tris: i32) -> Result<Self, StreamError> {
        let n = r.i32()?;
        let mut nodes = Vec::with_capacity(n.max(0) as usize);
        for _ in 0..n.max(0) {
            nodes.push(ObbNode::read(r)?);
        }
        let mut tri_leaf = vec![0i32; n_tris.max(0) as usize];
        for (i, nd) in nodes.iter().enumerate() {
            for k in 0..nd.ntris {
                if let Some(t) = tri_leaf.get_mut(nd.ichild.wrapping_add(k) as usize) {
                    *t = i as i32;
                }
            }
        }
        Ok(Self {
            n_nodes_alloc: n,
            nodes,
            tri_leaf,
            max_leaf_tris: r.i32()?,
            min_tris_per_side: r.i32()?,
            max_tris_per_leaf: r.i32()?,
            max_skip_dim: r.f32()?,
        })
    }

    /// `min(i_node, nNodes − 1)` (slots 9–11 clamp the node index).
    fn clamp(&self, i_node: i32) -> i32 {
        let m = self.nodes.len() as i32 - 1;
        let d = i_node.wrapping_sub(m);
        m.wrapping_add(d & (d >> 31))
    }

    fn write_rec(arena: &mut BvArena, node: i32, b: &BoxPrim) -> usize {
        let i = arena.alloc_bv();
        let w = &mut arena.bv_pool[i];
        w[rec::TYPE] = 0;
        w[rec::NODE] = node as u32;
        box_to_words(w, rec::BOX, b);
        i
    }
}

impl BvTree for ObbTree {
    /// `r0` → 0.
    fn tree_type(&self) -> BvTreeType {
        BvTreeType::Obb
    }

    /// 0x35194210: the root node's box (`bOriented = 1`).
    fn bbox(&self) -> BoxPrim {
        self.nodes[0].as_box()
    }

    /// 0x351BDCB0 (`+40`).
    fn max_prims_in_node(&self) -> i32 {
        self.max_leaf_tris
    }

    /// 0x35191EA0: `maxSkipDim = ((root.size.y + root.size.x) + root.size.z)·10`.
    fn set_geom_convex(&mut self) {
        let s = self.nodes[0].size;
        self.max_skip_dim = ((s.y + s.x) + s.z) * 10.0;
    }

    /// 0x351921E0: a used-node bitmap and list when `maxSkipDim > 0`.
    fn prepare_for_intersection_test(&self, gtest: &mut BvTestState, arena: &mut BvArena) -> i32 {
        if self.max_skip_dim > 0.0 {
            alloc_used_map_and_list(gtest, arena, self.nodes.len() as i32);
        } else {
            gtest.used_map = UsedNodeMap::None;
            gtest.used_list = None;
            gtest.n_used_cap = 0;
        }
        gtest.i_last_used = -1;
        1
    }

    /// 0x35191FA0.
    fn cleanup_after_intersection_test(&self, gtest: &mut BvTestState, arena: &mut BvArena) {
        cleanup_used_map(gtest, arena, self.nodes.len() as i32);
    }

    /// 0x351945D0.
    fn get_node_bv_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        i_node: i32,
    ) -> BvHandle {
        let i = self.clamp(i_node);
        let nd = &self.nodes[i as usize];
        let b = transform_box(&nd.axes, nd.center, nd.size, r, offset, scale);
        BvHandle::Pool(Self::write_rec(arena, i, &b) as i32)
    }

    /// 0x35194420: the node box swept (a 72-byte record in the BV pool).
    fn get_node_bv_swept(
        &self,
        arena: &mut BvArena,
        dir: Vec3,
        len: f32,
        i_node: i32,
    ) -> Option<BvHandle> {
        let i = self.clamp(i_node);
        let b = sweep_box(&self.nodes[i as usize].as_box(), dir, len);
        Some(BvHandle::Pool(Self::write_rec(arena, i, &b) as i32))
    }

    /// 0x351942D0.
    fn get_node_bv(&self, arena: &mut BvArena, i_node: i32) -> BvHandle {
        let i = self.clamp(i_node);
        BvHandle::Pool(Self::write_rec(arena, i, &self.nodes[i as usize].as_box()) as i32)
    }

    /// 0x35192070: `(ntris == 0)·((size.y·size.x)·size.z)` of the record.
    fn split_priority(&self, arena: &BvArena, bv: BvHandle) -> f32 {
        let w = arena.bv_words(bv);
        let nd = &self.nodes[w[rec::NODE] as usize];
        let s = box_from_words(&w, rec::BOX).size;
        (((nd.ntris - 1) >> 31) & 1) as f32 * ((s.y * s.x) * s.z)
    }

    /// 0x35194F80: the children's own boxes swept; `bOriented = iNode + 1`.
    fn get_node_children_bvs_swept(
        &self,
        arena: &mut BvArena,
        parent: BvHandle,
        dir: Vec3,
        len: f32,
    ) -> Option<(BvHandle, BvHandle)> {
        let ich = self.nodes[arena.bv_node(parent) as usize].ichild;
        let mut out = [0usize; 2];
        for (k, o) in out.iter_mut().enumerate() {
            let node = ich + k as i32;
            let mut b = sweep_box(&self.nodes[node as usize].as_box(), dir, len);
            b.oriented = node + 1;
            *o = Self::write_rec(arena, node, &b);
        }
        Some((BvHandle::Pool(out[0] as i32), BvHandle::Pool(out[1] as i32)))
    }

    /// 0x35194D70: the children's boxes; `bOriented = iNode + 1`.
    fn get_node_children_bvs(
        &self,
        arena: &mut BvArena,
        parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        let ich = self.nodes[arena.bv_node(parent) as usize].ichild;
        let mut out = [0usize; 2];
        for (k, o) in out.iter_mut().enumerate() {
            let node = ich + k as i32;
            let mut b = self.nodes[node as usize].as_box();
            b.oriented = node + 1;
            *o = Self::write_rec(arena, node, &b);
        }
        Some((BvHandle::Pool(out[0] as i32), BvHandle::Pool(out[1] as i32)))
    }

    /// 0x35194890: the children's boxes transformed; `bOriented = iNode + 1`.
    fn get_node_children_bvs_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        let ich = self.nodes[arena.bv_node(parent) as usize].ichild;
        let mut out = [0usize; 2];
        for (k, o) in out.iter_mut().enumerate() {
            let node = ich + k as i32;
            let nd = &self.nodes[node as usize];
            let mut b = transform_box(&nd.axes, nd.center, nd.size, r, offset, scale);
            b.oriented = node + 1;
            *o = Self::write_rec(arena, node, &b);
        }
        Some((BvHandle::Pool(out[0] as i32), BvHandle::Pool(out[1] as i32)))
    }

    /// 0x35191ED0.
    fn release_last_bvs(&self, arena: &mut BvArena) {
        arena.bv_count -= 2;
    }

    /// 0x35191EF0: the OBB tree's swept BVs live in the normal pool.
    fn release_last_swept_bvs(&self, arena: &mut BvArena) {
        arena.bv_count -= 2;
    }

    /// 0x351DB320.
    fn max_skip_dim(&self) -> f32 {
        let s = self.nodes[0].size;
        self.max_skip_dim / max_idiom(max_idiom(s.x, s.y), s.z)
    }

    /// 0x35192310: "OBB trees", 72 + 72·nNodesAlloc + 4·nTris.
    fn memory_size(&self) -> usize {
        72 + 72 * self.n_nodes_alloc.max(0) as usize + 4 * self.tri_leaf.len()
    }

    /// 0x351923A0: `nNodes, nodes, +40, +44, +48, +52`.
    fn save(&self, w: &mut MemWriter) {
        w.i32(self.nodes.len() as i32);
        for nd in &self.nodes {
            w.bytes(&nd.to_bytes());
        }
        w.i32(self.max_leaf_tris);
        w.i32(self.min_tris_per_side);
        w.i32(self.max_tris_per_leaf);
        w.f32(self.max_skip_dim);
    }

    /// 0x35191F10.
    fn get_node_contents(&self, i_node: i32) -> NodeContents {
        let nd = &self.nodes[i_node as usize];
        NodeContents {
            first_prim: nd.ichild,
            n_prims: nd.ntris,
        }
    }

    /// 0x351BDCC0.
    fn get_node_contents_idx(&self, i_node: i32) -> NodeContents {
        self.get_node_contents(i_node)
    }

    /// 0x351920B0: if the triangle's leaf is not yet used and its largest half-size is below
    /// maxSkipDim, mark it and climb: while the sibling (`i ^ 1`) is also marked, mark the
    /// parent. (The root's "sibling" is node 1, which is never marked.)
    fn mark_used_triangle(&self, i_tri: i32, gtest: &mut BvTestState, arena: &mut BvArena) {
        if gtest.used_map == UsedNodeMap::None {
            return;
        }
        let mut v = self.tri_leaf[i_tri as usize];
        if gtest.is_node_used(arena, v) == Some(true) {
            return;
        }
        let s = self.nodes[v as usize].size;
        let m = max_idiom(max_idiom(s.x, s.y), s.z);
        if !(m < self.max_skip_dim) {
            return;
        }
        loop {
            if let Some(w) = gtest.map_word_mut(arena, v) {
                *w |= 1u32 << (v & 31);
            }
            gtest.push_used(arena, v);
            gtest.b_marked = 1;
            let sib = v ^ 1;
            if gtest.is_node_used(arena, sib) != Some(true) {
                break;
            }
            v = self.nodes[sib as usize].i_parent;
            if v < 0 {
                break;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn blob_mesh(n: usize) -> (Vec<Vec3>, Vec<i32>) {
        // n separated, rotated quads.
        let (mut v, mut idx) = (Vec::new(), Vec::new());
        for k in 0..n {
            let a = k as f32 * 0.7;
            let (c, s) = (a.cos(), a.sin());
            let o = Vec3::new(4.0 * k as f32, (k % 3) as f32, (k % 2) as f32);
            let base = v.len() as i32;
            for (x, y) in [(0.0, 0.0), (1.0, 0.0), (1.0, 2.0), (0.0, 2.0)] {
                v.push(Vec3::new(c * x - s * y, s * x + c * y, 0.3 * x) + o);
            }
            idx.extend([base, base + 1, base + 2, base, base + 2, base + 3]);
        }
        (v, idx)
    }

    fn build(v: &[Vec3], idx: &mut [i32]) -> ObbTree {
        let mut t = ObbTree::new(2, 4, 0.1);
        let mut m = BuildMesh {
            vertices: v,
            indices: idx,
            mats: None,
            foreign_idx: None,
            aux: None,
        };
        t.build(&mut m);
        t
    }

    #[test]
    fn build_contains_triangles() {
        let (v, mut idx) = blob_mesh(12);
        let t = build(&v, &mut idx);
        assert!(t.nodes.len() > 3);
        for (i, nd) in t.nodes.iter().enumerate() {
            if i == 1 || nd.ntris == 0 {
                continue;
            }
            for tri in nd.ichild..nd.ichild + nd.ntris {
                assert_eq!(t.tri_leaf[tri as usize], i as i32);
                for k in 0..3 {
                    let p = v[idx[(tri * 3 + k) as usize] as usize] - nd.center;
                    for a in 0..3 {
                        let d = nd.axes.row(a).dot(p);
                        assert!(
                            d.abs() <= nd.size[a] * 1.001 + 1e-4,
                            "node {i} axis {a}: {d} > {}",
                            nd.size[a]
                        );
                    }
                }
            }
        }
        // Parents link back.
        for (i, nd) in t.nodes.iter().enumerate() {
            if i != 1 && nd.ntris == 0 {
                assert_eq!(t.nodes[nd.ichild as usize].i_parent, i as i32);
                assert_eq!(t.nodes[nd.ichild as usize + 1].i_parent, i as i32);
            }
        }
    }

    #[test]
    fn save_load_roundtrip() {
        let (v, mut idx) = blob_mesh(9);
        let t = build(&v, &mut idx);
        let mut w = MemWriter::new();
        t.save(&mut w);
        let b = w.into_bytes();
        let t2 = ObbTree::load(&mut MemReader::new(&b), (idx.len() / 3) as i32).unwrap();
        assert_eq!(t2.nodes, t.nodes);
        assert_eq!(t2.tri_leaf, t.tri_leaf);
        let mut w2 = MemWriter::new();
        t2.save(&mut w2);
        assert_eq!(w2.buf, b);
    }

    #[test]
    fn mark_used_climbs_to_parent() {
        let (v, mut idx) = blob_mesh(8);
        let mut t = build(&v, &mut idx);
        t.set_geom_convex();
        let mut a = BvArena::new();
        let mut g = BvTestState::default();
        t.prepare_for_intersection_test(&mut g, &mut a);
        // Mark every triangle: every node ends up used.
        for tri in 0..(idx.len() / 3) as i32 {
            t.mark_used_triangle(tri, &mut g, &mut a);
        }
        assert_eq!(g.is_node_used(&a, 0), Some(true));
        assert_eq!(g.b_marked, 1);
        t.cleanup_after_intersection_test(&mut g, &mut a);
        assert_eq!(g.is_node_used(&a, 0), Some(false));
    }

    #[test]
    fn children_records() {
        let (v, mut idx) = blob_mesh(6);
        let t = build(&v, &mut idx);
        let mut a = BvArena::new();
        let root = t.get_node_bv(&mut a, 0);
        let (l, r) = t.get_node_children_bvs(&mut a, root).unwrap();
        let ich = t.nodes[0].ichild;
        assert_eq!(a.bv_node(l), ich);
        assert_eq!(a.bv_box(r).oriented, ich + 2);
        assert_eq!(a.bv_box(l).center, t.nodes[ich as usize].center);
        let sw = t.get_node_bv_swept(&mut a, Vec3::Z, 3.0, 0).unwrap();
        assert_eq!(a.bv_box(sw).basis.row(2), Vec3::Z);
        assert_eq!(t.get_node_bv(&mut a, 1_000_000), BvHandle::Pool(4));
        assert_eq!(a.bv_node(BvHandle::Pool(4)), t.nodes.len() as i32 - 1);
    }
}
