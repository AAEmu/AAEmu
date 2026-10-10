//! `CHeightfieldBV` (x64 vtable 0x351F2068, embedded at `CHeightfield +728`): a single "node"
//! covering the current region `{ix, iy, w, h}` (cells, `+24..+36`) with the height range
//! `[hmin, hmax]` (`+40/+44`), both set by the geometry's slot 53 ([`super::region`]).
//!
//! | slot | x64 | port |
//! |---|---|---|
//! | 0 dtor | 0x350FE160 | `Drop` (frees `+48`) |
//! | 1 GetType | 0x35037C40 | [`BvTreeType::Heightfield`] (4) |
//! | 2 GetBBox | 0x35154010 | [`BvTree::bbox`] |
//! | 3 MaxPrimsInNode | 0x350FE150 | `2·w·h` |
//! | 4 Build | 0x35153F60 | [`HeightfieldBv::build`] |
//! | 9 GetNodeBV transformed | 0x351549A0 | [`BvTree::get_node_bv_transformed`] |
//! | 11 GetNodeBV local | 0x351540D0 | [`BvTree::get_node_bv`] |
//! | 23 GetNodeContents | 0x351546A0 | [`HeightfieldBv::node_contents`] |
//! | 25 MarkUsedTriangle | 0x35153FA0 | [`BvTree::mark_used_triangle`] |
//!
//! Every other slot is the `CBVTree` base (6 clears the gtest's used-node map, 12 and 19 return
//! 0, the rest are no-ops), which is what the [`BvTree`] defaults do.

use std::sync::atomic::{AtomicU32, Ordering};

use crate::geometry::bvtree::{BvArena, BvHandle, BvTestState, BvTree, NodeContents};
use crate::geometry::primitives::{BoxPrim, HeightfieldPrim, Triangle};
use crate::ids::BvTreeType;
use crate::math::{Mat33, Vec3};

use super::region::HfFrame;
use super::Heightfield;

/// `CHeightfieldBV`.
#[derive(Debug, Default)]
pub struct HeightfieldBv {
    /// The primitive `+16` points at (the geometry's `+608`; the port keeps it here). Basis,
    /// `bOriented` and origin are identity / 0 / zero after `SetData`.
    pub prim: HeightfieldPrim,
    /// `+16` is null until `SetData`.
    pub has_prim: bool,
    /// `+24` / `+28` region origin cell.
    pub ix: i32,
    pub iy: i32,
    /// `+32` / `+36` region size in cells.
    pub w: i32,
    pub h: i32,
    /// `+40` / `+44` height range of the region's vertices.
    pub hmin: f32,
    pub hmax: f32,
    /// `+48` used-triangle bitmask (slot 25 sets bits, slot 23 skips set triangles, the
    /// geometry's slot 53 clears the words covering nTris). Atomic words because slot 25 is a
    /// `&self` method of [`BvTree`] (the original writes through the BV's pointer).
    pub used_tris: Vec<AtomicU32>,
}

impl Clone for HeightfieldBv {
    fn clone(&self) -> Self {
        Self {
            prim: self.prim,
            has_prim: self.has_prim,
            ix: self.ix,
            iy: self.iy,
            w: self.w,
            h: self.h,
            hmin: self.hmin,
            hmax: self.hmax,
            used_tris: self
                .used_tris
                .iter()
                .map(|a| AtomicU32::new(a.load(Ordering::Relaxed)))
                .collect(),
        }
    }
}

/// One call the BV's slot 23 makes to the geometry's slot 56 (`GetPrimitiveList`, the
/// `CTriMesh` one, 0x351C3C60): triangles `first_prim .. first_prim + n_prims` of the region
/// mesh (slot 56 clamps the end to nTris). The remaining slot-56 arguments are the same for
/// every call: the collider BV's type and primitive, `bColliderLocal`, both gtests, and the
/// output buffers advanced by the running total of the earlier calls' return values.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Slot56Call {
    pub first_prim: i32,
    pub n_prims: i32,
}

