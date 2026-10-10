//! `CTriMesh::PointInsideStatus` (slot 8 0x351DB380, slot 61 0x351C8600) and the ray grid it
//! reads: slot 22 `PrepareForRayTest` (0x351D7C40) with the grid builder sub_351C8B10.
//!
//! # The ray grid (`CTriMesh +176..+392`)
//!
//! Slot 22 builds one or two [`RayGridLayer`]s. Each layer is a 2-D grid of cells over a plane
//! of the mesh's bounding box (the tree's root box, slot 2 `GetBBox`); every cell lists the
//! triangles whose projection onto the plane overlaps the cell, so a ray cast along the layer's
//! third axis ([`RayGridLayer::axis_ray`]) only has to test the triangles of one cell.
//!
//! | object offset | content |
//! |---|---|
//! | `+176`, `+184` | per layer: cell start array (`n0·n1 + 1` ints, prefix sums) — [`RayGridLayer::cells`] |
//! | `+200`, `+208` | per layer: cell triangle list — [`RayGridLayer::tris`] |
//! | `+224 + 84·k` | per layer: the 84-byte record (the other [`RayGridLayer`] fields) |
//! | `+476` | number of layers built (`ray_grid_ready`) |
//!
//! Cell `(i, j)` (`i < n0` along [`RayGridLayer::axis_x`], `j < n1` along
//! [`RayGridLayer::axis_y`]) has index `i·stride[0] + j·stride[1]` (`stride = (1, n0)`) and
//! holds `tris[cells[c] .. cells[c + 1]]`, ascending triangle indices.
//!
//! The original guards the build with the mesh lock at `+8`; the port is single threaded and
//! keeps only the "already built" logic (`+476 > 0` → done; also `+476 < 0` → done without a
//! build, as the original).

use crate::collision::contact::PrimInters;
use crate::collision::pairs::tri_pairs::intersect_ray_tri;
use crate::geometry::primitives::{BoxPrim, RayPrim, Triangle};
use crate::math::Vec3;

use super::TriMesh;

/// `inc_mod3` (0x3520A770).
const INC3: [usize; 3] = [1, 2, 0];
/// `dec_mod3` (0x3520A780).
const DEC3: [usize; 3] = [2, 0, 1];

/// `12582912.0` (`1.5·2²³`), the rounding constant.
const MAGIC: f32 = 12_582_912.0;
/// Bits of [`MAGIC`].
const MAGIC_BITS: i32 = 0x4B40_0000;
/// `1e-5` (0x351EDB78).
const EPS_1E5: f32 = f32::from_bits(0x3727_C5AC);
/// `0.001` (0x351EDB8C).
const EPS_1E3: f32 = f32::from_bits(0x3A83_126F);
/// `1e-4` (0x351EDB90).
const EPS_1E4: f32 = f32::from_bits(0x38D1_B717);
/// `1e-10` (0x351EDBE8).
const EPS_1E10: f32 = f32::from_bits(0x2EDB_E6FF);
/// `1e20` (0x351EFB3C), slot 61's initial nearest-hit distance.
const FAR: f32 = f32::from_bits(0x60AD_78EC);

/// One layer of the ray grid: the 84-byte record at `+224 + 84·k` plus its two arrays.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct RayGridLayer {
    /// `+0` the grid's first axis (cell index `i`).
    pub axis_x: Vec3,
    /// `+12` the grid's second axis (cell index `j`).
    pub axis_y: Vec3,
    /// `+24` the ray axis (slot 61 casts along it).
    pub axis_ray: Vec3,
    // +36: never written by the builder (whatever the object held before).
    /// `+40` the grid's corner (cell `(0, 0)`'s low corner) in mesh coordinates.
    pub origin: Vec3,
    /// `+52` cell size along `axis_x`, `axis_y`.
    pub step: [f32; 2],
    /// `+60` `n / extent` (the reciprocal step as the builder computes it, not `1/step`).
    pub rstep: [f32; 2],
    /// `+68` cell counts `(n0, n1)`, each in `1..=64`.
    pub size: [i32; 2],
    /// `+76` index strides `(1, n0)`.
    pub stride: [i32; 2],
    /// `+176 + 8k`: `n0·n1 + 1` cell starts into [`tris`](Self::tris).
    pub cells: Vec<i32>,
    /// `+200 + 8k`: the triangles of every cell, cell after cell.
    pub tris: Vec<i32>,
}

