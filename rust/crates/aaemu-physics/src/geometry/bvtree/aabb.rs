//! `CAABBTree` (vtable 0x351F30E8, sizeof 128): an AABB tree in a fixed basis (identity, or the
//! mesh's principal axes for build flag 0x40000) whose 12-byte nodes store their bounds
//! quantised to 7 bits relative to the parent box.
//!
//! Layout: `+8` geometry, `+16` nodes, `+24` nNodes, `+28` nNodesAlloc, `+32` root centre
//! (geometry frame), `+44` root half-size, `+56` basis, `+92` short bOriented, `+94` short split
//! axis restriction (−1 off, 0 = compute at build, else `axisA | axisB << 2`), `+96` packed
//! triangle → leaf map, `+104` log2(bits per map entry), `+108` max tris per leaf, `+112` min
//! tris per side, `+116` largest leaf, `+120` maxSkipDim.
//!
//! Nodes 0 (root) and 1 (unused) are allocated first, children always in pairs at
//! `ichild, ichild + 1`, so siblings are `i ^ 1`.

use super::idiom::{idxmax3, idxmin3, sel_max, sel_min, DEC_MOD3, INC_MOD3};
use super::single_box::is_identity_sum;
use super::stream::{MemReader, MemWriter, StreamError};
use super::swept::{sweep_box, transform_box};
use super::{
    alloc_used_map_and_list, box_from_words, box_to_words, cleanup_used_map, rec, vec_from_words,
    vec_to_words, BuildMesh, BvArena, BvHandle, BvTestState, BvTree, NodeContents, UsedNodeMap,
};
use crate::geometry::primitives::BoxPrim;
use crate::ids::BvTreeType;
use crate::math::{max_idiom, min_idiom, Mat33, Vec3};

/// A 12-byte AABB node.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct AabbNode {
    /// `+0` first child (children are `ichild`, `ichild + 1`), or first triangle of a leaf.
    pub ichild: i32,
    /// `+4..+9` minx, maxx, miny, maxy, minz, maxz, each 0..127 (bound = `q·s/64` from the
    /// parent's low corner; the max is `(q+1)·s/64`).
    pub bounds: [u8; 6],
    /// `+10` triangle count, 0 = internal.
    pub ntris: u8,
    /// `+11` bSingleColl. **Never written for internal nodes** by the builder (the original
    /// leaves `operator new` garbage there, which real CGF blobs contain); the port writes 0.
    pub single_coll: u8,
}

impl AabbNode {
    /// The 12 raw bytes.
    #[must_use]
    pub fn to_bytes(&self) -> [u8; 12] {
        let mut b = [0u8; 12];
        b[..4].copy_from_slice(&self.ichild.to_le_bytes());
        b[4..10].copy_from_slice(&self.bounds);
        b[10] = self.ntris;
        b[11] = self.single_coll;
        b
    }

    #[must_use]
    pub fn from_bytes(b: &[u8]) -> Self {
        Self {
            ichild: i32::from_le_bytes([b[0], b[1], b[2], b[3]]),
            bounds: [b[4], b[5], b[6], b[7], b[8], b[9]],
            ntris: b[10],
            single_coll: b[11],
        }
    }

    /// Decodes the node's box relative to its parent's (in the parent box's axes):
    /// `lo = (q·s)·(1/64)`, `hi = ((q+1)·s)·(1/64)`; offset `(hi+lo)·½ − s`, half-size
    /// `(hi−lo)·½` (sub_3513A120 and slots 13–15).
    #[must_use]
    pub fn decode(&self, parent_size: Vec3) -> (Vec3, Vec3) {
        let mut d = Vec3::ZERO;
        let mut s = Vec3::ZERO;
        for k in 0..3 {
            let (lo, hi) = self.lo_hi(k, parent_size[k]);
            d[k] = (hi + lo) * 0.5 - parent_size[k];
            s[k] = (hi - lo) * 0.5;
        }
        (d, s)
    }

    #[inline]
    fn lo_hi(&self, axis: usize, s: f32) -> (f32, f32) {
        let lo = (f32::from(self.bounds[axis * 2]) * s) * 0.015625;
        let hi = ((i32::from(self.bounds[axis * 2 + 1]) + 1) as f32 * s) * 0.015625;
        (lo, hi)
    }
}

/// `CAABBTree`.
#[derive(Clone, Debug, PartialEq)]
pub struct AabbTree {
    /// `+16` (`len` = nNodes, `+24`).
    pub nodes: Vec<AabbNode>,
    /// `+28` (memory statistics only).
    pub n_nodes_alloc: i32,
    /// `+32` root centre, geometry frame.
    pub center: Vec3,
    /// `+44` root half-size, tree frame.
    pub size: Vec3,
    /// `+56` tree basis (rows = tree axes in the geometry frame).
    pub basis: Mat33,
    /// `+92`.
    pub oriented: i16,
    /// `+94` (not serialised; uninitialised after `Load` in the original, unused there).
    pub split_axes: i16,
    /// `+96` packed triangle → leaf map.
    pub tri_map: Vec<u32>,
    /// `+104` log2 of the bits per map entry: 3 (≤ 256 nodes) or 4.
    pub map_shift: i32,
    /// `+108`.
    pub max_tris_per_leaf: i32,
    /// `+112`.
    pub min_tris_per_side: i32,
    /// `+116` largest leaf (slot 3).
    pub max_leaf_tris: i32,
    /// `+120` maxSkipDim: relative before the build, absolute (× root max half-size) after.
    pub max_skip_dim: f32,
    /// The mesh's nTris (`geometry +120`, read through the geometry pointer).
    pub n_tris: i32,
}

/// Per-triangle build record: tree-frame min, max, centroid.
type TriBox = [f32; 9];