impl HeightfieldBv {
    /// The words of the used-triangle mask.
    #[must_use]
    pub fn used_mask(&self) -> Vec<u32> {
        self.used_tris
            .iter()
            .map(|a| a.load(Ordering::Relaxed))
            .collect()
    }

    /// Replaces the mask with `n` zeroed words (a fresh allocation).
    pub(crate) fn set_used_mask_words(&mut self, n: usize) {
        self.used_tris = (0..n).map(|_| AtomicU32::new(0)).collect();
    }

    /// Word `i` of the mask (0 past the allocation; the original reads past it).
    fn used_word(&self, i: usize) -> u32 {
        self.used_tris
            .get(i)
            .map_or(0, |a| a.load(Ordering::Relaxed))
    }

    /// Slot 4 `Build` (0x35153F60): stores the geometry (`+8`) and returns the cost
    /// `((nx·step.x)·ny·step.y)·(hmax − hmin)` (single precision, in that order).
    #[must_use]
    pub fn build(&self) -> f32 {
        let p = &self.prim;
        (((p.size[0] as f32 * p.step[0]) * p.size[1] as f32) * p.step[1]) * (self.hmax - self.hmin)
    }

    /// Slot 2 body: the region box, axis-aligned (`bOriented` = 0), in the heightfield frame:
    /// `size = (w·step.x·0.5, h·step.y·0.5, (hmax − hmin)·0.5)`,
    /// `center = (ix·step.x + size.x, iy·step.y + size.y, size.z + hmin)`.
    #[must_use]
    pub(crate) fn bbox_impl(&self) -> BoxPrim {
        let p = &self.prim;
        let sx = (self.w as f32 * p.step[0]) * 0.5;
        let sy = (self.h as f32 * p.step[1]) * 0.5;
        let sz = (self.hmax - self.hmin) * 0.5;
        BoxPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            center: Vec3::new(
                self.ix as f32 * p.step[0] + sx,
                self.iy as f32 * p.step[1] + sy,
                sz + self.hmin,
            ),
            size: Vec3::new(sx, sy, sz),
        }
    }

    /// The common part of slots 9 and 11: the record's primitive is a copy of the heightfield
    /// primitive. Neither slot writes the record's `type` / `iNode` words: they keep the value
    /// of the DLL's static initialiser 0x35153F10 (type 2, iNode 0), which the port's
    /// [`BvArena::new`] does not set, so the port writes them here.
    fn record_base(&self, arena: &mut BvArena) {
        arena.hf_bv.kind = 2;
        arena.hf_bv.i_node = 0;
        arena.hf_bv.hf = self.prim;
    }

    /// Slot 23 `GetNodeContents` (0x351546A0): the slot-56 calls for the triangles under the
    /// collider BV (a record in the other tree; `collider_kind` its type, `collider_box` its
    /// box when the type is 0). Region triangle `2·(row·w + col) + {0, 1}`.
    ///
    /// * Collider not a box (`kind ≠ 0`): the whole region, `start = (0, 0)`,
    ///   `end = (w, h)`.
    /// * Box: sub_35154220 ([`collider_cells`]) on the box with this BV's primitive — in the
    ///   region frame when `b_collider_local ≠ 0`, else through `gtest` (the collider is then in
    ///   the other side's frame) — gives `(cx, cy, cw, ch)`; `start = (max(cx, 0), max(cy, 0))`,
    ///   `end = (min(cx + cw, w), min(cy + ch, h))`.
    ///
    /// Rows `start.y .. start.y + end.y` (**not** `end.y − start.y` rows — kept bug), each one
    /// call `(2·(row·w + start.x), 2·end.x)` (again a count, not an end — kept). With
    /// `b_collider_used ≠ 0` every triangle of those ranges is instead tested against the used
    /// mask and emitted alone (`n = 1`) when its bit is clear; the scan stops (whole function)
    /// at the first triangle whose mask word is past `(tri_cap >> 5) + 1` (the geometry is a
    /// heightfield, so the original takes `+796`; for any other type it would be nTris − 1).
    ///
    /// The returned calls are in order; the original adds each call's return value to the
    /// output buffers of the next one.
    #[allow(clippy::too_many_arguments)]
    #[must_use]
    pub fn node_contents(
        &self,
        tri_cap: i32,
        collider_kind: i32,
        collider_box: &BoxPrim,
        b_collider_used: bool,
        b_collider_local: bool,
        gtest: &HfFrame,
    ) -> Vec<Slot56Call> {
        let mut out = Vec::new();
        let (sx, sy, ex, ey);
        if collider_kind != 0 {
            sx = 0;
            sy = 0;
            ex = self.w;
            ey = self.h;
        } else {
            let frame = if b_collider_local { None } else { Some(gtest) };
            let c = collider_cells(collider_box, &self.prim, frame);
            let w = self.w;
            let hh = self.h;
            let d = c.ix.wrapping_add(c.w).wrapping_sub(w);
            ex = w.wrapping_add(d & (d >> 31));
            sx = !(c.ix >> 31) & c.ix;
            let d = c.iy.wrapping_add(c.h).wrapping_sub(hh);
            ey = hh.wrapping_add(d & (d >> 31));
            sy = !(c.iy >> 31) & c.iy;
        }
        let mut cell = sx.wrapping_add(sy.wrapping_mul(self.w));
        if !b_collider_used {
            for _ in 0..ey.max(0) {
                out.push(Slot56Call {
                    first_prim: cell.wrapping_mul(2),
                    n_prims: ex.wrapping_mul(2),
                });
                cell = cell.wrapping_add(self.w);
            }
            return out;
        }
        let words = (tri_cap >> 5) + 1;
        let span = ex.wrapping_mul(2);
        for _ in 0..ey.max(0) {
            if span > 0 {
                let start = cell.wrapping_mul(2);
                let mut t = start;
                loop {
                    if words <= t >> 5 {
                        return out;
                    }
                    if (self.used_word((t >> 5) as usize) >> (t & 31)) & 1 == 0 {
                        out.push(Slot56Call {
                            first_prim: t,
                            n_prims: 1,
                        });
                    }
                    t = t.wrapping_add(1);
                    if t.wrapping_sub(start) >= span {
                        break;
                    }
                }
            }
            cell = cell.wrapping_add(self.w);
        }
        out
    }
}

