//! `CTriMesh` slot 49 `UnprojectSphere(center, r, rsep, unproj_result*)` (0x351D8EA0): the
//! closest point of the mesh to a sphere centre, walking from a cached triangle.
//!
//! 1. **Cached triangle** (`out.i_feature[0]`, the result's `+40`): when it is a valid index
//!    and the centre projects inside it (three edge-side sign tests), `pt` = the projection
//!    and `n` = the triangle normal are written to `out`; if the centre is in front of the
//!    plane the call returns `|pt − c|² < rsep²` right there (`pt`/`n` stay written either
//!    way).
//! 2. Otherwise slot 22 `PrepareForRayTest(0)` (the grid is built on demand), then a start
//!    triangle: the first triangle (ascending) of **layer 0**'s cell under the centre (indices
//!    clamped into the grid) hit by the ray from the centre along the layer's ray axis, scaled
//!    by `ny·stepy + nx·stepx` and pointed away from the mesh centre (slot 32); else the first
//!    contact of slot 10 with a stack ray from the centre towards the cached mesh centre,
//!    `3.5·rsep` long, `ip` defaults with "first contact only" — no contact → return 0.
//! 3. The walk: inside the current triangle (all three edge tests pass) → its plane
//!    projection; else to the neighbour across the first failing edge (the second one when two
//!    edges fail and the first neighbour was visited already); a triangle visited twice ends
//!    the walk on the segment of the edge it was entered through (clamped projection).
//! 4. Unless the start came from the grid ray (then always accepted), `|pt − c|² > rsep²`
//!    returns 0. `n = normalize(c − pt)`, negated for a grid-ray start (the centre is inside);
//!    `out.pt`, `out.n`, `out.i_feature[0] = triangle`; return 1.
//!
//! The visited bitmask (`+592`, allocated once per mesh and cleared per call) is a local here.
//!
//! Deviations (the original reads out of bounds): an open edge (`topology < 0`) met by the
//! walk returns 0; a missing grid (slot 22 refused: mesh without a tree) returns 0.

// The NaN-aware comparisons mirror the original's `comiss` branches.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::intersect::{default_local_ip, IntersectExtra, Tables};
use super::protocol::CallerArena;
use super::raytest::layer_cell;
use super::TriMesh;
use crate::collision::contact::{PrimInters, UnprojResult};
use crate::collision::pairs::tri_pairs::intersect_ray_tri;
use crate::geometry::primitives::RayPrim;
use crate::geometry::shapes::RayGeom;
use crate::geometry::GeomWorldData;
use crate::math::Vec3;

/// `(i + 1) mod 3`.
const INC: [usize; 3] = [1, 2, 0];

#[inline]
fn sb(x: f32) -> u32 {
    x.to_bits() >> 31
}

#[inline]
fn mn0(x: i32) -> i32 {
    x & (x >> 31)
}

/// The edge-side sign of slot 49 (`e = v[i+1] − v[i]`, `q = c − v[i]`):
/// `(n.y·(e.z·q.x − q.z·e.x) + n.x·(q.z·e.y − e.z·q.y)) + n.z·(q.y·e.x − e.y·q.x)`.
#[inline]
fn edge_side(c: Vec3, p: Vec3, e: Vec3, n: Vec3) -> u32 {
    let (qx, qy, qz) = (c.x - p.x, c.y - p.y, c.z - p.z);
    sb((n.y * (e.z * qx - qz * e.x) + n.x * (qz * e.y - e.z * qy)) + n.z * (qy * e.x - e.y * qx))
}