impl AabbTree {
    /// sub_35136960 (tree setup in the `CTriMesh` constructor): `min_tris` per side,
    /// `max_tris` per leaf, the relative maxSkipDim (0 / 10 / 0.1 for mesh flags 0x400 / 0x100 /
    /// neither), the basis (`bOriented = !IsIdentity`), and the flat-split flag (mesh flag
    /// 0x4000 → `split_axes = 0`, else −1).
    #[must_use]
    pub fn new(
        min_tris: i32,
        max_tris: i32,
        max_skip_rel: f32,
        basis: Mat33,
        flat_split: bool,
    ) -> Self {
        Self {
            nodes: Vec::new(),
            n_nodes_alloc: 0,
            center: Vec3::ZERO,
            size: Vec3::ZERO,
            basis,
            oriented: i16::from(!is_identity_sum(&basis)),
            split_axes: if flat_split { 0 } else { -1 },
            tri_map: Vec::new(),
            map_shift: 3,
            max_tris_per_leaf: max_tris,
            min_tris_per_side: min_tris,
            max_leaf_tris: 0,
            max_skip_dim: max_skip_rel,
            n_tris: 0,
        }
    }

    fn map_words(n_tris: i32, shift: i32) -> usize {
        (((n_tris - 1) >> (5 - shift)) + 1).max(0) as usize
    }

    fn pack_map(&mut self, leaf_of: impl Fn(i32) -> i32) {
        let sh = self.map_shift;
        let mut map = vec![0u32; Self::map_words(self.n_tris, sh)];
        for t in 0..self.n_tris {
            let w = (t >> (5 - sh)) as usize;
            let s = ((t & ((1 << (5 - sh)) - 1)) << sh) as u32;
            map[w] |= (leaf_of(t) as u32).wrapping_shl(s);
        }
        self.tri_map = map;
    }

    /// The leaf holding triangle `i_tri` (from the packed map).
    #[must_use]
    pub fn leaf_of_tri(&self, i_tri: i32) -> i32 {
        let sh = self.map_shift;
        let w = self.tri_map[(i_tri >> (5 - sh)) as usize] as i32;
        ((1 << (1 << sh)) - 1) & (w >> ((i_tri & ((1 << (5 - sh)) - 1)) << sh))
    }

    /// Slot 4 `Build` (0x35139490): builds the tree over `mesh` (permuting its triangles) and
    /// returns the cost `8·Σ leaf volumes` (`+1e10` if a leaf has more than 63 triangles).
    pub fn build(&mut self, mesh: &mut BuildMesh<'_>) -> f32 {
        let n = mesh.n_tris();
        self.n_tris = n;
        self.n_nodes_alloc = 2 * n + 1;
        self.nodes = vec![AabbNode::default(); 2];
        self.max_leaf_tris = 0;
        let mut leaf_tmp = vec![0i32; n.max(0) as usize];

        let b = self.basis.m;
        let mut tris: Vec<TriBox> = Vec::with_capacity(n.max(0) as usize);
        let (mut mnx, mut mny, mut mnz) = (1_000_000.0f32, 1_000_000.0f32, 1_000_000.0f32);
        let (mut mxx, mut mxy, mut mxz) = (-1_000_000.0f32, -1_000_000.0f32, -1_000_000.0f32);
        for t in 0..n as usize {
            let p = [mesh.vtx(t, 0), mesh.vtx(t, 1), mesh.vtx(t, 2)];
            let q: Vec<Vec3> = p
                .iter()
                .map(|v| {
                    Vec3::new(
                        (b[0][0] * v.x + b[0][1] * v.y) + b[0][2] * v.z,
                        (b[1][0] * v.x + b[1][1] * v.y) + b[1][2] * v.z,
                        (b[2][0] * v.x + b[2][1] * v.y) + b[2][2] * v.z,
                    )
                })
                .collect();
            let mut tb = [0f32; 9];
            for k in 0..3 {
                tb[k] = sel_min(sel_min(q[0][k], q[1][k]), q[2][k]);
                tb[3 + k] = sel_max(sel_max(q[0][k], q[1][k]), q[2][k]);
                tb[6 + k] = ((q[0][k] + q[1][k]) + q[2][k]) * 0.333_333_34;
            }
            mnx = sel_min(mnx, tb[0]);
            mxx = sel_max(mxx, tb[3]);
            mny = sel_min(mny, tb[1]);
            mxy = sel_max(mxy, tb[4]);
            // `if (acc < v) v = acc; else acc = v;` (same as sel_min except for NaN).
            mnz = if mnz < tb[2] { mnz } else { tb[2] };
            mxz = sel_max(mxz, tb[5]);
            tris.push(tb);
        }
        let mut sz = (mxz - mnz) * 0.5;
        let mut sx = (mxx - mnx) * 0.5;
        let mut sy = (mxy - mny) * 0.5;
        let c = Vec3::new((mxx + mnx) * 0.5, (mxy + mny) * 0.5, (mxz + mnz) * 0.5);
        self.center = self.basis.t_mul_vec(c);
        let m = max_idiom(max_idiom(sx, sy), sz);
        let floor = m * 0.001;
        self.max_skip_dim *= m;
        if sz <= m * 0.001 {
            sz = m * 0.001;
        }
        if sy <= floor {
            sy = m * 0.001;
        }
        if sx <= floor {
            sx = m * 0.001;
        }
        self.size = Vec3::new(sx, sy, sz);
        if self.split_axes == 0 {
            let i = idxmin3([sx, sy, sz]);
            let (dec, inc) = (DEC_MOD3[i], INC_MOD3[i]);
            let t = self.size[i] * 10.0;
            self.split_axes = if t >= self.size[dec] || t >= self.size[inc] {
                -1
            } else {
                (dec | (inc << 2)) as i16
            };
        }
        let mut lc = self.basis.mul_vec(self.center);
        let mut ls = self.size;
        let cost = self.build_node(
            mesh,
            &mut leaf_tmp,
            &mut tris,
            0,
            0,
            n as u32,
            &mut lc,
            &mut ls,
            0,
        );
        if self.n_nodes_alloc > self.nodes.len() as i32 {
            self.n_nodes_alloc = self.nodes.len() as i32;
        }
        self.map_shift = i32::from(self.nodes.len() > 256) + 3;
        self.pack_map(|t| leaf_tmp[t as usize]);
        (((63 - self.max_leaf_tris) as u32 >> 31) as f32) * 1.0e10 + cost * 8.0
    }