/// The triangles slot 56 reads for a [`Slot56Call`], in the region frame: `pt` = the three
/// vertices of `indices[3i..3i+3]`, `n` = `normals[i]`, the id byte = `mats[i]` (−1 = hole; the
/// mesh slot 56 drops those). Clamped to nTris like slot 56. For inspection and for wiring;
/// slot 56 itself (transform into the gtest frame, OVERLAP filter) is the mesh's.
#[must_use]
pub fn region_triangles(hf: &Heightfield, call: Slot56Call) -> Vec<(i32, Triangle, i8)> {
    let m = &hf.mesh;
    let end = call
        .first_prim
        .wrapping_add(call.n_prims)
        .min(m.n_tris)
        .max(call.first_prim);
    (call.first_prim..end)
        .map(|i| {
            let k = i as usize;
            let v = |j: usize| m.vertices[m.indices[3 * k + j] as usize];
            let mat = m.mats.as_ref().map_or(0, |a| a[k]);
            (
                i,
                Triangle {
                    pt: [v(0), v(1), v(2)],
                    n: m.normals[k],
                },
                mat,
            )
        })
        .collect()
}

/// Output of sub_35154220.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ColliderCells {
    /// First cell, clamped at 0.
    pub ix: i32,
    pub iy: i32,
    /// Cell counts: `min(ceil-ish end, nx) − ix`.
    pub w: i32,
    pub h: i32,
    /// Bottom of the box (`center.z − extent.z`) in the heightfield frame.
    pub zmin: f32,
}