impl RayGridLayer {
    /// The 20 dwords of the record (`+0..+36`, `+40..+84`; `+36` is not written).
    #[must_use]
    pub fn record_words(&self) -> [u32; 20] {
        let mut w = [0u32; 20];
        let vs = [self.axis_x, self.axis_y, self.axis_ray, self.origin];
        for (k, v) in vs.iter().enumerate() {
            w[3 * k..3 * k + 3].copy_from_slice(&v.to_bits());
        }
        w[12] = self.step[0].to_bits();
        w[13] = self.step[1].to_bits();
        w[14] = self.rstep[0].to_bits();
        w[15] = self.rstep[1].to_bits();
        w[16] = self.size[0] as u32;
        w[17] = self.size[1] as u32;
        w[18] = self.stride[0] as u32;
        w[19] = self.stride[1] as u32;
        w
    }
}

/// The mesh's ray grid (`CTriMesh +176..+392`, built by slot 22 `PrepareForRayTest`): one
/// layer, or two when the box is not flat along the first chosen axis (see
/// [`TriMesh::prepare_for_ray_test`]). Slot 61 reads only `layers[0]`.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct RayGrid {
    pub layers: Vec<RayGridLayer>,
}

/// Slot 61's out triangle (52 bytes): the triangle as `GetPrimitive` gives it plus its index
/// at `+48`.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct InsideTri {
    pub tri: Triangle,
    /// `+48` the triangle index.
    pub idx: i32,
}

impl InsideTri {
    /// The 13 dwords as the original writes them.
    #[must_use]
    pub fn words(&self) -> [u32; 13] {
        let mut w = [0u32; 13];
        for k in 0..3 {
            w[3 * k..3 * k + 3].copy_from_slice(&self.tri.pt[k].to_bits());
        }
        w[9..12].copy_from_slice(&self.tri.n.to_bits());
        w[12] = self.idx as u32;
        w
    }
}

/// `bits(x + 12582912) − 0x4B400000` (wrapping).
#[inline]
fn rb(x: f32) -> i32 {
    ((x + MAGIC).to_bits() as i32).wrapping_sub(MAGIC_BITS)
}

