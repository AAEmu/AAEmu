//! `CTriMesh` neighbour topology (sub_351BFA60), islands (sub_351C0D80) and `IsConvex`
//! (slot 21, 0x351C2490).
//!
//! **Topology.** For every vertex the original builds the ascending list of triangles using it
//! (a counting sort; a triangle that uses a vertex twice is listed twice). For edge `j` of
//! triangle `i` (from vertex `j` to vertex `j+1 mod 3`) the lists of its two vertices are
//! intersected (sub_351BD620): with exactly two entries the neighbour is the other one (or the
//! second entry when the first is not `i`); otherwise every other listed triangle `t` that has
//! the edge in the opposite direction is a candidate, and the one whose third vertex makes the
//! "best" dihedral angle with this triangle's third vertex wins (−1 when none). The angle score
//! compares `|s|·s / q` ratios with `s` the triple product of the two perpendiculars and `q`
//! their squared lengths times `|e|²`; **both are multiplied by `isneg(1e10 − q)·1e-15`, which
//! is 0 whenever `q ≤ 1e10`** (kept: for ordinary meshes the first oppositely oriented candidate
//! wins and the rest tie). Edges with one triangle count as open, with more than two as
//! non-manifold; the sum goes to `+540` ([`TriMesh::n_bad_edges`]).
//!
//! **Islands.** A flood fill over the topology; unvisited triangles form a circular doubly
//! linked list packed into 32-bit words ([`TriMesh::tri_island`]: bits 0–15 next, 16–30
//! previous, 31 unvisited; **16- and 15-bit links**, so meshes above 32768 / 65536 triangles get
//! wrong links — kept). After the fill bits 16–30 hold the island index and bits 0–15 link the
//! island's triangles (from [`super::MeshIsland::first_tri`]). Each island accumulates its
//! signed volume and centre; the mesh's cached volume (+516) is their sum and the cached centre
//! (+528) the volume-weighted centre (divided only when the total volume is > 0).
//!
//! **IsConvex(tol).** Results are cached for up to 4 tolerances (most recent first). A
//! multi-island mesh is not convex (without storing the tolerance). Otherwise every edge must
//! have a neighbour, and wherever the two normals differ by more than `tol` (`|n_t×n_i|² >
//! tol²`), the edge direction must not point against `n_t×n_i` (a NaN makes it non-convex).

use super::build::{intersect_sorted, ueq};
use super::{MeshIsland, TriMesh};
use crate::math::Vec3;

/// `{1, 2, 0}` (0x3520A770): the next vertex of a triangle.
pub const INC_MOD3: [usize; 3] = [1, 2, 0];
/// `{2, 0, 1}` (0x3520A780): the previous vertex of a triangle.
pub const DEC_MOD3: [usize; 3] = [2, 0, 1];

impl TriMesh {
    /// sub_351BFA60(this, this.indices, 0): rebuilds [`TriMesh::topology`] and
    /// [`TriMesh::n_bad_edges`]; returns 1 when every edge has exactly one neighbour.
    pub fn build_topology(&mut self) -> i32 {
        let idx = self.indices.clone();
        let (topo, bad) = self.topology_pass(&idx, true);
        self.topology = topo;
        self.n_bad_edges = bad;
        i32::from(bad == 0)
    }

    /// sub_351BFA60(this, indices, 1): the number of open plus non-manifold edges of the
    /// triangles `indices` (the topology itself is discarded).
    #[must_use]
    pub fn count_bad_edges(&self, indices: &[i32]) -> i32 {
        self.topology_pass(indices, false).1
    }

