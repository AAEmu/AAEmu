//! `CHeightfield` slot 53 (`PrepareForIntersectionTest`, 0x35155180) up to its tail call into
//! `CTriMesh` slot 53 (0x351D3620): pick the cells under the other geometry's bounding box (the
//! *region*) and rebuild the `CTriMesh` base arrays as the triangle mesh of those cells.
//!
//! Region mesh (`w × h` cells starting at cell `(ix, iy)`), in the **region frame** (cell
//! `(ix, iy)`'s corner is the origin; the gtest offset is moved there):
//!
//! * vertex `r·(w+1) + c` (`r = 0..=h`, `c = 0..=w`) =
//!   `(x_c − ix·step.x, (iy + r)·step.y − iy·step.y, H(ix + c, iy + r)·hs)` where `x_c` starts at
//!   `ix·step.x` and adds `step.x` per column (accumulated, as in the binary);
//! * cell `k = r·w + c` (`r < h`, `c < w`), `v = r·(w+1) + c`: triangle `2k` = `(v, v+1, v+w+1)`,
//!   triangle `2k+1` = `(v+w+1, v+1, v+w+2)` (the `u + v = 1` split), normal
//!   `normalize((p1 − p0) × (p2 − p0))` (`(0, 0, 1)` when `|n|² ≤ 1e-20`);
//! * material of both = the cell's decoded surface type, or −1 (0xFF) for the hole type;
//! * topology (neighbour across edges 0, 1, 2), `−1` at the region border:
//!   `2k: [2k − 2w + 1 | row 0, 2k + 1, 2k − 1 | col 0]`,
//!   `2k+1: [2k, 2k + 2 | last col, 2k + 2w | last row]`; then every link of a neighbour that
//!   points back at a material-0xFF triangle is cleared to −1 (slot `2` if the neighbour's
//!   edge 2 points back, else `1` if edge 1 does, else `0`).
//!
//! `hmin`/`hmax` of the BV are the min/max idioms over the vertex heights.

use crate::geometry::bvtree::swept::sweep_box;
use crate::geometry::primitives::BoxPrim;
use crate::math::{Mat33, Vec3};

use super::bv::collider_cells;
use super::{height, surf_or_hole, surf_type, Heightfield};

/// The fields of this side's `geometry_under_test` that the heightfield code reads or writes
/// (names as in the P1a `GeomUnderTest`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct HfFrame {
    /// `+40` stop flag.
    pub stop: i32,
    /// `+48` rotation of this geometry's frame.
    pub r: Mat33,
    /// `+84` rotation relative to the other side.
    pub r_rel: Mat33,
    /// `+120` offset (slot 53 moves it to the region corner).
    pub offset: Vec3,
    /// `+132` offset relative to the other side.
    pub offset_rel: Vec3,
    /// `+156` reciprocal of the relative scale.
    pub scale_rel_r: f32,
}

impl Default for HfFrame {
    fn default() -> Self {
        Self {
            stop: 0,
            r: Mat33::IDENTITY,
            r_rel: Mat33::IDENTITY,
            offset: Vec3::ZERO,
            offset_rel: Vec3::ZERO,
            scale_rel_r: 1.0,
        }
    }
}

/// The other side's sweep, from its gtest (`+248` direction, `+260` "swept" value — the sweep is
/// applied unless `0 ≥ value`, so also for NaN — and `+264` length).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct OtherSweep {
    pub dir: Vec3,
    pub flag: f32,
    pub len: f32,
}

/// What slot 53 returns.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RegionOutcome {
    /// 0: the region is empty, has more than 1 000 000 cells, or the other box is entirely
    /// above the region's highest vertex. Nothing intersects.
    Reject,
    /// 1 without the mesh tail: no gtest, or its stop flag (`+40`) is set.
    Done,
    /// The original now returns `CTriMesh::PrepareForIntersectionTest(gtest, other,
    /// gtest_other, bKeepPrevContacts)` (0x351D3620) on the region mesh: the lead calls it.
    CallMeshSlot53,
}