    /// Quantises one axis (sub_351369E0): `q = round(min(max0(k·rel − ½), 127))` via the
    /// `+12582912` trick, keeping the low byte.
    #[inline]
    fn quant(k: f32, rel: f32) -> i32 {
        let t = k * rel - 0.5;
        let m = (t.abs() + t) * 0.5;
        let c = ((m + 127.0) - (m - 127.0).abs()) * 0.5 + 12_582_912.0;
        (c.to_bits() & 0xFF) as i32
    }

    #[allow(clippy::too_many_arguments)]
    fn make_leaf(
        &mut self,
        leaf_tmp: &mut [i32],
        i_node: i32,
        i_start: i32,
        n: u32,
        size: &Vec3,
    ) -> f32 {
        let nd = &mut self.nodes[i_node as usize];
        nd.ichild = i_start;
        nd.ntris = n as u8;
        let d = self.max_leaf_tris.wrapping_sub(n as i32);
        self.max_leaf_tris = self.max_leaf_tris.wrapping_sub(d & (d >> 31));
        let mx = max_idiom(max_idiom(size.x, size.y), size.z);
        nd.single_coll = ((mx - self.max_skip_dim).to_bits() >> 31) as u8;
        for t in i_start..i_start + n as i32 {
            leaf_tmp[t as usize] = i_node;
        }
        (size.x * size.y) * size.z
    }

    /// sub_351369E0: quantises node `i_node` against its parent box `(center, size)` (both in
    /// the tree frame, updated in place to the node's decoded box), then makes a leaf or splits.
    #[allow(clippy::too_many_arguments)]
    fn build_node(
        &mut self,
        mesh: &mut BuildMesh<'_>,
        leaf_tmp: &mut [i32],
        tris: &mut [TriBox],
        i_node: i32,
        i_start: i32,
        n: u32,
        center: &mut Vec3,
        size: &mut Vec3,
        _depth: i32,
    ) -> f32 {
        let thr = max_idiom(max_idiom(size.x, size.y), size.z) * 0.001;
        let (mut mn, mut mx) = ([3.3e38f32; 3], [-3.3e38f32; 3]);
        for t in &tris[i_start as usize..(i_start + n as i32) as usize] {
            for k in 0..3 {
                mn[k] = sel_min(mn[k], t[k]);
                mx[k] = sel_max(mx[k], t[3 + k]);
            }
        }
        let orig = *size;
        let c0 = *center;
        for k in 0..3 {
            let s = orig[k];
            let lo_rel = mn[k] + (s - c0[k]);
            let hi_rel = mx[k] + (s - c0[k]);
            let nd = &mut self.nodes[i_node as usize];
            if s <= thr {
                nd.bounds[2 * k] = 0;
                nd.bounds[2 * k + 1] = 127;
                continue;
            }
            let kk = 64.0 / s;
            let lo = Self::quant(kk, lo_rel);
            let hi = Self::quant(kk, hi_rel);
            let d = lo - hi;
            let lo = hi + (d & (d >> 31));
            let d = lo - hi;
            let hi = lo - (d & (d >> 31));
            nd.bounds[2 * k] = lo as u8;
            nd.bounds[2 * k + 1] = hi as u8;
            let (l, h) = nd.lo_hi(k, s);
            center[k] += (h + l) * 0.5 - s;
            size[k] = (h - l) * 0.5;
        }
        if n as i32 <= self.max_tris_per_leaf {
            return self.make_leaf(leaf_tmp, i_node, i_start, n, size);
        }

        // Axis candidates: only the larger of the two allowed axes when the split is restricted.
        let mut only = -1i32;
        if self.split_axes >= 0 {
            let a = i32::from(self.split_axes);
            only = a & 3;
            if size[((a >> 2) & 3) as usize] >= size[(a & 3) as usize] {
                only = (a >> 2) & 3;
            }
        }
        let min_t = self.min_tris_per_side;
        let mut crit = [2usize; 3];
        let mut score = [0f32; 3];
        for i in 0..3usize {
            if thr > size[i] || (only >= 0 && i as i32 != only) {
                score[i] = -1.0e10;
                continue;
            }
            let c = center[i];
            let s = size[i];
            let mut cnt = [0i32; 3];
            // (left max, right min) for the max / min / centroid criteria.
            let mut lr = [[-s, s]; 3];
            for t in &tris[i_start as usize..(i_start + n as i32) as usize] {
                let vmin = t[i] - c;
                let vmax = t[3 + i] - c;
                let vcen = t[6 + i] - c;
                for (j, v) in [vmax, vmin, vcen].into_iter().enumerate() {
                    if v < 0.0 {
                        lr[j][0] = max_idiom(lr[j][0], vmax);
                    } else {
                        cnt[j] += 1;
                        lr[j][1] = min_idiom(lr[j][1], vmin);
                    }
                }
            }
            let mut gap = [0f32; 3];
            for j in 0..3 {
                let bad = ((cnt[j].wrapping_sub(min_t) as u32)
                    | (n.wrapping_sub(cnt[j] as u32).wrapping_sub(min_t as u32)))
                    >> 31;
                gap[j] = (lr[j][1] - lr[j][0]) - ((8 * bad) as i32 as f32) * s;
            }
            crit[i] = idxmax3(gap);
            score[i] = (gap[crit[i]] * size[DEC_MOD3[i]]) * size[INC_MOD3[i]];
        }
        let axis = idxmax3(score);
        let cs = crit[axis];
        let col = cs ^ ((cs ^ 2) >> 1);
        let cv = center[axis];
        let mut pos = i_start as usize;
        for k in i_start as usize..(i_start + n as i32) as usize {
            if tris[k][3 * col + axis] - cv < 0.0 {
                tris.swap(k, pos);
                mesh.swap_tris(k, pos);
                pos += 1;
            }
        }
        let left = pos as i32 - i_start;
        if left < min_t || left > (n as i32).wrapping_sub(min_t) {
            return self.make_leaf(leaf_tmp, i_node, i_start, n, size);
        }
        let nn = self.nodes.len() as i32;
        if nn + 2 > self.n_nodes_alloc {
            self.n_nodes_alloc += 32;
        }
        self.nodes[i_node as usize].ichild = nn;
        self.nodes.push(AabbNode::default());
        self.nodes.push(AabbNode::default());
        let (mut cr, mut sr) = (*center, *size);
        let right = self.build_node(
            mesh,
            leaf_tmp,
            tris,
            nn + 1,
            left + i_start,
            n - left as u32,
            &mut cr,
            &mut sr,
            _depth + 1,
        );
        let (mut cl, mut sl) = (*center, *size);
        let l = self.build_node(
            mesh,
            leaf_tmp,
            tris,
            nn,
            i_start,
            left as u32,
            &mut cl,
            &mut sl,
            _depth + 1,
        );
        l + right
    }