    /// The body of sub_351BFA60. `idx` is the index array the edges and vertex lists come from;
    /// the positions always come from the mesh's own indices (as in the original). Returns the
    /// topology (empty unless `keep`) and the bad-edge count.
    pub(crate) fn topology_pass(&self, idx: &[i32], keep: bool) -> (Vec<[i32; 3]>, i32) {
        let nt = self.n_tris.max(0) as usize;
        let nv = self.n_verts.max(0) as usize;
        let mut start = vec![0i32; nv + 1];
        for &v in &idx[..3 * nt] {
            start[v as usize] += 1;
        }
        let lim = 4 * nt as i32 - 1;
        for k in 1..=nv {
            start[k] = (start[k] + start[k - 1]).min(lim);
        }
        let mut lists = vec![0i32; 3 * nt];
        for t in (0..nt).rev() {
            for k in 0..3 {
                let v = idx[3 * t + k] as usize;
                start[v] -= 1;
                lists[start[v] as usize] = t as i32;
            }
        }
        let list = |v: i32| {
            let (a, b) = (start[v as usize], start[v as usize + 1]);
            (&lists[a as usize..b.max(a) as usize], b - a)
        };
        let mi = &self.indices;
        let pos = |k: usize| self.vertices[mi[k] as usize];
        let mut topo = if keep {
            vec![[0i32; 3]; nt]
        } else {
            Vec::new()
        };
        let mut scratch: Vec<i32> = Vec::new();
        let (mut open, mut nonman) = (0i32, 0i32);
        for i in 0..nt {
            for j in 0..3 {
                let (la, na) = list(idx[3 * i + j]);
                let (lb, nb) = list(idx[3 * i + INC_MOD3[j]]);
                if scratch.len() < la.len() + 1 {
                    scratch.resize(la.len() + 1, 0);
                }
                let n = intersect_sorted(la, na, lb, nb, &mut scratch);
                let result;
                if n == 2 {
                    let x = i as i32 - scratch[0];
                    let d = i64::from(x) ^ i64::from(x.wrapping_sub(1));
                    result = scratch[(-(d >> 31)) as usize];
                } else {
                    let pa = pos(3 * i + j);
                    let pb = pos(3 * i + INC_MOD3[j]);
                    let pc = pos(3 * i + DEC_MOD3[j]);
                    let (ex, ey, ez) = (pb.x - pa.x, pb.y - pa.y, pb.z - pa.z);
                    let (cx, cy, cz) = (pc.x - pa.x, pc.y - pa.y, pc.z - pa.z);
                    let e2 = (ex * ex + ey * ey) + ez * ez;
                    let ce = (cx * ex + cy * ey) + cz * ez;
                    let c0 = cx * e2 - ce * ex;
                    let c1 = cy * e2 - ey * ce;
                    let c2 = cz * e2 - ez * ce;
                    let (mut num, mut den, mut best) = (3.0f32, 1.0f32, -1i32);
                    let ia = idx[3 * i + j];
                    let ib = idx[3 * i + INC_MOD3[j]];
                    for &t in &scratch[..n.max(0) as usize] {
                        if t == i as i32 {
                            continue;
                        }
                        let tu = t as usize;
                        for m in 0..3 {
                            if ia != idx[3 * tu + INC_MOD3[m]] || ib != idx[3 * tu + m] {
                                continue;
                            }
                            let pd = pos(3 * tu + DEC_MOD3[m]);
                            let (dx, dy, dz) = (pd.x - pa.x, pd.y - pa.y, pd.z - pa.z);
                            let de = (dx * ex + dy * ey) + dz * ez;
                            let d1 = dy * e2 - ey * de;
                            let d2 = dz * e2 - ez * de;
                            let d0 = dx * e2 - de * ex;
                            let s = ((d2 * c1 - d1 * c2) * ex + (c2 * d0 - d2 * c0) * ey)
                                + (d1 * c0 - c1 * d0) * ez;
                            let s2 = s.abs() * s;
                            let q = (((d1 * d1 + d0 * d0) + d2 * d2)
                                * ((c1 * c1 + c0 * c0) + c2 * c2))
                                * e2;
                            let k = ((f32::from_bits(0x5015_02F9) - q).to_bits() >> 31) as f32
                                * f32::from_bits(0x2690_1D7D);
                            let qk = q * k;
                            let sk = s2 * k;
                            let r = sk + ((2 * (sk.to_bits() >> 31)) as i32 as f32) * qk;
                            if !(((den * r) - (num * qk))
                                + ((r - num) * f32::from_bits(0x1E3C_E508))
                                >= 0.0)
                            {
                                num = r;
                                den = qk;
                                best = t;
                            }
                        }
                    }
                    result = best;
                    match n {
                        1 => open += 1,
                        // n == 0 cannot happen (a triangle is in both of its vertices' lists);
                        // the original would bump a stack slot below its counters.
                        0 => {}
                        _ => nonman += 1,
                    }
                }
                if keep {
                    topo[i][j] = result;
                }
            }
        }
        (topo, open + nonman)
    }