/// Sign bit as 0 / 1.
#[inline]
fn sbit(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// `(x & (x >> 31))` = `min(x, 0)`.
#[inline]
fn neg_part(x: i32) -> i32 {
    x & (x >> 31)
}

/// `min(a, b)` as `b + min(a − b, 0)` (wrapping).
#[inline]
fn min_b(a: i32, b: i32) -> i32 {
    b.wrapping_add(neg_part(a.wrapping_sub(b)))
}

/// `max(a, b)` as `a − min(a − b, 0)` (wrapping).
#[inline]
fn max_a(a: i32, b: i32) -> i32 {
    a.wrapping_sub(neg_part(a.wrapping_sub(b)))
}

/// `max(x, 0)` as `−min(−x, 0)` (wrapping).
#[inline]
fn max0(x: i32) -> i32 {
    neg_part(x.wrapping_neg()).wrapping_neg()
}

/// `−1 / 0 / +1` from the bits: `((b − 1) >> 31) + (b >> 31) + 1` (0 for `±0`).
#[inline]
fn sgn0(x: f32) -> i32 {
    let b = x.to_bits() as i32;
    (b.wrapping_sub(1) >> 31)
        .wrapping_add(b >> 31)
        .wrapping_add(1)
}

/// The box of sub_351C8B10's second argument: ray axis, centre, grid axes.
struct LayerFrame {
    axis_ray: Vec3,
    center: Vec3,
    axis_x: Vec3,
    axis_y: Vec3,
}

/// `dot` in the order the binary uses: `(a.y·b.y + a.x·b.x) + a.z·b.z`.
#[inline]
fn dot_yxz(a: Vec3, b: Vec3) -> f32 {
    (a.y * b.y + a.x * b.x) + a.z * b.z
}

impl TriMesh {
    /// Slot 22 `PrepareForRayTest` (0x351D7C40): builds the ray grid ([`RayGrid`]) unless
    /// `+476` (`ray_grid_ready`) is non-zero. Returns `false` (and builds nothing) when the
    /// mesh has no tree (the original logs "Getting bbox failed") or, for `raylen > 0`, when
    /// the mesh has more than `6·(n0·n1 + n2·(n1 + n0))` triangles, `nk = round(2·sizek/raylen)`
    /// (pair products above 4096 clamped to 64 per factor) — a ray that short is not worth a grid.
    ///
    /// The box is the tree's root box with half-sizes raised to `|raylen|/2`. With triangles,
    /// the box axis most aligned with triangle 0's normal (ties to the lower axis) is chosen
    /// as `k`, and rows `k` and `k+1` are negated when that normal points against row `k`
    /// (keeping the basis handedness). The first layer's ray axis is the box axis with the
    /// largest face area, picked by sign tests; a second layer (ray axis = next axis) is built
    /// unless `|raylen| − 2·(original half-size along the first ray axis) ≥ 0`. Slot 61 passes
    /// `raylen = 0`, which builds two layers for any box with a non-zero extent.
    pub fn prepare_for_ray_test(&mut self, raylen: f32) -> bool {
        if self.ray_grid_ready != 0 {
            // `> 0` returns before taking the lock; `< 0` returns after it — both `1`.
            return true;
        }
        let Some(tree) = self.tree.as_ref() else {
            return false;
        };
        let bx: BoxPrim = tree.as_dyn().bbox();
        let n_tris = self.n_tris;
        let s = sbit(raylen);
        let r = raylen * ((1 - 2 * s) as f32);
        let h = r * 0.5;
        let orig = bx.size;
        let mut size = [0f32; 3];
        for (k, sz) in size.iter_mut().enumerate() {
            let v = orig[k];
            *sz = ((v - h).abs() + (h + v)) * 0.5;
        }
        let mut rows = [bx.basis.row(0), bx.basis.row(1), bx.basis.row(2)];
        let (k, sgn) = if n_tris <= 0 {
            (1usize, 1i32)
        } else {
            let n = self.normals[0];
            let d = |r: Vec3| dot_yxz(r, n);
            let mut k = sbit(d(rows[0]).abs() - d(rows[1]).abs());
            let m = sbit(d(rows[k as usize]).abs() - d(rows[2]).abs());
            k += (-m) & (2 - k);
            let v = d(rows[k as usize]);
            (k as usize, crate::math::sgnnz(v))
        };
        let sf = sgn as f32;
        rows[k] = Vec3::new(rows[k].x * sf, rows[k].y * sf, rows[k].z * sf);
        let k1 = INC3[k];
        rows[k1] = Vec3::new(rows[k1].x * sf, rows[k1].y * sf, rows[k1].z * sf);
        let mut rinv = 0.0f32;
        // `comiss` + `jbe`: NaN skips.
        if r > 0.0 {
            rinv = 1.0 / r;
            let mut n = [0i32; 3];
            for (a, nv) in n.iter_mut().enumerate() {
                *nv = rb(((size[a] * 2.0) * rinv) + 0.5);
            }
            for a in 0..3 {
                let (p, q) = (INC3[a], DEC3[a]);
                if n[q].wrapping_mul(n[p]) > 4096 {
                    n[p] = n[p].wrapping_add(neg_part(64i32.wrapping_sub(n[p])));
                    n[q] = n[q].wrapping_add(neg_part(64i32.wrapping_sub(n[q])));
                }
            }
            let lhs = (1 - s).wrapping_mul(n_tris);
            let e = n[1]
                .wrapping_add(n[0])
                .wrapping_mul(n[2])
                .wrapping_add(n[1].wrapping_mul(n[0]));
            let rhs = e.wrapping_mul(3).wrapping_mul(2);
            if lhs > rhs {
                return false;
            }
        }
        let (s0, s1, s2) = (size[0], size[1], size[2]);
        let e = sbit(s2 * s1 - s2 * s0) as usize;
        let f = size[DEC3[e]] * size[INC3[e]] - s1 * s0;
        let m = (2 * sbit(f)) | e as i32;
        let mut ax = (m & !(m >> 1)) as usize;
        let c = r - orig[ax] * 2.0;
        let count = sbit(c) + 1;
        let mut grid = RayGrid::default();
        for _ in 0..count {
            let (p, q) = (INC3[ax], DEC3[ax]);
            let frame = LayerFrame {
                axis_ray: rows[ax],
                center: bx.center,
                axis_x: rows[p],
                axis_y: rows[q],
            };
            grid.layers.push(self.build_ray_grid_layer(
                &frame,
                [size[p] * 2.0, size[q] * 2.0],
                rinv,
            ));
            ax = INC3[ax];
        }
        self.ray_grid = Some(grid);
        self.ray_grid_ready = count;
        true
    }

    /// sub_351C8B10: one grid layer over the rectangle `ext` (full extents along `axis_x`,
    /// `axis_y`) centred on `frame.center`.
    ///
    /// Extents are padded by `1e-5·max` and floored at `1e-4` (x) and `0.001·x` (y). Cell
    /// counts: with `rinv = 0`, `n_s = round(√(2·nTris·ext_s / ext_o))` on the axis `s` with
    /// the smaller extent and `n_o = max(2·nTris / max(n_s, 1), 1)`; else
    /// `round(ext·rinv + 0.5)`; both clamped to `1..=64`. Triangles are projected on the grid
    /// plane (vertex order flipped when the normal points against the ray axis), and a cell
    /// receives a triangle when its centre is inside the projected triangle (all three edge
    /// functions negative) or when one of the triangle's edges overlaps the cell box (a
    /// per-edge slab test in the edge's own quadrant, with `1e-10` margins). Two passes over
    /// the triangles (last to first): counting, then filling (after prefix sums).
    fn build_ray_grid_layer(&self, frame: &LayerFrame, ext: [f32; 2], a7: f32) -> RayGridLayer {
        let (w, h) = (ext[0], ext[1]);
        let e = (((w - h).abs() + (w + h)) * 0.5) * EPS_1E5;
        let h1 = h + e;
        let w1 = w + e;
        let t = h1 * EPS_1E3;
        let m = ((w1 - t).abs() + (t + w1)) * 0.5;
        let ex = ((EPS_1E4 - m).abs() + (m + EPS_1E4)) * 0.5;
        let t = ex * EPS_1E3;
        let ey = ((h1 - t).abs() + (t + h1)) * 0.5;
        let dims = [ex, ey];
        let (mut n0, mut n1);
        // `ucomiss` + `jne`: NaN takes the `== 0` branch.
        if a7 == 0.0 || a7.is_nan() {
            let s = sbit(ex - ey) as usize;
            let nt2 = self.n_tris.wrapping_add(self.n_tris);
            let v = ((nt2 as f32 * dims[s]) / dims[s ^ 1]).sqrt();
            let ns = rb(v);
            let d = 1i32.wrapping_sub(neg_part(1i32.wrapping_sub(ns)));
            let qv = nt2.wrapping_div(d);
            let no = 1i32.wrapping_sub(neg_part(1i32.wrapping_sub(qv)));
            let mut n = [0i32; 2];
            n[s] = ns;
            n[s ^ 1] = no;
            n0 = n[0];
            n1 = n[1];
        } else {
            n0 = rb(ex * a7 + 0.5);
            n1 = rb(ey * a7 + 0.5);
        }
        n0 = n0.clamp(1, 64);
        n1 = n1.clamp(1, 64);
        let n_cells = n0 * n1;
        let mut cells = vec![0i32; (n_cells + 1) as usize];
        let n0f = n0 as f32;
        let n1f = n1 as f32;
        let stepx = ex / n0f;
        let rstepx = n0f / ex;
        let stepy = ey / n1f;
        let rstepy = n1f / ey;
        let hx = (n0f * stepx) * 0.5;
        let hy = (n1f * stepy) * 0.5;
        let (az, c, gx, gy) = (frame.axis_ray, frame.center, frame.axis_x, frame.axis_y);
        let ox = (c.x - gx.x * hx) - gy.x * hy;
        let oy = (c.y - gx.y * hx) - gy.y * hy;
        let oz = (c.z - gx.z * hx) - gy.z * hy;

        let mut tris: Vec<i32> = Vec::new();
        let mut last: i32 = 0; // v9: last valid index of the list (0 in the counting pass)
        for pass in 0..2i32 {
            for t in (0..self.n_tris).rev() {
                let tu = t as usize;
                let n = self.normals[tu];
                let s2 = (2 * sbit(dot_yxz(az, n))) as usize;
                let vtx = |k: usize| self.vertices[self.indices[3 * tu + k] as usize];
                let proj = |v: Vec3| -> (f32, f32) {
                    let dx = v.x - ox;
                    let dy = v.y - oy;
                    let dz = v.z - oz;
                    (
                        (dy * gx.y + gx.x * dx) + dz * gx.z,
                        (dy * gy.y + gy.x * dx) + dz * gy.z,
                    )
                };
                let (xa, ya) = proj(vtx(s2));
                let (xb, yb) = proj(vtx(1));
                let (xc, yc) = proj(vtx(s2 ^ 2));
                let e0x = xb - xa;
                let e0y = yb - ya;
                let e1x = xc - xb;
                let e1y = yc - yb;
                let e2x = xa - xc;
                let e2y = ya - yc;
                let edges = [e0x, e0y, e1x, e1y, e2x, e2y];
                let pos = [xa, ya, xb, yb, xc, yc];
                let ia = rb(rstepx * xa - 0.5);
                let ja = rb(rstepy * ya - 0.5);
                let ib = rb(rstepx * xb - 0.5);
                let jb = rb(rstepy * yb - 0.5);
                let ic = rb(rstepx * xc - 0.5);
                let jc = rb(rstepy * yc - 0.5);
                let i_lo = max0(min_b(min_b(ia, ib), ic));
                let j_lo = max0(min_b(min_b(ja, jb), jc));
                let i_hi = min_b(n0 - 1, max_a(max_a(ia, ib), ic));
                let j_hi = min_b(n1 - 1, max_a(max_a(ja, jb), jc));
                if j_lo > j_hi || i_lo > i_hi {
                    continue;
                }
                let cx0 = ((i_lo as f32) + 0.5) * stepx;
                for j in j_lo..=j_hi {
                    let cy = ((j as f32) + 0.5) * stepy;
                    let v100 = (cy - ya) * e0x;
                    let v101 = (cy - yc) * e2x;
                    let v102 = (cy - yb) * e1x;
                    let mut cx = cx0;
                    for i in i_lo..=i_hi {
                        let f0 = e0y * (cx - xa) - v100;
                        let f1 = e1y * (cx - xb) - v102;
                        let f2 = e2y * (cx - xc) - v101;
                        let mut hit = sbit(f2) & sbit(f1) & sbit(f0);
                        for k in 0..3 {
                            let exk = edges[2 * k];
                            let eyk = edges[2 * k + 1];
                            let aex = exk.abs();
                            let aey = eyk.abs();
                            let sx = sgn0(exk) as f32;
                            let sy = sgn0(eyk) as f32;
                            let px = pos[2 * k];
                            let py = pos[2 * k + 1];
                            let hxs = (sx * stepx) * 0.5;
                            let a_lo = ((cx - hxs) - px) * sx;
                            let a_hi = ((cx + hxs) - px) * sx;
                            let v120 = (a_lo.abs() + a_lo) * 0.5;
                            let v118 = ((a_hi + aex) - (aex - a_hi).abs()) * 0.5;
                            let hys = (sy * stepy) * 0.5;
                            let b_hi = ((cy + hys) - py) * sy;
                            let b_lo = ((cy - hys) - py) * sy;
                            let v119 = (b_lo.abs() + b_lo) * 0.5;
                            let v121 = ((b_hi + aey) - (aey - b_hi).abs()) * 0.5;
                            let v166 = (v120 - v118) - EPS_1E10;
                            let v148 = (v119 - v121) - EPS_1E10;
                            let p1 = v120 * aey;
                            let p2 = aex * v119;
                            let p = (p1 - p2).abs() + (p2 + p1);
                            let q1 = v118 * aey;
                            let q2 = v121 * aex;
                            let q = (q1 + q2) - (q1 - q2).abs();
                            let v165 = (p - q) - EPS_1E10;
                            hit |= sbit(v165) & sbit(v148) & sbit(v166);
                        }
                        let ci = (j.wrapping_mul(n0).wrapping_add(i)) as usize;
                        cells[ci] = cells[ci].wrapping_sub((-hit) & pass);
                        if pass == 1 && hit != 0 {
                            // Pass 0 writes into a stack scratch word (`*a6` = &v147), index 0.
                            let idx = max0(min_b(cells[ci], last)) as usize;
                            tris[idx] = (tris[idx] & (hit - 1)) | ((-hit) & t);
                        }
                        cells[ci] = cells[ci].wrapping_add((pass ^ 1) & hit);
                        cx += stepx;
                    }
                }
            }
            if pass == 0 {
                for k in 0..n_cells as usize {
                    cells[k + 1] = cells[k + 1].wrapping_add(cells[k]);
                }
                let total = cells[(n1 * n0) as usize];
                tris = vec![0; total.max(0) as usize];
                last = total.wrapping_sub(1);
            }
        }
        RayGridLayer {
            axis_x: gx,
            axis_y: gy,
            axis_ray: az,
            origin: Vec3::new(ox, oy, oz),
            step: [stepx, stepy],
            rstep: [rstepx, rstepy],
            size: [n0, n1],
            stride: [1, n0],
            cells,
            tris,
        }
    }

    /// Slot 8 `PointInsideStatus(pt)` (0x351DB380): slot 61 without an out triangle.
    pub fn point_inside_status(&mut self, pt: Vec3) -> i32 {
        self.point_inside_status_tri(pt, None)
    }

    /// Slot 61 `PointInsideStatus(pt, Triangle* out)` (0x351C8600): builds the ray grid
    /// (slot 22 with `raylen = 0`; returns 0 when that fails), then casts a ray from `pt` along
    /// layer 0's ray axis, `(n0·step0 + n1·step1)` long, against the triangles of `pt`'s
    /// (clamped) cell with ray–tri (0x3515AB20, tolerance 0). The nearest hit (smallest
    /// `(hit − pt)·dir`, starting from `1e20`) decides: inside (1) when the hit triangle's
    /// normal has `n·dir ≥ 0` (sign bit clear), else 0; no hit → 0. On every new nearest hit
    /// the triangle (vertices, normal, index) is written to `out` when given.
    ///
    /// Binary quirks kept: a NaN distance is accepted as the new nearest (and then every later
    /// hit is too: `comiss` + `jae`); the ray starts at `pt`, so only triangles in front of
    /// the point along the axis count, and a point outside the grid uses the nearest border
    /// cell.
    pub fn point_inside_status_tri(&mut self, pt: Vec3, mut out: Option<&mut InsideTri>) -> i32 {
        if !self.prepare_for_ray_test(0.0) {
            return 0;
        }
        // An object marked ready without a grid (`+476 < 0`, or a parsed dump) has no layer
        // to read; the original would read whatever +176.. holds.
        let Some(g) = self.ray_grid.as_ref().and_then(|g| g.layers.first()) else {
            return 0;
        };
        let (gx, gy, o) = (g.axis_x, g.axis_y, g.origin);
        let dx = pt.x - o.x;
        let dy = pt.y - o.y;
        let dz = pt.z - o.z;
        let fx = (((dy * gx.y) + (dx * gx.x)) + (dz * gx.z)) * g.rstep[0] - 0.5;
        let fy = (((dy * gy.y) + (dx * gy.x)) + (dz * gy.z)) * g.rstep[1] - 0.5;
        let ix = rb(fx);
        let iy = rb(fy);
        let cxi = max0(ix);
        let cxi = min_b(g.size[0].wrapping_sub(1), cxi);
        let cyi = max0(iy);
        let cyi = min_b(g.size[1].wrapping_sub(1), cyi);
        let cell = cxi
            .wrapping_mul(g.stride[0])
            .wrapping_add(cyi.wrapping_mul(g.stride[1])) as usize;
        let len = (g.size[1] as f32) * g.step[1] + (g.size[0] as f32) * g.step[0];
        let a = g.axis_ray;
        let dir = Vec3::new(a.x * len, a.y * len, a.z * len);
        let ray = RayPrim { origin: pt, dir };
        let mut best = FAR;
        let mut res = 0i32;
        let (start, end) = (g.cells[cell], g.cells[cell + 1]);
        for k in start..end {
            let t = g.tris[k as usize];
            let tu = t as usize;
            let tri = Triangle {
                pt: [
                    self.vertices[self.indices[3 * tu] as usize],
                    self.vertices[self.indices[3 * tu + 1] as usize],
                    self.vertices[self.indices[3 * tu + 2] as usize],
                ],
                n: self.normals[tu],
            };
            let mut pi = PrimInters::default(); // tolerance (+40) = 0
            if !intersect_ray_tri(&ray, &tri, &mut pi) {
                continue;
            }
            let h = pi.pt[0];
            let d = ((h.y - pt.y) * dir.y + (h.x - pt.x) * dir.x) + (h.z - pt.z) * dir.z;
            // `comiss d, best` + `jae skip`: NaN is accepted.
            if d >= best {
                continue;
            }
            best = d;
            let nn = tri.n;
            res = i32::from(((nn.y * dir.y + nn.x * dir.x) + nn.z * dir.z).to_bits() >> 31 == 0);
            if let Some(o) = out.as_deref_mut() {
                *o = InsideTri { tri, idx: t };
            }
        }
        res
    }
}
