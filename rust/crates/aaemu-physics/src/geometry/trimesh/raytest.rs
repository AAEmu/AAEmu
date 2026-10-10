//! `CTriMesh` ray path (P1c-ray): the ray branch of slot 10 `Intersect` (0x351C9910) with its
//! sorted-list helpers sub_35199B80 (union) / sub_351BD620 (intersection), and slot 48
//! `PreparePrimitive` (0x351C2A60). The ray grid itself (slot 22 `PrepareForRayTest`
//! 0x351D7C40, builder sub_351C8B10, `+176..+392`) is [`super::inside::RayGrid`].
//!
//! # Slot 10, ray branch
//!
//! Taken when the other geometry is a ray (slot 1 = 3) and the grid is built (`+476 > 0`);
//! otherwise — and when the ray's cell range along a layer's x axis spans more than two cells
//! — the generic [`intersect`] runs. The ray is moved into the mesh frame (`gwdB`, then
//! `gwdA⁻¹` scaled by `1/scale`); the optional start triangle `gwdA+88 − 1` is tried first (a
//! hit there forces "first contact only"); then the candidates of **every built layer** —
//! the union of the cells in the ray's `[min, max]` cell range of that layer, intersected
//! across layers, 1024 entries at most ([`TriMesh::ray_grid_candidates`]) — are tested in
//! ascending order with INTERSECT ray–tri (sub_3515AB20, tolerance 0). Each hit becomes a
//! contact:
//!
//! * no unprojection (`mode = −1`: no `ip`, or the ray's priority (0) below the mesh's (1) —
//!   **always** for engine rays): `t` = distance along the world `ndir`, `pt` / `n` in world
//!   space, `dir = 0`, features `{0x40, 0x20}`;
//! * with unprojection (ray priority ≥ mesh priority; only reachable by poking the ray's
//!   `+20`): `UNPROJECT[mode][tri][ray]` pushes the ray out, then the walk hops across up to
//!   three neighbouring triangles (moved by the accumulated `t`; rotated in mode 1 with the
//!   MSVCR100 double `cos` / `sin`) while the result's feature is an edge or a vertex.
//!
//! The contacts are selection-sorted by `t`, largest first, and added to the per-caller
//! contact count.

// The NaN-aware comparisons mirror the original's `comiss` branches.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::inside::RayGridLayer;
use super::intersect::{intersect, IntersectExtra, IntersectResult, Tables};
use super::protocol::{BorderKind, CallerArena, GeomProtocol, CONTACT_POOL};
use super::TriMesh;
use crate::collision::contact::{
    GeomContact, IntersectionParams, PrimInters, UnprojParams, UnprojResult,
};
use crate::collision::pairs::tri_pairs::intersect_ray_tri;
use crate::collision::swept::common::{normalize_in_place, rotate_vec};
use crate::collision::swept::poly::crt::{atan2, cos, sin};
use crate::geometry::primitives::{Primitive, RayPrim, Triangle};
use crate::geometry::GeomWorldData;
use crate::ids::GeomType;
use crate::math::Vec3;

/// `dword_3520A770`: `(i + 1) mod 3`.
const INC: [usize; 3] = [1, 2, 0];
/// `dword_3520A780`: `(i + 2) mod 3`.
const DEC: [usize; 3] = [2, 0, 1];
/// `-0x4B400000`, the round-magic bias as an integer.
const MAGIC_BITS: i32 = 0x4B40_0000;
/// Capacity of the candidate lists of slot 10 (`v451`, 3 × 1024 ints).
const LIST_CAP: usize = 1024;

/// The bits of `x + 12582912.0` (kept as bits where the original subtracts them directly).
#[inline]
fn mbits(x: f32) -> i32 {
    (x + 12_582_912.0).to_bits() as i32
}

/// `x & (x >> 31)` = `min(x, 0)`.
#[inline]
pub(crate) fn mn0(x: i32) -> i32 {
    x & (x >> 31)
}

/// Sign bit as 0/1 (`-0.0` and negative NaNs count).
#[inline]
fn sb(x: f32) -> u32 {
    x.to_bits() >> 31
}