    /// sub_351C0D80: the island list (+128 / +136) and per-triangle links (+144), plus the
    /// cached volume (+516) and centre (+528). Returns the island count; does nothing when the
    /// list exists. A mesh without triangles keeps no list (and returns 0).
    pub fn build_islands(&mut self) -> i32 {
        if let Some(isl) = &self.islands {
            return isl.len() as i32;
        }
        let nt = self.n_tris;
        if nt <= 0 {
            return 0;
        }
        let ntu = nt as usize;
        let mut til = vec![0u32; ntu];
        for (i, w) in til.iter_mut().enumerate() {
            let i = i as i32;
            let mut x = (i + 1) as u32 & 0xFFFF;
            let prev = (i.wrapping_sub(1) << 16) as u32;
            x ^= (x ^ prev) & 0x7FFF_0000;
            *w = x | 0x8000_0000;
        }
        til[0] ^= (til[0] ^ ((nt - 1) << 16) as u32) & 0x7FFF_0000;
        til[ntu - 1] &= 0xFFFF_0000;
        self.cached_volume = 0.0;
        let mut center = Vec3::ZERO;
        let mut total_v = 0.0f32;
        let mut islands: Vec<MeshIsland> = Vec::new();
        let mut stack: Vec<i32> = Vec::new();
        let mut v8 = 0i32;
        loop {
            let ii = islands.len();
            let mut words = [0u32; 10];
            words[0] = 0x7FFF;
            words[2] = u32::MAX;
            words[3] = u32::MAX;
            words[4] = u32::MAX;
            islands.push(MeshIsland { words });
            stack.clear();
            stack.push(v8);
            til[v8 as usize] &= !0x8000_0000;
            let (mut vol, mut cx, mut cy, mut cz) = (0.0f32, 0.0f32, 0.0f32, 0.0f32);
            let mut ntri = 0u32;
            let mut head = 0x7FFFu32;
            let mut v68;
            loop {
                let t = stack.pop().expect("island stack") as usize;
                let wt = til[t];
                let prev = ((wt >> 16) & 0x7FFF) as usize;
                til[prev] = (til[prev] & 0xFFFF_0000) | (wt & 0xFFFF);
                let wt2 = til[t];
                let next = (wt2 & 0xFFFF) as usize;
                til[next] &= 0x8000_FFFF;
                til[next] |= wt2 & 0x7FFF_0000;
                let w3 = til[t];
                let dd = v8.wrapping_sub(t as i32);
                let mask = (dd ^ dd.wrapping_sub(1)) >> 31;
                v68 = i32::from((mask & w3 as i32) as u16) | (v8 & !mask);
                til[t] ^= (til[t] ^ ((ii as u32) << 16)) & 0x7FFF_0000;
                til[t] = (til[t] & 0xFFFF_0000) | (head & 0xFFFF);
                head = t as u32;
                ntri += 1;
                let p = |k: usize| self.vertices[self.indices[3 * t + k] as usize];
                let (p0, p1, p2) = (p(0), p(1), p(2));
                let sz = (p1.z + p0.z) + p2.z;
                let sx = p2.x + (p1.x + p0.x);
                let sy = p2.y + (p1.y + p0.y);
                vol += ((p2.y - p0.y) * (p1.x - p0.x) - (p1.y - p0.y) * (p2.x - p0.x)) * sz;
                cx += sx;
                cy += sy;
                cz += sz;
                for e in 0..3 {
                    let nb = self.topology[t][e];
                    if nb >= 0 && (til[nb as usize] as i32) < 0 {
                        stack.push(nb);
                        til[nb as usize] &= !0x8000_0000;
                    }
                }
                v8 = v68;
                if stack.is_empty() {
                    break;
                }
            }
            let vol = vol * f32::from_bits(0x3E2A_AAAB);
            total_v += vol;
            let inv = 1.0 / (3 * ntri as i32) as f32;
            let (cx, cy, cz) = (inv * cx, inv * cy, inv * cz);
            center = Vec3::new(
                cx * vol + center.x,
                cy * vol + center.y,
                cz * vol + center.z,
            );
            let rec = &mut islands[ii].words;
            rec[0] = head;
            rec[1] = ntri;
            rec[5] = vol.to_bits();
            rec[6] = cx.to_bits();
            rec[7] = cy.to_bits();
            rec[8] = cz.to_bits();
            if (til[v68 as usize] as i32) >= 0 {
                break;
            }
        }
        if total_v > 0.0 {
            let inv = 1.0 / total_v;
            center = Vec3::new(inv * center.x, inv * center.y, inv * center.z);
        }
        self.cached_volume = total_v;
        self.cached_center = center;
        let n = islands.len() as i32;
        self.islands = Some(islands);
        self.tri_island = Some(til);
        n
    }

    /// Slot 21 `IsConvex(tolerance)` (0x351C2490), with the 4-entry cache at +480 / +496.
    pub fn is_convex(&mut self, tol: f32) -> i32 {
        let mut k = 0usize;
        while k < 3 {
            let t = self.convex_tol[k];
            if !(t >= 0.0) || ueq(t, tol) {
                break;
            }
            k += 1;
        }
        if ueq(tol, self.convex_tol[k]) {
            self.convex_tol[k] = self.convex_tol[0];
            self.convex_tol[0] = tol;
            let r = self.convex_cache[k];
            self.convex_cache[k] = self.convex_cache[0];
            self.convex_cache[0] = r;
            return r;
        }
        if self.multi_island != 0 {
            self.convex_cache[k] = 0;
            return 0;
        }
        self.convex_tol[k] = tol;
        let tol2 = tol * tol;
        for i in 0..self.n_tris.max(0) as usize {
            let ni = self.normals[i];
            for j in 0..3 {
                let t = self.topology[i][j];
                if t < 0 {
                    self.convex_cache[k] = 0;
                    return 0;
                }
                let nt = self.normals[t as usize];
                let a = nt.z * ni.y - nt.y * ni.z;
                let c = ni.x * nt.y - nt.x * ni.y;
                let b = nt.x * ni.z - ni.x * nt.z;
                if (a * a + b * b) + c * c > tol2 {
                    let p0 = self.vertices[self.indices[3 * i + j] as usize];
                    let p1 = self.vertices[self.indices[3 * i + INC_MOD3[j]] as usize];
                    let dot = ((p1.x - p0.x) * a + (p1.y - p0.y) * b) + (p1.z - p0.z) * c;
                    if !(dot >= 0.0) {
                        self.convex_cache[k] = 0;
                        return 0;
                    }
                }
            }
        }
        self.convex_cache[k] = 1;
        1
    }
}