/// sub_35154220: the cells under a box. With `frame` (`gtest`) the box is in the other side's
/// frame and is brought in with `+84` (`r_rel`), `+132` (`offset_rel`) and `+156`
/// (`scale_rel_r`): `M = bOriented ? Basis·R_rel : R_rel`, `ext_j = Σ_i |M[i][j]|·(size_i·k)`,
/// `c = R_relᵀ·(center − offset_rel)·k` (`k = scale_rel_r`). Without it the box is local and its
/// basis is used **whatever `bOriented` says** (kept). Then
/// `ix = max(0, round((c.x − ext.x)·stepr.x − 0.5))`,
/// `w = min(round((ext.x + c.x)·stepr.x + 0.5), nx) − ix` (same for y), `zmin = c.z − ext.z`.
#[must_use]
pub fn collider_cells(b: &BoxPrim, p: &HeightfieldPrim, frame: Option<&HfFrame>) -> ColliderCells {
    let (c, ext);
    match frame {
        None => {
            let m = &b.basis.m;
            let s = b.size;
            ext = Vec3::new(
                (m[1][0].abs() * s.y + m[0][0].abs() * s.x) + m[2][0].abs() * s.z,
                (m[0][1].abs() * s.x + m[1][1].abs() * s.y) + m[2][1].abs() * s.z,
                (m[0][2].abs() * s.x + m[1][2].abs() * s.y) + m[2][2].abs() * s.z,
            );
            c = b.center;
        }
        Some(f) => {
            let mm = if b.oriented != 0 {
                b.basis.mul_mat(&f.r_rel)
            } else {
                f.r_rel
            };
            let m = &mm.m;
            let k = f.scale_rel_r;
            let sx = b.size.x * k;
            let sy = b.size.y * k;
            let sz = b.size.z * k;
            ext = Vec3::new(
                (sy * m[1][0].abs() + sx * m[0][0].abs()) + sz * m[2][0].abs(),
                (sy * m[1][1].abs() + m[0][1].abs() * sx) + sz * m[2][1].abs(),
                (sy * m[1][2].abs() + m[0][2].abs() * sx) + sz * m[2][2].abs(),
            );
            let r = &f.r_rel.m;
            let dx = b.center.x - f.offset_rel.x;
            let dy = b.center.y - f.offset_rel.y;
            let dz = b.center.z - f.offset_rel.z;
            c = Vec3::new(
                ((dy * r[1][0] + dx * r[0][0]) + dz * r[2][0]) * k,
                ((dy * r[1][1] + dx * r[0][1]) + dz * r[2][1]) * k,
                ((dy * r[1][2] + dx * r[0][2]) + dz * r[2][2]) * k,
            );
        }
    }
    let lo = |v: f32, sr: f32| -> i32 {
        let i = round_i((v * sr - 0.5) + 12_582_912.0);
        !(i >> 31) & i
    };
    let hi = |v: f32, sr: f32, n: i32, start: i32| -> i32 {
        let i = round_i((v * sr + 0.5) + 12_582_912.0).wrapping_sub(n);
        (i & (i >> 31)).wrapping_sub(start).wrapping_add(n)
    };
    let ix = lo(c.x - ext.x, p.stepr[0]);
    let iy = lo(c.y - ext.y, p.stepr[1]);
    ColliderCells {
        ix,
        iy,
        w: hi(ext.x + c.x, p.stepr[0], p.size[0], ix),
        h: hi(ext.y + c.y, p.stepr[1], p.size[1], iy),
        zmin: c.z - ext.z,
    }
}

/// `bits(v) − 0x4B400000` for a value that already had the magic number added.
#[inline]
fn round_i(v: f32) -> i32 {
    (v.to_bits() as i32).wrapping_sub(0x4B40_0000)
}