/// sub_35199B80: union of two ascending lists without duplicates into `out` (at most `cap`
/// entries). An exhausted list reads as `list[0] + 0x40000000` (or `0x40000000` when empty
/// from the start). Returns the number written.
fn merge_union(a: &[i32], na: i32, b: &[i32], nb: i32, out: &mut [i32], cap: i32) -> i32 {
    let mut ma: i32 = if na.wrapping_neg() < 0 { -1 } else { 0 };
    let mut mbm: i32 = if nb.wrapping_neg() < 0 { -1 } else { 0 };
    let a0 = ma != 0;
    let b0 = mbm != 0;
    let (mut i, mut j, mut k) = (0i32, 0i32, 0i32);
    if !(cap.wrapping_neg() < 0 && (ma | mbm) != 0) {
        return 0;
    }
    let mut v16 = cap.wrapping_neg();
    loop {
        v16 = v16.wrapping_add(1);
        let ia = (ma & i) as usize;
        let ib = (mbm & j) as usize;
        let ra = if a0 { a[ia] } else { 0 };
        let rb = if b0 { b[ib] } else { 0 };
        let va = ra.wrapping_add(!ma & 0x4000_0000);
        let vb = rb.wrapping_add(!mbm & 0x4000_0000);
        let m = mn0(va.wrapping_sub(vb));
        let v = vb.wrapping_add(m);
        out[k as usize] = v;
        k += 1;
        j += ((m.wrapping_neg().wrapping_sub(1)) as u32 >> 31) as i32;
        i += ((va.wrapping_sub(v).wrapping_sub(1)) as u32 >> 31) as i32;
        mbm = j.wrapping_sub(nb) >> 31;
        ma = i.wrapping_sub(na) >> 31;
        if !(v16 < 0 && (ma | mbm) != 0) {
            break;
        }
    }
    k
}

/// sub_351BD620: intersection of two ascending lists into `out`; returns the count.
fn intersect_sorted(a: &[i32], na: i32, b: &[i32], nb: i32, out: &mut [i32]) -> i32 {
    let (mut i, mut j, mut k) = (0i32, 0i32, 0i32);
    if na.wrapping_neg() & nb.wrapping_neg() >= 0 {
        return 0;
    }
    loop {
        let x = a[i as usize];
        let y = b[j as usize];
        let d = x.wrapping_sub(y);
        out[k as usize] = x;
        let eq = -((d.wrapping_sub(1) ^ d) >> 31);
        k += eq;
        i += ((d as u32) >> 31) as i32 + eq;
        j += ((y.wrapping_sub(x) as u32) >> 31) as i32 + eq;
        if i.wrapping_sub(na) & j.wrapping_sub(nb) >= 0 {
            break;
        }
    }
    k
}

impl TriMesh {
    /// Vertex `idx[3·tri + k]`.
    #[inline]
    fn rt_vertex(&self, tri: usize, k: usize) -> Vec3 {
        self.vertices[self.indices[3 * tri + k] as usize]
    }

    /// Triangle `tri` in the mesh frame (`{pt[3], n}`).
    #[inline]
    pub(crate) fn tri_prim(&self, tri: usize) -> Triangle {
        Triangle {
            pt: [
                self.rt_vertex(tri, 0),
                self.rt_vertex(tri, 1),
                self.rt_vertex(tri, 2),
            ],
            n: self.normals[tri],
        }
    }

    /// Slot 48 `PreparePrimitive(gwd, prim**, iCaller)` (0x351C2A60): the **first** triangle
    /// in world space (`R·v·scale + offset`; the normal rotated, not scaled). Returns type 1.
    #[must_use]
    pub fn prepare_primitive_slot48(&self, gwd: &GeomWorldData) -> Triangle {
        let r = &gwd.r.m;
        let s = gwd.scale;
        let o = gwd.offset;
        let w = |v: Vec3| {
            Vec3::new(
                ((v.y * r[0][1] + v.x * r[0][0]) + v.z * r[0][2]) * s + o.x,
                ((v.y * r[1][1] + v.x * r[1][0]) + v.z * r[1][2]) * s + o.y,
                ((v.y * r[2][1] + v.x * r[2][0]) + v.z * r[2][2]) * s + o.z,
            )
        };
        let n = self.normals[0];
        Triangle {
            pt: [
                w(self.rt_vertex(0, 0)),
                w(self.rt_vertex(0, 1)),
                w(self.rt_vertex(0, 2)),
            ],
            n: Vec3::new(
                (n.x * r[0][0] + n.y * r[0][1]) + n.z * r[0][2],
                (n.x * r[1][0] + n.y * r[1][1]) + n.z * r[1][2],
                (n.x * r[2][0] + n.y * r[2][1]) + n.z * r[2][2],
            ),
        }
    }