impl Heightfield {
    /// Slot 53 (0x35155180) without the `CTriMesh` slot-53 tail.
    ///
    /// 1. The other geometry's box (its slot 6), swept by [`sweep_box`] (sub_35135490) when
    ///    `other_sweep` applies.
    /// 2. [`collider_cells`] (sub_35154220) with `gtest` (or in the heightfield frame without
    ///    one) → `(ix, iy, w, h, zmin)`. Reject when `((h − 1) | (w − 1) | (1e6 − h·w)) < 0`.
    /// 3. `gtest.offset += R·(ix·step.x, iy·step.y, 0)` (row `k`: `(iy·sy·R[k][1] +
    ///    ix·sx·R[k][0]) + R[k][2]·0`, the pairs in the binary's order).
    /// 4. If the region differs from the BV's: `hmax = max_idiom` of `H·hs` over the vertices
    ///    `ix..=ix+w × iy..=iy+h` (x outer, starting from `H(ix, iy)·hs`); reject when
    ///    `zmin > hmax` (the BV keeps the **old** region); else store the region and rebuild the
    ///    mesh (module docs), growing the arrays to `((n − 1) & ~15) + 16`.
    ///    If the region is unchanged, reject when `zmin > ` the BV's `hmax`.
    /// 5. Clear used-mask words `0..=(nTris − 1) >> 5`.
    /// 6. [`RegionOutcome::Done`] without a gtest or when its stop flag is set, else
    ///    [`RegionOutcome::CallMeshSlot53`].
    pub fn prepare_region(
        &mut self,
        other_bbox: &BoxPrim,
        other_sweep: Option<OtherSweep>,
        gtest: Option<&mut HfFrame>,
    ) -> RegionOutcome {
        let bx = match other_sweep {
            Some(s) if !(0.0 >= s.flag) => sweep_box(other_bbox, s.dir, s.len),
            _ => *other_bbox,
        };
        let p = self.bv.prim;
        let cells = collider_cells(&bx, &p, gtest.as_deref());
        let (ix, iy, w, h) = (cells.ix, cells.iy, cells.w, cells.h);
        if (h.wrapping_sub(1) | w.wrapping_sub(1) | 1_000_000i32.wrapping_sub(h.wrapping_mul(w))) < 0
        {
            return RegionOutcome::Reject;
        }
        let tx = ix as f32 * p.step[0];
        let ty = iy as f32 * p.step[1];
        let mut gtest = gtest;
        if let Some(g) = gtest.as_deref_mut() {
            let m = &g.r.m;
            g.offset = Vec3::new(
                ((ty * m[0][1] + tx * m[0][0]) + m[0][2] * 0.0) + g.offset.x,
                ((tx * m[1][0] + ty * m[1][1]) + m[1][2] * 0.0) + g.offset.y,
                ((tx * m[2][0] + ty * m[2][1]) + m[2][2] * 0.0) + g.offset.z,
            );
        }
        let changed = ((self.bv.w ^ w) | (self.bv.ix ^ ix) | (self.bv.h ^ h) | (self.bv.iy ^ iy)) != 0;
        if changed {
            let hs = p.height_scale;
            let mut hmax = height(&p, ix, iy) * hs;
            let mut x = ix;
            while x <= ix.wrapping_add(w) {
                let mut y = iy;
                while y <= iy.wrapping_add(h) {
                    let z = height(&p, x, y) * hs;
                    hmax = ((hmax - z).abs() + (z + hmax)) * 0.5;
                    y += 1;
                }
                x += 1;
            }
            if cells.zmin > hmax {
                return RegionOutcome::Reject;
            }
            self.build_region_mesh(ix, iy, w, h, tx, ty);
        } else if cells.zmin > self.bv.hmax {
            return RegionOutcome::Reject;
        }
        let last = (self.mesh.n_tris.wrapping_sub(1)) >> 5;
        let mut j = last;
        while j >= 0 {
            if let Some(a) = self.bv.used_tris.get(j as usize) {
                a.store(0, std::sync::atomic::Ordering::Relaxed);
            }
            j -= 1;
        }
        match gtest {
            Some(g) if g.stop == 0 => RegionOutcome::CallMeshSlot53,
            _ => RegionOutcome::Done,
        }
    }