    /// Slot 22 `Load` (0x351390A0). Current format: `−1, nNodes, nodes[12·n]`; legacy format
    /// (first dword ≥ 0 = nNodes): three packed dwords per node. Then centre, size, basis
    /// (bOriented = `!IsIdentity`), largest leaf, min tris, max tris, maxSkipDim. `n_tris` is the
    /// mesh's triangle count (for the triangle → leaf map).
    ///
    /// # Errors
    /// Truncated stream.
    pub fn load(r: &mut MemReader<'_>, n_tris: i32) -> Result<Self, StreamError> {
        let first = r.i32()?;
        let n_nodes = if first < 0 { r.i32()? } else { first };
        let mut nodes = vec![AabbNode::default(); n_nodes.max(0) as usize];
        if first == -1 {
            let b = r.bytes(12 * nodes.len())?;
            for (k, nd) in nodes.iter_mut().enumerate() {
                *nd = AabbNode::from_bytes(&b[12 * k..12 * k + 12]);
            }
        } else if first >= 0 {
            let b = r.bytes(12 * nodes.len())?;
            for (k, nd) in nodes.iter_mut().enumerate() {
                let w = |i: usize| {
                    let o = 12 * k + 4 * i;
                    u32::from_le_bytes([b[o], b[o + 1], b[o + 2], b[o + 3]])
                };
                let (w0, w1, w2) = (w(0), w(1), w(2));
                *nd = AabbNode {
                    ichild: ((w1 >> 14) & 0x7FFF) as i32,
                    bounds: [
                        (w0 & 0x7F) as u8,
                        ((w0 >> 7) & 0x7F) as u8,
                        ((w0 >> 14) & 0x7F) as u8,
                        ((w0 >> 21) & 0x7F) as u8,
                        (w1 & 0x7F) as u8,
                        ((w1 >> 7) & 0x7F) as u8,
                    ],
                    ntris: (w2 & 0x3F) as u8,
                    single_coll: u8::from(w2 & 0x40 != 0),
                };
            }
        }
        // first < −1: nothing is read for the nodes (the original leaves them uninitialised).
        let mut t = Self {
            n_nodes_alloc: n_nodes,
            nodes,
            center: Vec3::ZERO,
            size: Vec3::ZERO,
            basis: Mat33::IDENTITY,
            oriented: 0,
            split_axes: 0,
            tri_map: Vec::new(),
            map_shift: i32::from(n_nodes > 256) + 3,
            max_tris_per_leaf: 0,
            min_tris_per_side: 0,
            max_leaf_tris: 0,
            max_skip_dim: 0.0,
            n_tris,
        };
        let leaves: Vec<(i32, i32, i32)> = t
            .nodes
            .iter()
            .enumerate()
            .map(|(i, nd)| (i as i32, nd.ichild, i32::from(nd.ntris)))
            .collect();
        let sh = t.map_shift;
        let mut map = vec![0u32; Self::map_words(n_tris, sh)];
        for (i, first, cnt) in leaves {
            for k in 0..cnt {
                let tt = (first.wrapping_add(k)) as u32;
                let w = (tt >> (5 - sh)) as usize;
                let s = ((tt & ((1 << (5 - sh)) - 1)) << sh) as u32;
                if let Some(m) = map.get_mut(w) {
                    *m |= (i as u32).wrapping_shl(s);
                }
            }
        }
        t.tri_map = map;
        t.center = r.vec3()?;
        t.size = r.vec3()?;
        t.basis = r.mat33()?;
        // Load's own sum (same terms, |m01| first); a NaN sum counts as identity here too.
        t.oriented = i16::from(!is_identity_sum(&t.basis));
        t.max_leaf_tris = r.i32()?;
        t.min_tris_per_side = r.i32()?;
        t.max_tris_per_leaf = r.i32()?;
        t.max_skip_dim = r.f32()?;
        Ok(t)
    }

    /// sub_3513A120: the decoded root node box — centre (geometry frame) and half-size.
    #[must_use]
    pub fn root_box(&self) -> (Vec3, Vec3) {
        let (d, s) = self.nodes[0].decode(self.size);
        (self.basis.t_mul_vec(d) + self.center, s)
    }

    /// A child's decoded box relative to a parent record box: `(center, size)` with the
    /// centre offset rotated by the parent basis when `rotate`.
    fn child_box(
        &self,
        i_child: i32,
        pc: Vec3,
        ps: Vec3,
        pb: &Mat33,
        rotate: bool,
    ) -> (Vec3, Vec3) {
        let (d, s) = self.nodes[i_child as usize].decode(ps);
        let c = if rotate { pb.t_mul_vec(d) + pc } else { d + pc };
        (c, s)
    }
}