    /// The candidate triangles of the ray `(o, d)` (mesh frame) from the ray grid, as slot 10
    /// computes them: per layer the union of the cells in the ray's cell range, intersected
    /// across layers (1024 entries at most per list). `None` when a layer's x range spans more
    /// than two cells (slot 10 then falls back to the generic `Intersect`); an empty list when
    /// the grid is not built.
    #[must_use]
    pub fn ray_grid_candidates(&self, o: Vec3, d: Vec3) -> Option<Vec<i32>> {
        let mut bufs = vec![vec![0i32; LIST_CAP]; 3];
        let mut cnt = [0i32; 4];
        let mut cur = 0usize;
        let layers: &[RayGridLayer] = self.ray_grid.as_ref().map_or(&[], |g| &g.layers);
        let nl = (self.ray_grid_ready.max(0) as usize).min(layers.len());
        for (g, gr) in layers.iter().take(nl).enumerate() {
            let (gx, gy, go) = (gr.axis_x, gr.axis_y, gr.origin);
            let (dx, dy, dz) = (o.x - go.x, o.y - go.y, o.z - go.z);
            let v115 = (dx * gy.x + dy * gy.y) + dz * gy.z;
            let v116 = (dx * gx.x + dy * gx.y) + dz * gx.z;
            let v117 = (((d.x * gx.x + d.y * gx.y) + d.z * gx.z) + v116) * gr.rstep[0];
            let l118 = mbits(v116 * gr.rstep[0] - 0.5);
            let v119 = (((d.x * gy.x + d.y * gy.y) + d.z * gy.z) + v115) * gr.rstep[1] - 0.5;
            let l328 = mbits(v115 * gr.rstep[1] - 0.5);
            let l365 = mbits(v119);
            let l355 = mbits(v117 - 0.5);
            let w = i32::wrapping_sub;
            let v120 = mn0(w(l328, l365));
            let v121 = mn0(w(l118, l355));
            let v122 = w(w(l118, MAGIC_BITS), v121);
            let v123 = mn0(v121.wrapping_add(w(l355, MAGIC_BITS)).wrapping_neg());
            let y_hi0 = w(w(l328, MAGIC_BITS), v120);
            let y_hi = y_hi0.wrapping_add(mn0(w(w(gr.size[1], 1), y_hi0)));
            let x_lo = v123.wrapping_neg();
            let y_lo = mn0(v120.wrapping_add(w(l365, MAGIC_BITS)).wrapping_neg()).wrapping_neg();
            let x_hi = v122.wrapping_add(mn0(w(w(gr.size[0], 1), v122)));
            if w(x_lo, x_hi).wrapping_add(1) < 0 {
                return None;
            }
            let mut a = INC[cur];
            let mut b = INC[a];
            cnt[a] = 0;
            let mut iy = y_lo;
            while iy <= y_hi {
                let mut cell = x_lo
                    .wrapping_mul(gr.stride[0])
                    .wrapping_add(iy.wrapping_mul(gr.stride[1]));
                let mut ix = x_lo;
                while ix <= x_hi {
                    std::mem::swap(&mut a, &mut b);
                    let tris = layer_cell(gr, cell);
                    let (src, dst) = two_mut(&mut bufs, b, a);
                    cnt[a] =
                        merge_union(tris, tris.len() as i32, src, cnt[b], dst, LIST_CAP as i32);
                    cell = cell.wrapping_add(gr.stride[0]);
                    ix += 1;
                }
                iy += 1;
            }
            if g == 0 {
                cur = a;
            } else {
                let mut tmp = vec![0i32; LIST_CAP];
                cnt[b] = intersect_sorted(&bufs[cur], cnt[cur], &bufs[a], cnt[a], &mut tmp);
                bufs[b] = tmp;
                cur = b;
            }
        }
        Some(bufs[cur][..cnt[cur].max(0) as usize].to_vec())
    }
}