    /// Step 4's rebuild (region changed and not rejected).
    fn build_region_mesh(&mut self, ix: i32, iy: i32, w: i32, h: i32, tx: f32, ty: f32) {
        let p = self.bv.prim;
        let hs = p.height_scale;
        self.bv.ix = ix;
        self.bv.iy = iy;
        self.bv.w = w;
        self.bv.h = h;
        let nv = (h + 1).wrapping_mul(w + 1);
        let nt = 2i32.wrapping_mul(h).wrapping_mul(w);
        self.mesh.n_verts = nv;
        self.mesh.max_vertex_valency = 6;
        self.mesh.n_tris = nt;
        if self.vtx_cap < nv {
            let cap = ((nv - 1) & !15) + 16;
            self.vtx_cap = cap;
            self.mesh.vertices = vec![Vec3::ZERO; cap as usize];
        }
        if self.tri_cap < nt {
            let cap = ((nt - 1) & !15) + 16;
            self.tri_cap = cap;
            self.mesh.normals = vec![Vec3::ZERO; cap as usize];
            self.mesh.indices = vec![0; 3 * cap as usize];
            self.mesh.mats = Some(vec![0; cap as usize]);
            self.mesh.topology = vec![[0; 3]; cap as usize];
            self.bv.set_used_mask_words(((cap >> 5) + 1) as usize);
        }
        let z0 = height(&p, ix, iy) * hs;
        self.bv.hmin = z0;
        self.bv.hmax = z0;
        let mut k = 0usize;
        for r in 0..=h {
            let mut x = ix as f32 * p.step[0];
            let y = (r + iy) as f32 * p.step[1] - ty;
            for c in 0..=w {
                let z = height(&p, c + ix, r + iy) * hs;
                self.mesh.vertices[k] = Vec3::new(x - tx, y, z);
                k += 1;
                let lo = self.bv.hmin;
                self.bv.hmin = ((lo + z) - (lo - z).abs()) * 0.5;
                let hi = self.bv.hmax;
                self.bv.hmax = ((hi - z).abs() + (z + hi)) * 0.5;
                x += p.step[0];
            }
        }
        let mut v = 0i32;
        let mut t = 0usize;
        for row in 0..h {
            for col in 0..w {
                let tri_a = [v, v + 1, v + w + 1];
                let tri_b = [v + w + 1, v + 1, v + w + 2];
                for (j, tri) in [tri_a, tri_b].into_iter().enumerate() {
                    self.mesh.indices[3 * (t + j)..3 * (t + j) + 3].copy_from_slice(&tri);
                    let n = face_normal(
                        self.mesh.vertices[tri[0] as usize],
                        self.mesh.vertices[tri[1] as usize],
                        self.mesh.vertices[tri[2] as usize],
                    );
                    self.mesh.normals[t + j] = n;
                }
                let k2 = t as i32;
                let row_first = (row - 1) >> 31;
                self.mesh.topology[t] = [
                    (k2 - 2 * w + 1) | row_first,
                    k2 + 1,
                    (k2 - 1) | ((col - 1) >> 31),
                ];
                self.mesh.topology[t + 1] = [
                    k2,
                    (k2 + 2) | ((w - col - 2) >> 31),
                    (k2 + 2 * w) | ((h - row - 2) >> 31),
                ];
                let s = surf_or_hole(&p, surf_type(&p, col + ix, row + iy)) as i8;
                if let Some(m) = self.mesh.mats.as_mut() {
                    m[t] = s;
                    m[t + 1] = s;
                }
                t += 2;
                v += 1;
            }
            v += 1;
        }
        let mats = self.mesh.mats.clone().unwrap_or_default();
        for i in 0..nt.max(0) as usize {
            if mats[i] != -1 {
                continue;
            }
            for e in 0..3 {
                let nb = self.mesh.topology[i][e];
                if nb < 0 {
                    continue;
                }
                let me = i as i32;
                let t1 = self.mesh.topology[nb as usize][1].wrapping_sub(me);
                let t2 = self.mesh.topology[nb as usize][2].wrapping_sub(me);
                let is1 = ((t1 ^ t1.wrapping_sub(1)) >> 31) & 1;
                let m2 = (t2 ^ t2.wrapping_sub(1)) >> 31;
                let slot = (!m2 & is1) | (m2 & 2);
                self.mesh.topology[nb as usize][slot as usize] = -1;
            }
        }
    }
}

/// `normalize((p1 − p0) × (p2 − p0))` as the region build and the ray cell test compute it:
/// with `a = p2 − p0`, `b = p1 − p0`: `n = (b.y·a.z − b.z·a.y, b.z·a.x − a.z·b.x,
/// a.y·b.x − b.y·a.x)`; `|n|² = (n.y² + n.x²) + n.z²`; `> 1e-20` → `n·(1/√|n|²)`, else `(0,0,1)`.
#[must_use]
pub(crate) fn face_normal(p0: Vec3, p1: Vec3, p2: Vec3) -> Vec3 {
    let n = cross_ba(p0, p1, p2);
    let l2 = (n.y * n.y + n.x * n.x) + n.z * n.z;
    if l2 > 1e-20 {
        let k = 1.0 / l2.sqrt();
        Vec3::new(n.x * k, n.y * k, n.z * k)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    }
}

/// The unnormalised `(p1 − p0) × (p2 − p0)` (see [`face_normal`]).
#[must_use]
pub(crate) fn cross_ba(p0: Vec3, p1: Vec3, p2: Vec3) -> Vec3 {
    let a = Vec3::new(p2.x - p0.x, p2.y - p0.y, p2.z - p0.z);
    let b = Vec3::new(p1.x - p0.x, p1.y - p0.y, p1.z - p0.z);
    Vec3::new(
        b.y * a.z - b.z * a.y,
        b.z * a.x - a.z * b.x,
        a.y * b.x - b.y * a.x,
    )
}