impl BvTree for AabbTree {
    /// `r1` → 1.
    fn tree_type(&self) -> BvTreeType {
        BvTreeType::Aabb
    }

    /// 0x351365F0: the root box (basis, bOriented sign-extended, centre, size).
    fn bbox(&self) -> BoxPrim {
        BoxPrim {
            basis: self.basis,
            oriented: i32::from(self.oriented),
            center: self.center,
            size: self.size,
        }
    }

    /// 0x351DB220: the largest leaf.
    fn max_prims_in_node(&self) -> i32 {
        self.max_leaf_tris
    }

    /// 0x35136410: every node becomes "single collide".
    fn set_geom_convex(&mut self) {
        for nd in &mut self.nodes {
            nd.single_coll = 1;
        }
    }

    /// 0x35136780. Allocates a used-node bitmap and list when `maxSkipDim > 0` and the mesh has
    /// more than 16 triangles, but then **always stores a null map** (0x3513689A), so AABB
    /// used-node marking never happens (the pool counters still advance). Bug kept.
    fn prepare_for_intersection_test(&self, gtest: &mut BvTestState, arena: &mut BvArena) -> i32 {
        if self.max_skip_dim <= 0.0 || self.n_tris <= 16 {
            gtest.used_map = UsedNodeMap::None;
            gtest.used_list = None;
            gtest.n_used_cap = 0;
        } else {
            alloc_used_map_and_list(gtest, arena, self.nodes.len() as i32);
        }
        gtest.i_last_used = -1;
        gtest.used_map = UsedNodeMap::None;
        1
    }

    /// 0x35136520.
    fn cleanup_after_intersection_test(&self, gtest: &mut BvTestState, arena: &mut BvArena) {
        cleanup_used_map(gtest, arena, self.nodes.len() as i32);
    }