/// The triangles of cell `cell` of a layer (`tris[cells[c] .. cells[c + 1]]`), empty when
/// out of range (the original does not range-check; slot 10 clamps the indices first).
#[must_use]
pub fn layer_cell(l: &RayGridLayer, cell: i32) -> &[i32] {
    let c = cell as usize;
    if cell < 0 || c + 1 >= l.cells.len() {
        return &[];
    }
    let (a, b) = (l.cells[c], l.cells[c + 1]);
    if a < 0 || b < a || b as usize > l.tris.len() {
        return &[];
    }
    &l.tris[a as usize..b as usize]
}

/// Two distinct mutable elements of `v`: (`v[i]`, `v[j]`).
fn two_mut(v: &mut [Vec<i32>], i: usize, j: usize) -> (&Vec<i32>, &mut Vec<i32>) {
    assert_ne!(i, j);
    if i < j {
        let (l, r) = v.split_at_mut(j);
        (&l[i], &mut r[0])
    } else {
        let (l, r) = v.split_at_mut(i);
        (&r[0], &mut l[j])
    }
}

/// `R·v` with the association `(v.y·R[i][1] + v.x·R[i][0]) + v.z·R[i][2]`.
#[inline]
fn rot(r: &[[f32; 3]; 3], v: Vec3) -> Vec3 {
    Vec3::new(
        (v.y * r[0][1] + v.x * r[0][0]) + v.z * r[0][2],
        (v.y * r[1][1] + v.x * r[1][0]) + v.z * r[1][2],
        (v.y * r[2][1] + v.x * r[2][0]) + v.z * r[2][2],
    )
}

/// `Rᵀ·v`: `x = (v.y·R10 + v.x·R00) + v.z·R20`, `y = (v.x·R01 + v.y·R11) + v.z·R21`,
/// `z = (v.x·R02 + v.y·R12) + v.z·R22`.
#[inline]
fn rot_t(r: &[[f32; 3]; 3], v: Vec3) -> Vec3 {
    Vec3::new(
        (v.y * r[1][0] + v.x * r[0][0]) + v.z * r[2][0],
        (v.x * r[0][1] + v.y * r[1][1]) + v.z * r[2][1],
        (v.x * r[0][2] + v.y * r[1][2]) + v.z * r[2][2],
    )
}

/// The ray-branch state of one slot-10 call.
struct RayCall<'a> {
    mesh: &'a TriMesh,
    tables: Tables<'a>,
    ga: GeomWorldData,
    gb: Option<GeomWorldData>,
    ndir: Vec3,
    ray: RayPrim,
    mode: i32,
    p: UnprojParams,
}

/// What happened to one candidate triangle.
#[allow(clippy::large_enum_variant)]
enum Hit {
    /// No contact (`LABEL_93`: next candidate).
    Skip,
    /// A contact.
    Contact(GeomContact),
}