impl TriMesh {
    /// Slot 49 `UnprojectSphere` (0x351D8EA0), see the module docs. `_r` (the third argument)
    /// is not read by the original. Mutates the mesh like the original: the ray grid may be
    /// built (slot 22) and the centre cached (slot 32). `tables` / `arena` serve the internal
    /// slot-10 call.
    #[allow(clippy::too_many_arguments)]
    pub fn unproject_sphere_slot49(
        &mut self,
        tables: Tables<'_>,
        arena: &mut CallerArena,
        c: Vec3,
        _r: f32,
        rsep: f32,
        out: &mut UnprojResult,
    ) -> i32 {
        let t0 = out.i_feature[0];
        if (t0 as u32) < self.n_tris as u32 {
            let tp = self.tri_prim(t0 as usize);
            let n = tp.n;
            let v = tp.pt;
            let s = (0..3)
                .map(|i| {
                    let e = v[INC[i]] - v[i];
                    edge_side(c, v[i], e, n)
                })
                .sum::<u32>();
            if s == 0 {
                let d = ((v[0].y - c.y) * n.y + (v[0].x - c.x) * n.x) + (v[0].z - c.z) * n.z;
                let pt = Vec3::new(c.x + n.x * d, n.y * d + c.y, n.z * d + c.z);
                out.pt = pt;
                out.n = n;
                if ((c.y - pt.y) * n.y + (c.x - pt.x) * n.x) + (c.z - pt.z) * n.z > 0.0 {
                    let (dx, dy, dz) = (pt.x - c.x, pt.y - c.y, pt.z - c.z);
                    return sb(((dy * dy + dx * dx) + dz * dz) - rsep * rsep) as i32;
                }
            }
        }
        self.prepare_for_ray_test(0.0);
        let Some(l0) = self
            .ray_grid
            .as_ref()
            .and_then(|g| g.layers.first())
            .cloned()
        else {
            return 0;
        };
        let (nx, ny) = (l0.size[0], l0.size[1]);
        let (ax, ay, go) = (l0.axis_x, l0.axis_y, l0.origin);
        let (dx, dy, dz) = (c.x - go.x, c.y - go.y, c.z - go.z);
        let liy = ((((dx * ay.x + dy * ay.y) + dz * ay.z) * l0.rstep[1]) - 0.5 + 12_582_912.0)
            .to_bits() as i32;
        let lix = ((((dy * ax.y + dx * ax.x) + dz * ax.z) * l0.rstep[0]) - 0.5 + 12_582_912.0)
            .to_bits() as i32;
        let w = i32::wrapping_sub;
        let ix = mn0(w(0x4B40_0000, lix)).wrapping_neg();
        let ix = ix.wrapping_add(mn0(w(w(nx, 1), ix)));
        let my = mn0(w(0x4B40_0000, liy));
        let iy = mn0(w(ny, 1).wrapping_add(my)).wrapping_sub(my);
        let cell = l0.stride[1]
            .wrapping_mul(iy)
            .wrapping_add(l0.stride[0].wrapping_mul(ix));
        let ctr = self.get_center();
        let k = (ny as f32 * l0.step[1]) + (nx as f32 * l0.step[0]);
        let ar = l0.axis_ray;
        let mut dir = Vec3::new(ar.x * k, ar.y * k, ar.z * k);
        let sdot = ((c.y - ctr.y) * dir.y + (c.x - ctr.x) * dir.x) + (c.z - ctr.z) * dir.z;
        let sg = ((((sdot.to_bits() as i32) >> 30) & !1) + 1) as f32;
        dir = Vec3::new(dir.x * sg, dir.y * sg, dir.z * sg);
        let ray = RayPrim { origin: c, dir };
        let mut inters = PrimInters::default();
        let mut hit = 0i32;
        let mut tri: i32 = -1;
        for &t in layer_cell(&l0, cell) {
            if intersect_ray_tri(&ray, &self.tri_prim(t as usize), &mut inters) {
                hit = 1;
                tri = t;
                break;
            }
        }
        if hit == 0 {
            let cc = self.cached_center;
            let (vx, vy, vz) = (cc.x - c.x, cc.y - c.y, cc.z - c.z);
            let l2 = (vy * vy + vx * vx) + vz * vz;
            let nd = if !(l2 > f32::from_bits(0x1E3C_E508)) {
                Vec3::new(0.0, 0.0, 1.0)
            } else {
                let k = 1.0 / l2.sqrt();
                Vec3::new(k * vx, vy * k, vz * k)
            };
            let len = rsep * 3.5;
            let mut rg = RayGeom::new(&RayPrim {
                origin: c,
                dir: Vec3::new(len * nd.x, nd.y * len, nd.z * len),
            });
            rg.ndir = nd;
            let mut ip = default_local_ip();
            let mut gwd = GeomWorldData::default();
            let extra = IntersectExtra {
                start_node: [0, 0],
                first_only: true,
            };
            let r = self.intersect_slot10(
                tables,
                arena,
                &rg,
                Some(&mut gwd),
                None,
                Some(&mut ip),
                extra,
            );
            let Some(c0) = r.contacts.first() else {
                return 0;
            };
            tri = c0.i_prim[0];
        }
        let nwords = ((self.n_tris.max(0) as usize) + 31) >> 5;
        let mut mask = vec![0u32; nwords];
        let mut e_in = 0usize;
        let pt;
        loop {
            if tri < 0 || tri >= self.n_tris {
                return 0;
            }
            let tu = tri as usize;
            let tp = self.tri_prim(tu);
            let v = tp.pt;
            if (mask[tu >> 5] >> (tu & 31)) & 1 != 0 {
                let p = v[e_in];
                let q = v[INC[e_in]];
                let e = Vec3::new(q.x - p.x, q.y - p.y, q.z - p.z);
                let t = (((c.x - p.x) * e.x + (c.y - p.y) * e.y) + (c.z - p.z) * e.z)
                    / ((e.x * e.x + e.y * e.y) + e.z * e.z);
                let a = ((t + 1.0) - (1.0 - t).abs()) * 0.5;
                let b = ((-a).abs() + a) * 0.5;
                pt = Vec3::new(b * e.x + p.x, e.y * b + p.y, e.z * b + p.z);
                break;
            }
            mask[tu >> 5] |= 1 << (tu & 31);
            let n = tp.n;
            let mut fails = [0usize; 4];
            let mut cnt = 0usize;
            for i in 0..3 {
                fails[cnt] = i;
                let e = v[INC[i]] - v[i];
                cnt += edge_side(c, v[i], e, n) as usize;
            }
            if cnt == 0 {
                let d = ((v[0].y - c.y) * n.y + (v[0].x - c.x) * n.x) + (v[0].z - c.z) * n.z;
                pt = Vec3::new(n.x * d + c.x, n.y * d + c.y, n.z * d + c.z);
                break;
            }
            let topo = self.topology[tu];
            let nb0 = topo[fails[0]];
            let bit = if nb0 >= 0 && nb0 < self.n_tris {
                (mask[nb0 as usize >> 5] >> (nb0 & 31)) & 1
            } else {
                0
            };
            let sel = if cnt >= 2 { bit as usize } else { 0 };
            let nb = topo[fails[sel]];
            if nb < 0 || nb >= self.n_tris {
                return 0;
            }
            let tn = self.topology[nb as usize];
            let d1 = tn[1].wrapping_sub(tri);
            let d2 = tn[2].wrapping_sub(tri);
            let s1 = (d1.wrapping_sub(1) ^ d1) >> 31;
            let s2 = (d2.wrapping_sub(1) ^ d2) >> 31;
            e_in = ((!s2 & (s1 & 1)) | (s2 & 2)) as usize;
            tri = nb;
        }
        let (dx, dy, dz) = (pt.x - c.x, pt.y - c.y, pt.z - c.z);
        if ((dy * dy + dx * dx) + dz * dz) * (1 - hit) as f32 > rsep * rsep {
            return 0;
        }
        let (ux, uy, uz) = (c.x - pt.x, c.y - pt.y, c.z - pt.z);
        let l2 = (uy * uy + ux * ux) + uz * uz;
        let nn = if !(l2 > f32::from_bits(0x1E3C_E508)) {
            Vec3::new(0.0, 0.0, 1.0)
        } else {
            let k = 1.0 / l2.sqrt();
            Vec3::new(ux * k, uy * k, uz * k)
        };
        let s = (1 - 2 * hit) as f32;
        out.n = Vec3::new(s * nn.x, nn.y * s, nn.z * s);
        out.i_feature[0] = tri;
        out.pt = pt;
        1
    }
}