    /// 0x35139DE0: the root box (any `i_node`) in another frame.
    fn get_node_bv_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        _i_node: i32,
    ) -> BvHandle {
        let (c, s) = self.root_box();
        let b = transform_box(&self.basis, c, s, r, offset, scale);
        let i = arena.alloc_bv();
        let w = &mut arena.bv_pool[i];
        w[rec::TYPE] = 0;
        w[rec::NODE] = 0;
        box_to_words(w, rec::BOX, &b);
        BvHandle::Pool(i as i32)
    }

    /// 0x35137C00: the root in swept-pool slot 0. **The swept counter is reset to 0 and not
    /// advanced**, so the next slot 13 hands out slots 0 and 1 and overwrites this record while
    /// reading it (reproduced by [`AabbTree::get_node_children_bvs_swept`]).
    fn get_node_bv_swept(
        &self,
        arena: &mut BvArena,
        dir: Vec3,
        len: f32,
        _i_node: i32,
    ) -> Option<BvHandle> {
        arena.swept_count = 0;
        let (c, s) = self.root_box();
        let orig = BoxPrim {
            basis: self.basis,
            oriented: i32::from(self.oriented),
            center: c,
            size: s,
        };
        let w = &mut arena.swept_pool[0];
        w[rec::TYPE] = 0;
        w[rec::NODE] = 0;
        box_to_words(w, rec::ORIG, &orig);
        let sw = sweep_box(&orig, dir, len);
        box_to_words(&mut arena.swept_pool[0], rec::BOX, &sw);
        Some(BvHandle::Swept(0))
    }

    /// 0x35137B50: the root box in the tree frame (`i_node` ignored).
    fn get_node_bv(&self, arena: &mut BvArena, _i_node: i32) -> BvHandle {
        let (c, s) = self.root_box();
        let i = arena.alloc_bv();
        let w = &mut arena.bv_pool[i];
        w[rec::TYPE] = 0;
        w[rec::NODE] = 0;
        box_to_words(
            w,
            rec::BOX,
            &BoxPrim {
                basis: self.basis,
                oriented: i32::from(self.oriented),
                center: c,
                size: s,
            },
        );
        BvHandle::Pool(i as i32)
    }

    /// 0x35136660: `(ntris == 0) · ((size.y·size.x)·size.z)` of the record.
    fn split_priority(&self, arena: &BvArena, bv: BvHandle) -> f32 {
        let w = arena.bv_words(bv);
        let nd = &self.nodes[w[rec::NODE] as usize];
        let s = vec_from_words(&w, rec::BOX + 13);
        let internal = ((i32::from(nd.ntris) - 1) >> 31) & 1;
        internal as f32 * ((s.y * s.x) * s.z)
    }

    /// 0x351387F0: children of a swept record, from its unswept box, swept again (136-byte
    /// records, swept pool). Reads the parent while writing the children in the original's
    /// order, so the aliasing with the slot-10 root (see [`AabbTree::get_node_bv_swept`]) has
    /// the original's effect: the second child decodes its y/z extents against the **first
    /// child's** box and its z centre from the first child's centre. The second child also
    /// tests the parent's *swept* `bOriented` (`+44`, always non-zero) instead of the unswept
    /// one (`+108`).
    fn get_node_children_bvs_swept(
        &self,
        arena: &mut BvArena,
        parent: BvHandle,
        dir: Vec3,
        len: f32,
    ) -> Option<(BvHandle, BvHandle)> {
        let BvHandle::Swept(p) = parent else {
            panic!("CAABBTree slot 13 needs a swept-AABB record");
        };
        let p = p as usize;
        let c0 = arena.swept_count as usize;
        let c1 = c0 + 1;
        arena.swept_count += 2;
        let f = |a: &BvArena, k: usize| f32::from_bits(a.swept_pool[p][k]);
        let o = rec::ORIG;

        let i_node = arena.swept_pool[p][rec::NODE] as usize;
        let p_sx = f(arena, o + 13);
        let p_or = arena.swept_pool[p][o + 9];
        let ich = self.nodes[i_node].ichild;
        arena.swept_pool[c0][o + 9] = p_or;
        arena.swept_pool[c1][o + 9] = p_or;
        arena.swept_pool[c0][rec::NODE] = ich as u32;
        arena.swept_pool[c1][rec::NODE] = (ich + 1) as u32;
        arena.swept_pool[c1][rec::TYPE] = 0;
        arena.swept_pool[c0][rec::TYPE] = 0;
        for k in 0..9 {
            arena.swept_pool[c0][o + k] = arena.swept_pool[p][o + k];
        }
        for k in 0..9 {
            arena.swept_pool[c1][o + k] = arena.swept_pool[p][o + k];
        }
        let n0 = self.nodes[ich as usize];
        let p_sy = f(arena, o + 14);
        let p_sz = f(arena, o + 15);
        let p_size = Vec3::new(f(arena, o + 13), f(arena, o + 14), f(arena, o + 15));
        let (lx0, hx0) = n0.lo_hi(0, p_sx);
        let (ly0, hy0) = n0.lo_hi(1, p_sy);
        let (lz0, hz0) = n0.lo_hi(2, p_sz);
        let d0 = Vec3::new(
            (hx0 + lx0) * 0.5 - p_size.x,
            (hy0 + ly0) * 0.5 - p_size.y,
            (hz0 + lz0) * 0.5 - p_size.z,
        );
        let pcx = f(arena, o + 10);
        let pcy = f(arena, o + 11);
        let pcz = f(arena, o + 12);
        let pb = box_from_words(&arena.swept_pool[p], o).basis;
        let cen0 = if p_or != 0 {
            pb.t_mul_vec(d0) + Vec3::new(pcx, pcy, pcz)
        } else {
            d0 + Vec3::new(pcx, pcy, pcz)
        };
        vec_to_words(&mut arena.swept_pool[c0], o + 10, cen0);
        // Child 1's y scale is read here, after child 0's centre but before its size.
        let p_sy1 = f(arena, o + 14);
        let s0 = Vec3::new((hx0 - lx0) * 0.5, (hy0 - ly0) * 0.5, (hz0 - lz0) * 0.5);
        vec_to_words(&mut arena.swept_pool[c0], o + 13, s0);

        let n1 = self.nodes[(ich + 1) as usize];
        let (lx1, hx1) = n1.lo_hi(0, p_sx);
        let (ly1, hy1) = n1.lo_hi(1, p_sy1);
        let swept_or = arena.swept_pool[p][rec::BOX + 9];
        let q_size = Vec3::new(f(arena, o + 13), f(arena, o + 14), f(arena, o + 15));
        let (lz1, hz1) = n1.lo_hi(2, q_size.z);
        let d1 = Vec3::new(
            (hx1 + lx1) * 0.5 - q_size.x,
            (hy1 + ly1) * 0.5 - q_size.y,
            (hz1 + lz1) * 0.5 - q_size.z,
        );
        if swept_or != 0 {
            let b = pb.m;
            let x = ((d1.y * b[1][0] + d1.x * b[0][0]) + d1.z * b[2][0]) + pcx;
            arena.swept_pool[c1][o + 10] = x.to_bits();
            let z = (d1.y * b[1][2] + d1.x * b[0][2]) + d1.z * b[2][2];
            let y = ((d1.y * b[1][1] + d1.x * b[0][1]) + d1.z * b[2][1]) + pcy;
            let z = z + f(arena, o + 12);
            arena.swept_pool[c1][o + 11] = y.to_bits();
            arena.swept_pool[c1][o + 12] = z.to_bits();
        } else {
            vec_to_words(
                &mut arena.swept_pool[c1],
                o + 10,
                d1 + Vec3::new(pcx, pcy, pcz),
            );
        }
        let s1 = Vec3::new((hx1 - lx1) * 0.5, (hy1 - ly1) * 0.5, (hz1 - lz1) * 0.5);
        vec_to_words(&mut arena.swept_pool[c1], o + 13, s1);

        for c in [c0, c1] {
            let ob = box_from_words(&arena.swept_pool[c], o);
            let sw = sweep_box(&ob, dir, len);
            box_to_words(&mut arena.swept_pool[c], rec::BOX, &sw);
            let nd = arena.swept_pool[c][rec::NODE];
            arena.swept_pool[c][rec::BOX + 9] = nd.wrapping_add(1);
        }
        Some((BvHandle::Swept(c0 as i32), BvHandle::Swept(c1 as i32)))
    }

    /// 0x351381F0: children of a local record. The basis is copied only when the parent is
    /// oriented; otherwise the children's basis words keep whatever the pool slot held (stale,
    /// as in the original — consumers ignore it when `bOriented == 0`).
    fn get_node_children_bvs(
        &self,
        arena: &mut BvArena,
        parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        let pw = arena.bv_words(parent);
        let pbox = box_from_words(&pw, rec::BOX);
        let ich = self.nodes[pw[rec::NODE] as usize].ichild;
        let c0 = arena.alloc_bv();
        let c1 = arena.alloc_bv();
        let or = pbox.oriented != 0;
        for (c, node) in [(c0, ich), (c1, ich + 1)] {
            let (cc, cs) = self.child_box(node, pbox.center, pbox.size, &pbox.basis, or);
            let w = &mut arena.bv_pool[c];
            w[rec::TYPE] = 0;
            w[rec::NODE] = node as u32;
            w[rec::BOX + 9] = pbox.oriented as u32;
            if or {
                for (k, v) in pbox.basis.to_array().iter().enumerate() {
                    w[rec::BOX + k] = v.to_bits();
                }
            }
            vec_to_words(w, rec::BOX + 10, cc);
            vec_to_words(w, rec::BOX + 13, cs);
        }
        Some((BvHandle::Pool(c0 as i32), BvHandle::Pool(c1 as i32)))
    }

    /// 0x35137CD0: children of a transformed record (always oriented, basis copied).
    fn get_node_children_bvs_transformed(
        &self,
        arena: &mut BvArena,
        _r: &Mat33,
        _offset: Vec3,
        _scale: f32,
        parent: BvHandle,
    ) -> Option<(BvHandle, BvHandle)> {
        let pw = arena.bv_words(parent);
        let pbox = box_from_words(&pw, rec::BOX);
        let ich = self.nodes[pw[rec::NODE] as usize].ichild;
        let c0 = arena.alloc_bv();
        let c1 = arena.alloc_bv();
        for (c, node) in [(c0, ich), (c1, ich + 1)] {
            let (cc, cs) = self.child_box(node, pbox.center, pbox.size, &pbox.basis, true);
            let w = &mut arena.bv_pool[c];
            w[rec::TYPE] = 0;
            w[rec::NODE] = node as u32;
            box_to_words(
                w,
                rec::BOX,
                &BoxPrim {
                    basis: pbox.basis,
                    oriented: 1,
                    center: cc,
                    size: cs,
                },
            );
        }
        Some((BvHandle::Pool(c0 as i32), BvHandle::Pool(c1 as i32)))
    }

    /// 0x35136440.
    fn release_last_bvs(&self, arena: &mut BvArena) {
        arena.bv_count -= 2;
    }

    /// 0x35136460.
    fn release_last_swept_bvs(&self, arena: &mut BvArena) {
        arena.swept_count -= 2;
    }

    /// 0x351DB2C0: maxSkipDim / max(root half-size).
    fn max_skip_dim(&self) -> f32 {
        self.max_skip_dim / max_idiom(max_idiom(self.size.x, self.size.y), self.size.z)
    }

    /// 0x351368C0: "AABB trees", 128 + nodes + map.
    fn memory_size(&self) -> usize {
        128 + 12 * self.n_nodes_alloc.max(0) as usize
            + if self.tri_map.is_empty() {
                0
            } else {
                4 * Self::map_words(self.n_tris, self.map_shift)
            }
    }

    /// 0x35138E80: `−1, nNodes, nodes, centre, size, basis, +116, +112, +108, +120`.
    fn save(&self, w: &mut MemWriter) {
        w.i32(-1);
        w.i32(self.nodes.len() as i32);
        for nd in &self.nodes {
            w.bytes(&nd.to_bytes());
        }
        w.vec3(self.center);
        w.vec3(self.size);
        w.mat33(&self.basis);
        w.i32(self.max_leaf_tris);
        w.i32(self.min_tris_per_side);
        w.i32(self.max_tris_per_leaf);
        w.f32(self.max_skip_dim);
    }

    /// 0x35136480.
    fn get_node_contents(&self, i_node: i32) -> NodeContents {
        let nd = &self.nodes[i_node as usize];
        NodeContents {
            first_prim: nd.ichild,
            n_prims: i32::from(nd.ntris),
        }
    }

    /// 0x351DB230.
    fn get_node_contents_idx(&self, i_node: i32) -> NodeContents {
        self.get_node_contents(i_node)
    }

    /// 0x351366A0 (inert in practice: slot 6 always leaves the map null).
    fn mark_used_triangle(&self, i_tri: i32, gtest: &mut BvTestState, arena: &mut BvArena) {
        if gtest.used_map == UsedNodeMap::None {
            return;
        }
        let leaf = self.leaf_of_tri(i_tri);
        if self.nodes[leaf as usize].single_coll != 0 {
            if let Some(wd) = gtest.map_word_mut(arena, leaf) {
                *wd |= 1u32 << (leaf & 31);
            }
            gtest.push_used(arena, leaf);
            gtest.b_marked = 1;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A unit cube surface: 8 vertices, 12 triangles.
    fn cube(offset: Vec3, scale: f32) -> (Vec<Vec3>, Vec<i32>) {
        let mut v = Vec::new();
        for i in 0..8 {
            v.push(
                Vec3::new(
                    (i & 1) as f32 - 0.5,
                    ((i >> 1) & 1) as f32 - 0.5,
                    ((i >> 2) & 1) as f32 - 0.5,
                ) * scale
                    + offset,
            );
        }
        let idx = vec![
            0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4,
            6, 1, 3, 5, 3, 7, 5,
        ];
        (v, idx)
    }

    /// Several separated cubes in a row.
    fn cubes(n: usize) -> (Vec<Vec3>, Vec<i32>) {
        let (mut v, mut idx) = (Vec::new(), Vec::new());
        for k in 0..n {
            let (cv, ci) = cube(
                Vec3::new(3.0 * k as f32, 0.5 * k as f32, 0.0),
                1.0 + 0.1 * k as f32,
            );
            let base = v.len() as i32;
            v.extend(cv);
            idx.extend(ci.into_iter().map(|i| i + base));
        }
        (v, idx)
    }

    fn build(v: &[Vec3], idx: &mut [i32], max_tris: i32) -> AabbTree {
        let mut t = AabbTree::new(2, max_tris, 0.1, Mat33::IDENTITY, false);
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
    fn build_partitions_and_covers() {
        let (v, mut idx) = cubes(6);
        let t = build(&v, &mut idx, 4);
        assert!(t.nodes.len() > 3);
        // Every triangle is in exactly one leaf and the map agrees.
        let mut seen = vec![0; idx.len() / 3];
        for (i, nd) in t.nodes.iter().enumerate() {
            if i != 1 && nd.ntris > 0 {
                for k in 0..i32::from(nd.ntris) {
                    seen[(nd.ichild + k) as usize] += 1;
                    assert_eq!(t.leaf_of_tri(nd.ichild + k), i as i32);
                }
            }
        }
        assert!(seen.iter().all(|&s| s == 1));
        // Every leaf's decoded box contains its triangles' vertices.
        fn walk(t: &AabbTree, i: i32, c: Vec3, s: Vec3, v: &[Vec3], idx: &[i32]) {
            let nd = t.nodes[i as usize];
            let (d, cs) = nd.decode(s);
            let cc = d + c;
            if nd.ntris > 0 {
                for tri in nd.ichild..nd.ichild + i32::from(nd.ntris) {
                    for k in 0..3 {
                        let p = v[idx[(tri * 3 + k) as usize] as usize];
                        for a in 0..3 {
                            assert!((p[a] - cc[a]).abs() <= cs[a] * 1.0001 + 1e-5);
                        }
                    }
                }
            } else {
                walk(t, nd.ichild, cc, cs, v, idx);
                walk(t, nd.ichild + 1, cc, cs, v, idx);
            }
        }
        walk(&t, 0, t.basis.mul_vec(t.center), t.size, &v, &idx);
    }

    #[test]
    fn save_load_roundtrip_and_legacy() {
        let (v, mut idx) = cubes(5);
        let t = build(&v, &mut idx, 3);
        let mut w = MemWriter::new();
        t.save(&mut w);
        let b = w.into_bytes();
        let t2 = AabbTree::load(&mut MemReader::new(&b), t.n_tris).unwrap();
        let mut w2 = MemWriter::new();
        t2.save(&mut w2);
        assert_eq!(b, w2.into_bytes());
        assert_eq!(t2.tri_map, t.tri_map);
        assert_eq!(t2.nodes, t.nodes);

        // Legacy packed nodes.
        let mut lw = MemWriter::new();
        lw.i32(t.nodes.len() as i32);
        for nd in &t.nodes {
            let bb = nd.bounds.map(u32::from);
            lw.u32(bb[0] | bb[1] << 7 | bb[2] << 14 | bb[3] << 21);
            lw.u32(bb[4] | bb[5] << 7 | (nd.ichild as u32) << 14);
            lw.u32(u32::from(nd.ntris) | u32::from(nd.single_coll) << 6);
        }
        lw.vec3(t.center);
        lw.vec3(t.size);
        lw.mat33(&t.basis);
        for x in [t.max_leaf_tris, t.min_tris_per_side, t.max_tris_per_leaf] {
            lw.i32(x);
        }
        lw.f32(t.max_skip_dim);
        let lb = lw.into_bytes();
        let t3 = AabbTree::load(&mut MemReader::new(&lb), t.n_tris).unwrap();
        assert_eq!(t3.nodes, t.nodes);
    }

    #[test]
    fn local_and_transformed_children_agree() {
        let (v, mut idx) = cubes(4);
        let t = build(&v, &mut idx, 2);
        let mut a = BvArena::new();
        let root = t.get_node_bv(&mut a, 0);
        let (l, r) = t.get_node_children_bvs(&mut a, root).unwrap();
        let rt = t.get_node_bv_transformed(&mut a, &Mat33::IDENTITY, Vec3::ZERO, 1.0, 0);
        let (lt, rtt) = t
            .get_node_children_bvs_transformed(&mut a, &Mat33::IDENTITY, Vec3::ZERO, 1.0, rt)
            .unwrap();
        assert_eq!(a.bv_box(l).center, a.bv_box(lt).center);
        assert_eq!(a.bv_box(r).size, a.bv_box(rtt).size);
        assert_eq!(a.bv_box(rt).oriented, 1);
        assert!(t.split_priority(&a, root) > 0.0);
        assert_eq!(a.bv_count, 6);
        t.release_last_bvs(&mut a);
        assert_eq!(a.bv_count, 4);
    }

    #[test]
    fn swept_root_children_alias_slot_zero() {
        let (v, mut idx) = cubes(4);
        let t = build(&v, &mut idx, 2);
        let mut a = BvArena::new();
        let root = t.get_node_bv_swept(&mut a, Vec3::X, 2.0, 0).unwrap();
        assert_eq!(root, BvHandle::Swept(0));
        assert_eq!(a.swept_count, 0);
        let root_orig = a.swept_orig_box(0);
        let (c0, c1) = t
            .get_node_children_bvs_swept(&mut a, root, Vec3::X, 2.0)
            .unwrap();
        assert_eq!((c0, c1), (BvHandle::Swept(0), BvHandle::Swept(1)));
        // Child 0 is computed from the intact root.
        let ich = t.nodes[0].ichild;
        let (d0, s0) = t.nodes[ich as usize].decode(root_orig.size);
        let want_c0 = d0 + root_orig.center;
        assert_eq!(a.swept_orig_box(0).center, want_c0);
        assert_eq!(a.swept_orig_box(0).size, s0);
        // Child 1's x extent uses the root's size, its z extent child 0's.
        let n1 = t.nodes[(ich + 1) as usize];
        let (l, h) = n1.lo_hi(2, s0.z);
        assert_eq!(a.swept_orig_box(1).size.z, (h - l) * 0.5);
        let (lx, hx) = n1.lo_hi(0, root_orig.size.x);
        assert_eq!(a.swept_orig_box(1).size.x, (hx - lx) * 0.5);
        assert_eq!(a.bv_box(c1).oriented, ich + 2);
    }

    #[test]
    fn prepare_never_keeps_a_map() {
        let (v, mut idx) = cubes(3);
        let t = build(&v, &mut idx, 2);
        let mut a = BvArena::new();
        let mut g = BvTestState::default();
        t.prepare_for_intersection_test(&mut g, &mut a);
        assert_eq!(g.used_map, UsedNodeMap::None);
        assert_eq!(g.i_last_used, -1);
        // 36 tris > 16 and maxSkip > 0: the pools were still consumed.
        assert_eq!(a.used_list_count, 32);
        assert!(a.used_map_count > 0);
        t.mark_used_triangle(0, &mut g, &mut a);
        assert_eq!(g.b_marked, 0);
    }
}