impl RayCall<'_> {
    fn world_pt(&self, p: Vec3) -> Vec3 {
        let r = &self.ga.r.m;
        let s = self.ga.scale;
        let o = self.ga.offset;
        let v = rot(r, p);
        Vec3::new(v.x * s + o.x, v.y * s + o.y, v.z * s + o.z)
    }

    fn unproject(&mut self, a: &Triangle, fa: i32, fb: i32, out: &mut UnprojResult) -> i32 {
        if !(0..=1).contains(&self.mode) {
            // the original indexes past the two UNPROJECT planes
            return 0;
        }
        self.tables.unproj.unproject(
            self.mode as usize,
            &mut self.p,
            &Primitive::Triangle(*a),
            fa,
            &Primitive::Ray(self.ray),
            fb,
            out,
            None,
        )
    }

    /// `LABEL_54`: the contact of a ray–tri hit on `tri` (`inters` from sub_3515AB20).
    fn hit(&mut self, tri: usize, inters: &PrimInters) -> Hit {
        let m = self.mesh;
        let r = self.ga.r.m;
        let mut c = GeomContact::default();
        let mut cur = tri as i32; // v353
        if self.mode < 0 {
            c.pt = self.world_pt(inters.pt[0]);
            let dw = match &self.gb {
                Some(gb) => {
                    let b = &gb.r.m;
                    let n = self.ndir;
                    Vec3::new(
                        (n.y * b[0][1] + n.x * b[0][0]) + n.z * b[0][2],
                        (n.x * b[1][0] + n.y * b[1][1]) + n.z * b[1][2],
                        (n.x * b[2][0] + n.y * b[2][1]) + n.z * b[2][2],
                    )
                }
                None => self.ndir,
            };
            let o = self.ray.origin;
            let dl = Vec3::new(
                inters.pt[0].x - o.x,
                inters.pt[0].y - o.y,
                inters.pt[0].z - o.z,
            );
            let w = rot(&r, dl);
            let t = ((w.y * dw.y + dw.x * w.x) + w.z * dw.z) * self.ga.scale;
            c.t = f64::from(t);
            c.n = rot(&r, inters.n);
            c.dir = Vec3::ZERO;
            c.i_prim[0] = cur;
            c.i_feature = [0x40, 0x20];
            c.i_unproj_mode = 0;
        } else {
            let tp = m.tri_prim(tri);
            let rd = self.ray.dir;
            let a = self.p.dir;
            if (a.y * a.y + a.x * a.x) + a.z * a.z == 0.0
                || ((a.y * a.y + a.x * a.x) + a.z * a.z).is_nan()
            {
                let n = tp.n;
                if (n.y * rd.y + n.x * rd.x) + n.z * rd.z > 0.0 {
                    return Hit::Skip;
                }
                let mut ax = Vec3::new(
                    n.y * rd.z - n.z * rd.y,
                    n.z * rd.x - n.x * rd.z,
                    n.x * rd.y - n.y * rd.x,
                );
                if self.mode == 0 {
                    ax = Vec3::new(
                        ax.y * rd.z - ax.z * rd.y,
                        rd.x * ax.z - rd.z * ax.x,
                        rd.y * ax.x - rd.x * ax.y,
                    );
                }
                if !((ax.y * ax.y + ax.x * ax.x) + ax.z * ax.z >= f32::from_bits(0x3B03_126F)) {
                    let v334 = ((rd.y * rd.y + rd.x * rd.x) + rd.z * rd.z)
                        * f32::from_bits(0x3F4F_5C28)
                        - rd.x * rd.x;
                    let k = sb(v334) as usize;
                    let mut av = [ax.x, ax.y, ax.z];
                    let dv = [rd.x, rd.y, rd.z];
                    av[k] = 0.0;
                    let (de, ie) = (DEC[k], INC[k]);
                    let v164 = dv[ie];
                    av[ie] = dv[de];
                    av[de] = -v164;
                    ax = Vec3::new(av[0], av[1], av[2]);
                }
                let l2 = (ax.y * ax.y + ax.x * ax.x) + ax.z * ax.z;
                ax = if !(l2 > f32::from_bits(0x1E3C_E508)) {
                    Vec3::new(0.0, 0.0, 1.0)
                } else {
                    let k = 1.0 / l2.sqrt();
                    Vec3::new(ax.x * k, ax.y * k, ax.z * k)
                };
                self.p.dir = ax;
            }
            let mut res = UnprojResult::default();
            if self.unproject(&tp, -1, -1, &mut res) == 0 {
                return Hit::Skip;
            }
            let mut y = res.t;
            let mut xc = res.cos_t;
            if self.mode != 0 {
                y = atan2(y, xc);
            }
            let mut acc = 0.0f64;
            c.i_prim[0] = cur;
            let mut step = y;
            let mut mm = self.mode;
            let mut cnt = 0i32;
            let mut feat = res.i_feature;
            let mut rpt = res.pt;
            let mut rn = res.n;
            let tmax = f64::from(self.p.tmax);
            while step > 0.0 {
                if cnt >= 3 || feat[0] & 0x80 == 0 {
                    break;
                }
                if mm != 0 {
                    // kept: the step is already an angle and goes through atan2 again
                    y = atan2(step, xc);
                    step = y;
                }
                acc += step;
                if acc > tmax {
                    // kept: the step is counted twice in `t` below
                    break;
                }
                let nb = m.topology[cur as usize][(feat[0] & 0x1F) as usize];
                if nb < 0 {
                    // kept: the step is counted twice in `t` below
                    break;
                }
                let nbu = nb as usize;
                let mut tn = m.tri_prim(nbu);
                let ctr = self.p.center;
                let ax = self.p.dir;
                if mm != 0 {
                    let cs = cos(acc) as f32;
                    let sn = sin(acc) as f32;
                    for v in &mut tn.pt {
                        let d = Vec3::new(v.x - ctr.x, v.y - ctr.y, v.z - ctr.z);
                        let h = (ax.x * d.x + d.y * ax.y) + d.z * ax.z;
                        let ry = d.y - ax.y * h;
                        let rz = d.z - ax.z * h;
                        let rx = d.x - ax.x * h;
                        *v = Vec3::new(
                            ctr.x + ((cs * rx + (rz * ax.y - ry * ax.z) * sn) + ax.x * h),
                            (ax.y * h + (ry * cs + (ax.z * rx - ax.x * rz) * sn)) + ctr.y,
                            (ax.z * h + (rz * cs + (ax.x * ry - ax.y * rx) * sn)) + ctr.z,
                        );
                    }
                    tn.n = rotate_vec(tn.n, ax, cs, sn);
                } else {
                    let tf = acc as f32;
                    let off = Vec3::new(ax.x * tf, ax.y * tf, ax.z * tf);
                    for v in &mut tn.pt {
                        *v = Vec3::new(off.x + v.x, off.y + v.y, off.z + v.z);
                    }
                }
                let tp_nb = m.topology[nbu];
                let d1 = tp_nb[1].wrapping_sub(cur);
                let d2 = tp_nb[2].wrapping_sub(cur);
                let s1 = ((d1 ^ d1.wrapping_sub(1)) as u32 >> 31) as i32;
                let s2 = ((d2 ^ d2.wrapping_sub(1)) as u32 >> 31) as i32;
                let fa = ((s2 << 1) & 2) | (s1 & 1 & !s2) | 0xA0;
                y = 0.0;
                cur = nb;
                let mut r2 = UnprojResult::default();
                if self.unproject(&tn, fa, 0xA0, &mut r2) != 0 {
                    feat = r2.i_feature;
                    y = r2.t;
                    xc = r2.cos_t;
                    rpt = r2.pt;
                    rn = r2.n;
                    c.i_prim[0] = cur;
                    mm = self.mode;
                    let small = if mm != 0 {
                        y = atan2(y, xc);
                        step = y;
                        sb((y - 0.001) as f32)
                    } else {
                        step = y;
                        sb((y - f64::from(m.min_vtx_dist)) as f32)
                    } as i32;
                    cnt = (small.wrapping_neg() & cnt) + small;
                } else {
                    mm = self.mode;
                    step = y;
                }
            }
            let k = (1 - mm) as f32 * self.ga.scale + mm as f32;
            c.t = f64::from(k) * (acc + step);
            let s = self.ga.scale;
            let o = self.ga.offset;
            c.pt = Vec3::new(
                ((rpt.x * r[0][0] + rpt.y * r[0][1]) + rpt.z * r[0][2]) * s + o.x,
                ((rpt.x * r[1][0] + rpt.y * r[1][1]) + rpt.z * r[1][2]) * s + o.y,
                ((rpt.x * r[2][0] + rpt.y * r[2][1]) + rpt.z * r[2][2]) * s + o.z,
            );
            c.n = rot(&r, rn);
            c.dir = rot(&r, self.p.dir);
            c.i_feature = feat;
            c.i_unproj_mode = self.mode;
        }
        c.vel = 0.0;
        c.border = vec![c.pt];
        c.area = None;
        c.id = [
            m.mats.as_ref().map_or(-1, |v| i32::from(v[cur as usize])),
            -1,
        ];
        c.i_prim[1] = 0;
        c.i_node = [c.i_prim[0] + 1, 0];
        c.center = c.pt;
        Hit::Contact(c)
    }
}