impl BvTree for HeightfieldBv {
    /// Slot 1 (0x35037C40): 4.
    fn tree_type(&self) -> BvTreeType {
        BvTreeType::Heightfield
    }

    /// Slot 2 `GetBBox` (0x35154010).
    fn bbox(&self) -> BoxPrim {
        self.bbox_impl()
    }

    /// Slot 3 (0x350FE150): `2·w·h`.
    fn max_prims_in_node(&self) -> i32 {
        2i32.wrapping_mul(self.w).wrapping_mul(self.h)
    }

    /// Slot 9 (0x351549A0): the record of caller `iCaller` (arena `+0x39DE8`) with the
    /// primitive copied, then `basis = Rᵀ`, `bOriented = 1`, `step ·= scale`,
    /// `stepr ·= 1/scale` (exactly 1 when `scale == 1`), `heightscale ·= scale` and
    /// `origin = offset − R·(ix·step'.x, iy·step'.y, 0)` (the region corner becomes cell 0).
    fn get_node_bv_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        _i_node: i32,
    ) -> BvHandle {
        self.record_base(arena);
        let rec = &mut arena.hf_bv.hf;
        rec.basis = r.transpose();
        rec.oriented = 1;
        rec.origin = offset;
        rec.step = [scale * self.prim.step[0], scale * self.prim.step[1]];
        // `ucomiss scale, 1.0; je`: 1 (also for a NaN scale), else `1/scale`.
        let rs = if scale == 1.0 || scale.is_nan() {
            1.0
        } else {
            1.0 / scale
        };
        rec.stepr = [rs * self.prim.stepr[0], rs * self.prim.stepr[1]];
        let ty = self.iy as f32 * rec.step[1];
        let tx = self.ix as f32 * rec.step[0];
        let m = &r.m;
        let d0 = (tx * m[0][0] + ty * m[0][1]) + m[0][2] * 0.0;
        let d1 = (ty * m[1][1] + tx * m[1][0]) + m[1][2] * 0.0;
        let d2 = (ty * m[2][1] + tx * m[2][0]) + m[2][2] * 0.0;
        rec.origin = Vec3::new(rec.origin.x - d0, rec.origin.y - d1, rec.origin.z - d2);
        rec.height_scale *= scale;
        BvHandle::Heightfield
    }

    /// Slot 11 (0x351540D0): the record with the primitive copied, identity basis,
    /// `bOriented` = 0 and `origin = (−ix·step.x, −iy·step.y, 0)`.
    fn get_node_bv(&self, arena: &mut BvArena, _i_node: i32) -> BvHandle {
        self.record_base(arena);
        let rec = &mut arena.hf_bv.hf;
        rec.basis = Mat33::IDENTITY;
        rec.oriented = 0;
        rec.origin = Vec3::new(
            self.ix.wrapping_neg() as f32 * self.prim.step[0],
            self.iy.wrapping_neg() as f32 * self.prim.step[1],
            0.0,
        );
        BvHandle::Heightfield
    }

    /// Slot 23 cannot be expressed as one range: see [`HeightfieldBv::node_contents`].
    fn get_node_contents(&self, _i_node: i32) -> NodeContents {
        panic!("CHeightfieldBV slot 23 emits one slot-56 call per row: use HeightfieldBv::node_contents")
    }

    /// Slot 25 `MarkUsedTriangle` (0x35153FA0): `mask[i >> 5] |= 1 << (i & 31)` (the BV's
    /// own mask, not the gtest's).
    fn mark_used_triangle(&self, i_tri: i32, _gtest: &mut BvTestState, _arena: &mut BvArena) {
        let w = (i_tri >> 5) as usize;
        if let Some(a) = self.used_tris.get(w) {
            a.fetch_or(1u32 << (i_tri & 31), Ordering::Relaxed);
        }
    }
}