impl TriMesh {
    /// `CTriMesh` slot 10 `Intersect` (0x351C9910) with the same arguments as the generic
    /// [`intersect`] (`self` is A, the mesh). The ray branch (module docs) runs when `b` is a
    /// ray and the ray grid is built; everything else, and rays whose cell range along a
    /// grid's x axis spans more than two cells, go to [`intersect`].
    ///
    /// Kept from the original (mode ≥ 0 only, i.e. a ray with priority ≥ 1):
    /// * mode 1 runs every accumulated step through `atan2(step, cos)` a second time;
    /// * a walk stopped by `acc > tmax` or by an open edge counts its last step twice in `t`;
    /// * `tmax` is π/2 in mode 1 (compared with the accumulated angle), `ip.max_unproj` in
    ///   mode 0;
    /// * `id[0]` is the material of the last triangle walked to even when its unprojection
    ///   failed (`iPrim[0]` / `iNode[0]` stay at the last successful one).
    ///
    /// Deviations: a null `gwdA` (a crash in the original) is the identity; a start triangle
    /// (`extra.start_node[0]`) whose hit is skipped in mode ≥ 0 continues with garbage list
    /// indices in the original (out-of-frame reads) — the port returns no contacts there;
    /// an unprojection mode other than 0/1 (beyond the original's tables) finds nothing.
    #[allow(clippy::too_many_arguments)]
    pub fn intersect_slot10(
        &self,
        tables: Tables<'_>,
        arena: &mut CallerArena,
        b: &dyn GeomProtocol,
        gwd_a: Option<&mut GeomWorldData>,
        gwd_b: Option<&GeomWorldData>,
        ip: Option<&mut IntersectionParams>,
        extra: IntersectExtra,
    ) -> IntersectResult {
        let ray = match b.proto_ray() {
            Some(r) if b.proto_type() == GeomType::Ray && self.ray_grid_ready > 0 => r,
            _ => return intersect(tables, arena, self, b, gwd_a, gwd_b, ip, extra),
        };
        let ga = gwd_a.as_deref().copied().unwrap_or_default();
        let rs = if ga.scale != 1.0 { 1.0 / ga.scale } else { 1.0 };
        let (mut o, mut d) = (ray.0.origin, ray.0.dir);
        if let Some(gb) = gwd_b {
            let r = &gb.r.m;
            let s = gb.scale;
            let x = (o.x * r[0][0] + o.y * r[0][1]) + o.z * r[0][2];
            let y = (o.x * r[1][0] + o.y * r[1][1]) + o.z * r[1][2];
            let z = (o.x * r[2][0] + o.y * r[2][1]) + o.z * r[2][2];
            o = Vec3::new(
                x * s + gb.offset.x,
                s * y + gb.offset.y,
                s * z + gb.offset.z,
            );
            d = Vec3::new(
                ((r[0][1] * d.y + r[0][0] * d.x) + r[0][2] * d.z) * s,
                s * ((r[1][1] * d.y + r[1][0] * d.x) + r[1][2] * d.z),
                s * ((r[2][1] * d.y + r[2][0] * d.x) + r[2][2] * d.z),
            );
        }
        let ra = ga.r.m;
        let off = ga.offset;
        let lo = rot_t(&ra, Vec3::new(o.x - off.x, o.y - off.y, o.z - off.z));
        let lo = Vec3::new(lo.x * rs, lo.y * rs, lo.z * rs);
        let ld = rot_t(&ra, d);
        let ld = Vec3::new(ld.x * rs, ld.y * rs, ld.z * rs);
        let ray_l = RayPrim {
            origin: lo,
            dir: ld,
        };
        let mut p = UnprojParams {
            mode: -1,
            dir: Vec3::ZERO,
            center: Vec3::ZERO,
            vel: 0.0,
            tmax: 0.0,
            tmin: 0.0,
            min_pt_dist: 0.0,
            max_cos: f32::from_bits(0x3DCC_CCCD),
            flag: 0,
        };
        let ip_axis = ip.as_deref().map_or(Vec3::ZERO, |p| p.axis_of_rotation);
        let (first_only, keep) = match ip.as_deref() {
            Some(ipr) => {
                if b.proto_priority() >= self.priority {
                    let c = ipr.center_of_rotation;
                    let cl = rot_t(&ra, Vec3::new(c.x - off.x, c.y - off.y, c.z - off.z));
                    p.center = Vec3::new(cl.x * rs, cl.y * rs, cl.z * rs);
                    p.mode = ipr.i_unprojection_mode;
                    let a = if p.mode != 0 {
                        p.tmax = f32::from_bits(0x3FC9_0FDB);
                        ipr.axis_of_rotation
                    } else {
                        let vb = gwd_b.map_or(Vec3::ZERO, |g| g.v);
                        let mut a = Vec3::new(vb.x - ga.v.x, vb.y - ga.v.y, vb.z - ga.v.z);
                        if (a.y * a.y + a.x * a.x) + a.z * a.z > 0.0 {
                            normalize_in_place(&mut a);
                        }
                        p.tmax = ipr.max_unproj;
                        a
                    };
                    p.dir = rot_t(&ra, a);
                    p.min_pt_dist = self.min_vtx_dist;
                }
                (extra.first_only, ipr.keep_prev_contacts)
            }
            None => (false, false),
        };
        if !keep {
            arena.n_contacts = 0;
        }
        let mut call = RayCall {
            mesh: self,
            tables,
            ga,
            gb: gwd_b.copied(),
            ndir: ray.1,
            ray: ray_l,
            mode: p.mode,
            p,
        };
        let mut contacts: Vec<GeomContact> = Vec::new();
        let mut inters = PrimInters::default();
        let st = extra.start_node[0];
        let mut done = false;
        if (st.wrapping_sub(1) as u32) < self.n_tris as u32 {
            let tri = (st - 1) as usize;
            if intersect_ray_tri(&ray_l, &self.tri_prim(tri), &mut inters) {
                match call.hit(tri, &inters) {
                    Hit::Contact(c) => contacts.push(c),
                    Hit::Skip => {
                        // the original continues the candidate loop with garbage indices
                    }
                }
                done = true;
            }
        }
        if !done {
            let Some(cands) = self.ray_grid_candidates(lo, ld) else {
                return intersect(tables, arena, self, b, gwd_a, gwd_b, ip, extra);
            };
            for &t in &cands {
                if (contacts.len() as i32 + arena.n_contacts) as u32 >= CONTACT_POOL as u32 {
                    break;
                }
                let tri = t as usize;
                if !intersect_ray_tri(&ray_l, &self.tri_prim(tri), &mut inters) {
                    continue;
                }
                if let Hit::Contact(c) = call.hit(tri, &inters) {
                    contacts.push(c);
                    if first_only {
                        break;
                    }
                }
            }
        }
        // selection sort, largest `t` first (sign of `(float)(t_m − t_j)`); whole records are
        // swapped, including the border pointer, which keeps pointing at the `pt` of the slot
        // the contact was created in (kept: after the sort a moved contact's border point is
        // the `pt` of whichever contact now occupies that slot)
        let n = contacts.len();
        let mut slot: Vec<usize> = (0..n).collect();
        for i in 0..n.saturating_sub(1) {
            let mut m = i;
            for j in i + 1..n {
                if sb((contacts[m].t - contacts[j].t) as f32) != 0 {
                    m = j;
                }
            }
            if m != i {
                contacts.swap(i, m);
                slot.swap(i, m);
            }
        }
        let mut kinds = vec![BorderKind::Pt; n];
        for i in 0..n {
            if slot[i] != i {
                contacts[i].border = vec![contacts[slot[i]].pt];
                kinds[i] = BorderKind::Pool;
            }
        }
        arena.n_contacts += n as i32;
        IntersectResult {
            border_kinds: kinds,
            contacts,
            ip_convex: 0,
            // the ray branch leaves `ip+16` alone
            ip_axis,
        }
    }
}
