//! **P3 — the entity grid** (`world.md` §3): a 2D grid over the two axes other than `axisz`,
//! a sparse page table of cells (pages of 8×8 cells), and a pool of 32-byte "thunks" (one per
//! entity and covered cell, index-linked). Ported functions:
//!
//! | function | address |
//! |---|---|
//! | SetupEntityGrid (world slot 11) | 0x3507E5A0 |
//! | grow the thunk pool | 0x35078050 |
//! | build the page table | 0x35095DA0 |
//! | cell slot (allocating a page) | 0x35076C40 |
//! | detach an entity's thunks | 0x3507A860 |
//! | RepositionEntity | 0x35080A00 |
//! | GetEntitiesInBox / GetEntitiesAround | 0x35084680 |
//! | AABB overlap test | 0x350963E0 |
//! | quicksort by mass | 0x35079CC0 |
//!
//! Not ported: the physics-on-demand grid (`+1016…+1064`, world slots 13–16 and the streamer
//! callbacks; with `log2PODscale = 0` and no POD pages the lookups in GetEntitiesInBox hit the
//! dummy cell and are no-ops, which is what this port implements), placeholders
//! (`CPhysicalPlaceholder`, `+56` links: entities are always real), breakable structures (part
//! `+128` sub-entities).
//!
//! Note from the oracle: entities whose bbox lies below the grid origin's z are not found by the
//! broad phase (the z range of a thunk is quantised from the origin and clamped at 0).

use super::{EntIdx, World};
use crate::math::Vec3;

/// The magic-number floor of the grid code: `(int)((x − 0.5) + 12582912.0) − 0x4B400000`.
#[inline]
fn floor_m(x: f32) -> i32 {
    ((x - 0.5) + 12_582_912.0).to_bits() as i32 - 1_262_485_504
}

/// `x + ((lim − x) & ((lim − x) >> 31))` = `min(x, lim)` (branch-free).
#[inline]
fn min_bf(x: i32, lim: i32) -> i32 {
    let d = lim.wrapping_sub(x);
    x.wrapping_add(d & (d >> 31))
}

/// `-1 - ((-1 - x) & ((-1 - x) >> 31))` = `max(x, -1)`.
#[inline]
fn max_m1(x: i32) -> i32 {
    let d = -1i32 - x;
    -1 - (d & (d >> 31))
}

/// A grid thunk (32 bytes). Field docs give the binary layout.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Thunk {
    /// `+0` bits 0–19 next thunk in the cell.
    pub w0: u32,
    /// `+4` bits 0–19 previous thunk in the cell, or (when `w8 & 0x800000`) the packed cell
    /// coordinates `ix | iy << 10`.
    pub w4: u32,
    /// `+8` bits 0–19 next thunk of the same entity, bit 0x800000 head-of-cell, bits 20–22
    /// simclass.
    pub w8: u32,
    /// `+12 … +15` sub-cell box `x0, y0, x1, y1` in 1/256 cell units.
    pub sub: [u8; 4],
    /// `+16` the entity.
    pub ent: EntIdx,
    /// `+24` / `+26` quantised zmin / zmax.
    pub z: [u16; 2],
}

/// Grid state (`world+856 … +996`).
#[derive(Clone, Debug)]
pub struct Grid {
    /// `+856` rotation (identity).
    pub r: [f32; 9],
    /// `+896` origin.
    pub origin: Vec3,
    /// `+908` / `+912` cell size, `+916` / `+920` inverse cell size.
    pub step: [f32; 2],
    pub step_r: [f32; 2],
    /// `+924` / `+928` cell counts (≤ 1024).
    pub size: [i32; 2],
    /// `+932` / `+936` strides (1, nx).
    pub stride: [i32; 2],
    /// `+940` / `+944` world axis of grid x / y, `+948` axisz.
    pub iaxis: [usize; 2],
    pub axisz: usize,
    /// `+952` / `+956` z quantisation step (0.0625) and its inverse (16).
    pub zstep: f32,
    pub zstep_r: f32,
    /// `+960` log2 of the row bit count, `+964` / `+968` masks.
    pub page_shift: i32,
    pub page_mask: i32,
    pub page_mask2: i32,
    /// `+976` page table (pages of 8×8 cells holding the first thunk of each cell); `None` =
    /// grid not set up.
    pub pages: Option<Vec<Option<Box<[u32; 64]>>>>,
    /// `+984` thunks, `+992` capacity (= `thunks.len()`), `+996` free list head.
    pub thunks: Vec<Thunk>,
    pub free_thunk: u32,
    /// `+1048` / `+1052` / `+1056` POD strides and log2 scale.
    pub pod_stride: [i32; 2],
    pub pod_log2: i32,
    /// "Error: too many entity grid thunks" logged once (`byte_35213D04`).
    pub thunk_error_logged: bool,
}

/// The world-axis index tables (`0x352094C8` / `0x352094D8`) indexed by `axisz`.
const AXIS_X: [usize; 3] = [1, 2, 0];
const AXIS_Y: [usize; 3] = [2, 0, 1];

impl Default for Grid {
    fn default() -> Self {
        Self {
            r: [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0],
            origin: Vec3::ZERO,
            step: [0.0; 2],
            step_r: [0.0; 2],
            size: [0; 2],
            stride: [0; 2],
            iaxis: [0, 1],
            axisz: 2,
            // world reset 0x3507DE80: +952 = 0.0625, +956 = 16.
            zstep: f32::from_bits(1_031_798_784),
            zstep_r: f32::from_bits(1_098_907_648),
            page_shift: 0,
            page_mask: 0,
            page_mask2: 0,
            pages: None,
            thunks: Vec::new(),
            free_thunk: 0,
            pod_stride: [0; 2],
            pod_log2: 0,
            thunk_error_logged: false,
        }
    }
}

impl Grid {
    /// `sub_35095DA0`: page table for an `nx × ny` grid.
    fn build_pages(&mut self) {
        let (nx, ny) = (self.size[0], self.size[1]);
        let npages = (((ny * nx) as u32) >> 6) as usize + 1;
        self.pages = Some(vec![None; npages]);
        let pc = ((nx - 1) as u32).count_ones() as i32;
        self.page_shift = pc;
        self.page_mask = (1i32 << pc).wrapping_sub(1);
        self.page_mask2 = (1i32 << (pc + 3)).wrapping_sub(1);
    }

    /// Page index and slot of cell `i` (the address arithmetic shared by every reader).
    #[inline]
    fn cell_loc(&self, i: i32) -> (usize, usize) {
        let lo = i & self.page_mask;
        let hi = ((i as i64 >> 3) & (!(self.page_mask2 as i64) >> 3)) as i32;
        let page = ((lo | hi) >> 3) as usize;
        let slot = ((lo & 7) + 8 * ((i >> self.page_shift) & 7)) as usize;
        (page, slot)
    }

    /// First thunk of cell `i` (empty pages read as 0).
    #[inline]
    #[must_use]
    pub fn cell(&self, i: i32) -> u32 {
        let (p, s) = self.cell_loc(i);
        match self
            .pages
            .as_ref()
            .and_then(|v| v.get(p))
            .and_then(|o| o.as_ref())
        {
            Some(pg) => pg[s],
            None => 0,
        }
    }

    /// `sub_35076C40`: the slot of cell `i`, allocating its page (zeroed) when missing.
    fn cell_mut(&mut self, i: i32) -> &mut u32 {
        let (p, s) = self.cell_loc(i);
        let pages = self.pages.as_mut().expect("grid pages");
        let pg = pages[p].get_or_insert_with(|| Box::new([0u32; 64]));
        &mut pg[s]
    }

    /// `sub_35078050`: resize the thunk pool to `n` (shrinking drops everything; growing
    /// links the new thunks into the free list after the old ones).
    pub fn grow_thunks(&mut self, n: usize) {
        let old = self.thunks.len();
        if n == old {
            return;
        }
        if n < old {
            self.thunks.clear();
        }
        let old = self.thunks.len();
        if old != 0 {
            self.thunks.resize(n, Thunk::default());
            for i in old..n - 1 {
                self.thunks[i].w8 = (self.thunks[i].w8 & 0xFFF0_0000) | ((i as u32 + 1) & 0xFFFFF);
            }
            self.thunks[n - 1].w8 &= 0xFFF0_0000;
            self.free_thunk = old as u32;
        } else {
            self.thunks = vec![Thunk::default(); n];
            for i in 0..n - 1 {
                self.thunks[i].w8 = (self.thunks[i].w8 & 0xFFF0_0000) | ((i as u32 + 1) & 0xFFFFF);
            }
            self.thunks[n - 1].w8 &= 0xFFF0_0000;
            self.thunks[0].w4 &= 0xFFF0_0000;
            self.thunks[0].w0 &= 0xFFF0_0000;
            self.free_thunk = 1;
        }
    }

    /// The cell index `ix·stride0 + iy·stride1` (with grid flag 2: the out-of-grid cell
    /// `nx·ny` for coordinates outside), `sub_3507A0B0`.
    #[must_use]
    pub fn cell_index(&self, ix: i32, iy: i32, grid_flags: i32) -> i32 {
        if grid_flags & 2 == 0 {
            return iy * self.stride[1] + ix * self.stride[0];
        }
        let (nx, ny) = (self.size[0], self.size[1]);
        let m = ((-1 - iy) >> 31) & ((-1 - ix) >> 31) & ((ix - nx) >> 31) & ((iy - ny) >> 31);
        (!m & (nx * ny)) | (m & (iy * self.stride[1] + ix * self.stride[0]))
    }
}

/// `sub_350963E0`: AABB overlap with the binary's arithmetic
/// (`max(max(dy, dx), dz) < 0` with `d = (|(max_a+min_a) − min_b − max_b| − (max_a−min_a)) −
/// (max_b − min_b)`).
#[must_use]
pub fn aabb_overlap(amin: Vec3, amax: Vec3, bmin: Vec3, bmax: Vec3) -> bool {
    let d = |i: usize| {
        ((((amax[i] + amin[i]) - bmin[i]) - bmax[i]).abs() - (amax[i] - amin[i]))
            - (bmax[i] - bmin[i])
    };
    let v4 = d(1);
    let v5 = d(0);
    let v6 = ((v5 - v4).abs() + (v5 + v4)) * 0.5;
    let v7 = d(2);
    (((v6 - v7).abs() + (v7 + v6)) * 0.5) < 0.0
}

impl World {
    /// World slot 11 `SetupEntityGrid(axisz, origin, nx, ny, stepx, stepy, log2PODscale,
    /// bCyclic)` (0x3507E5A0). `bCyclic` is not read by the x64 build.
    #[allow(clippy::too_many_arguments)]
    pub fn setup_entity_grid(
        &mut self,
        axisz: usize,
        origin: Vec3,
        nx: i32,
        ny: i32,
        stepx: f32,
        stepy: f32,
        log2_pod_scale: i32,
    ) {
        if self.grid.pages.is_some() {
            for i in 0..self.id_table.len() {
                let e = self.id_table[i];
                if !e.is_none() {
                    self.grid_detach(e);
                }
            }
            let mut a = self.global_area;
            while !a.is_none() {
                self.grid_detach(a);
                a = self.area_next(a);
            }
            // Clear the "prev" links of the cell heads (the page memory is freed next).
            let n = self.grid.size[0] * self.grid.size[1];
            for i in (0..=n).rev() {
                let t = self.grid.cell(i);
                if t != 0 {
                    self.grid.thunks[t as usize].w4 &= 0xFFF0_0000;
                }
            }
            self.grid.pages = None;
        }
        self.grid.grow_thunks(100_000);
        // vslot 14: POD reset (POD not ported; nothing to clear).
        let nx = min_bf(nx, 1024);
        let ny = min_bf(ny, 1024);
        let g = &mut self.grid;
        g.iaxis = [AXIS_X[axisz], AXIS_Y[axisz]];
        g.axisz = axisz;
        g.size = [nx, ny];
        g.stride = [1, nx];
        g.step = [stepx, stepy];
        g.step_r = [1.0 / stepx, 1.0 / stepy];
        g.origin = origin;
        g.r = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0];
        g.build_pages();
        g.pod_log2 = log2_pod_scale;
        g.pod_stride = [1, ny >> (log2_pod_scale + 3)];
    }

    /// `sub_3507A860`: unlink every thunk of `e` from its cells and return them to the free
    /// list.
    pub fn grid_detach(&mut self, e: EntIdx) {
        let mut t = self.ent(e).grid_thunk;
        if t == 0 {
            return;
        }
        let g = &mut self.grid;
        let mut last;
        loop {
            let i = t as usize;
            let (w8, w4, w0) = (g.thunks[i].w8, g.thunks[i].w4, g.thunks[i].w0);
            g.thunks[i].ent = EntIdx::NONE;
            g.thunks[i].w4 |= 0xFFFFF;
            g.thunks[i].w0 |= 0xFFFFF;
            let next = w0 & 0xFFFFF;
            let prev = w4 & 0xFFFFF;
            let nent = w8 & 0xFFFFF;
            let n = next as usize;
            g.thunks[n].w4 =
                (g.thunks[n].w4 & 0xFFF0_0000) | (prev & ((-(next as i32) >> 31) as u32) & 0xFFFFF);
            g.thunks[n].w8 ^= (g.thunks[n].w8 ^ g.thunks[i].w8) & 0x80_0000;
            if g.thunks[i].w8 & 0x80_0000 != 0 {
                let mut cell = g.size[0] * g.size[1];
                if g.cell(cell) != t {
                    cell = (prev & 0x3FF) as i32 * g.stride[0]
                        + ((prev >> 10) & 0x3FF) as i32 * g.stride[1];
                }
                *g.cell_mut(cell) = next;
            } else {
                let p = prev as usize;
                g.thunks[p].w0 = (g.thunks[p].w0 & 0xFFF0_0000) | (next & 0xFFFFF);
            }
            last = t;
            t = nent;
            if t == 0 {
                break;
            }
        }
        let l = last as usize;
        g.thunks[l].w8 ^= (g.free_thunk ^ g.thunks[l].w8) & 0xFFFFF;
        g.free_thunk = self.ents[e.0 as usize].as_ref().expect("entity").grid_thunk;
        self.ent_mut(e).grid_thunk = 0;
    }

    /// `sub_35080A00` RepositionEntity(world, ent, flags, bbox*): flags bit 0 = grid update,
    /// bit 1 = simclass-list update (and event 2), bit 2 = test each cell against the
    /// geometry (not ported: no caller in the rigid/static paths uses it). `bbox` = `None`
    /// uses the entity's own bbox. Returns the grid lock delta (0 or 0x10000) or -1 for
    /// particles covering too many cells.
    pub fn reposition_entity(&mut self, e: EntIdx, flags: u32, bbox: Option<[Vec3; 2]>) -> i32 {
        let id_bits = self.ent(e).id_bits;
        if (id_bits >> 24) >= 7 {
            return 0;
        }
        let mut ret = 0;
        if flags & 1 != 0 && self.grid.pages.is_some() {
            let bb = bbox.unwrap_or(self.ent(e).bbox);
            let g = &self.grid;
            let (ax, ay, az) = (g.iaxis[0], g.iaxis[1], g.axisz);
            let (ox, oy, oz) = (g.origin[ax], g.origin[ay], g.origin[az]);
            let (nx, ny) = (g.size[0], g.size[1]);
            // [0] = min corner, [1] = max corner
            let mut ixy = [[0i32; 2]; 2]; // [corner][axis]
            let mut sub = [[0i32; 2]; 2];
            let mut iz = [0i32; 2];
            for (k, p) in bb.iter().enumerate() {
                let fx = (p[ax] - ox) * g.step_r[0];
                let cx = max_m1(min_bf(floor_m(fx), nx));
                let sx = floor_m((fx - cx as f32) * 256.0);
                ixy[k][0] = cx;
                sub[k][0] = -((-min_bf(sx, 255)) & ((-min_bf(sx, 255)) >> 31));
                let fy = (p[ay] - oy) * g.step_r[1];
                let cy = max_m1(min_bf(floor_m(fy), ny));
                let sy = floor_m((fy - cy as f32) * 256.0);
                ixy[k][1] = cy;
                sub[k][1] = -((-min_bf(sy, 255)) & ((-min_bf(sy, 255)) >> 31));
                iz[k] = k as i32 + ((p[az] - oz) * g.zstep_r) as i32;
            }
            let ent_cells = self.ent(e).grid_cells;
            if ent_cells[0] != -3 {
                let (x0, x1, y0, y1) = (ixy[0][0], ixy[1][0], ixy[0][1], ixy[1][1]);
                let changed = (flags & 4) as i32
                    | (x1 - i32::from(ent_cells[2]))
                    | (x0 - i32::from(ent_cells[0]))
                    | ((((flags & 4) as i32) ^ ((id_bits << 8) as i32 >> 29)) >> 2)
                    | (y0 - i32::from(ent_cells[1]))
                    | (y1 - i32::from(ent_cells[3]));
                if changed != 0 {
                    ret = 0x10000;
                    self.grid_detach(e);
                    let ncells = ((x1 - x0 + 1) * (y1 - y0 + 1)) as u32;
                    let sc = self.ent(e).sim_class();
                    if sc == 5 {
                        if ncells > self.vars.max_particle_cells as u32 {
                            return -1;
                        }
                    } else if ncells == 0 || ncells > self.vars.max_entity_cells as u32 {
                        // "Error: %s @ %.1f,%.1f,%.1f is too large or invalid"
                        if self.vars.validation_strict != 0 {
                            let ent = self.ent_mut(e);
                            ent.grid_cells = [-2, -2, -2, -2];
                        }
                        return self.reposition_lists(e, flags, ret);
                    }
                    // A thunk-pool overflow aborts the loop and skips the cell-bbox update.
                    if self.grid_insert(e, flags, ixy, sub, iz) {
                        let ent = self.ent_mut(e);
                        ent.grid_cells = [x0 as i16, y0 as i16, x1 as i16, y1 as i16];
                    }
                } else if self.ent(e).grid_thunk != 0 && x1 >= x0 {
                    self.grid_refresh(e, ixy, sub, iz);
                }
            }
        }
        self.reposition_lists(e, flags, ret)
    }

    /// The cell loop of RepositionEntity: one thunk per covered cell, `x` outer, `y` inner.
    /// Returns false when the thunk pool is exhausted (0x100000 thunks).
    fn grid_insert(
        &mut self,
        e: EntIdx,
        flags: u32,
        ixy: [[i32; 2]; 2],
        sub: [[i32; 2]; 2],
        iz: [i32; 2],
    ) -> bool {
        let (x0, x1, y0, y1) = (ixy[0][0], ixy[1][0], ixy[0][1], ixy[1][1]);
        let simclass = self.ent(e).sim_class();
        let mut x = x0;
        let mut v119 = 0i32; // -(x - x0)
        while x <= x1 {
            let mut y = y0;
            let mut v123 = 0i32; // -(y - y0)
            while y <= y1 {
                let g = &self.grid;
                let (nx, ny) = (g.size[0], g.size[1]);
                // `(-1 - y0 + v123)` = `-1 - y`, `(v139 + v119)` = `-1 - x`.
                let cell_ok =
                    ((-1 - y) >> 31) & ((-1 - x) >> 31) & ((y - ny) >> 31) & ((x - nx) >> 31);
                let cell = (!cell_ok & (ny * nx)) | (cell_ok & (x * g.stride[0] + y * g.stride[1]));
                if self.grid.free_thunk == 0 {
                    let n = self.grid.thunks.len();
                    if n >= 0x10_0000 {
                        // "Error: too many entity grid thunks created, further repositions ignored"
                        self.grid.thunk_error_logged = true;
                        return false;
                    }
                    self.grid.grow_thunks(n + 0x8000);
                }
                let t = self.grid.free_thunk;
                let ti = t as usize;
                let next_free = self.grid.thunks[ti].w8 & 0xFFFFF;
                self.grid.thunks[ti].w8 &= 0xFFF0_0000;
                self.grid.free_thunk = next_free;
                let prev_head = {
                    let ent = self.ent_mut(e);
                    ent.id_bits &= !0x80_0000;
                    let h = ent.grid_thunk & 0xFFFFF;
                    ent.grid_thunk = t;
                    ent.id_bits |= (flags & 4) << 21;
                    h
                };
                self.grid.thunks[ti].w8 |= prev_head;
                let first = self.grid.cell(cell);
                let g = &mut self.grid;
                if first != 0 && g.thunks[first as usize].w8 & 0x70_0000 == 0x50_0000 {
                    // keep simclass-5 thunks at the front: insert after the last of them
                    let mut f = first;
                    if g.thunks[(g.thunks[f as usize].w0 & 0xFFFFF) as usize].w8 & 0x70_0000
                        == 0x50_0000
                    {
                        loop {
                            f = g.thunks[f as usize].w0 & 0xFFFFF;
                            if g.thunks[(g.thunks[f as usize].w0 & 0xFFFFF) as usize].w8 & 0x70_0000
                                != 0x50_0000
                            {
                                break;
                            }
                        }
                    }
                    let th = &mut g.thunks[ti];
                    th.w0 &= 0xFFF0_0000;
                    th.w4 &= 0xFFF0_0000;
                    th.w8 &= !0x80_0000;
                    th.w4 |= f & 0xFFFFF;
                    let nxt = g.thunks[f as usize].w0 & 0xFFFFF;
                    g.thunks[ti].w0 |= nxt;
                    let nn = nxt as usize;
                    g.thunks[nn].w4 = (g.thunks[nn].w4 & 0xFFF0_0000)
                        | (t & ((-(nxt as i32) >> 31) as u32) & 0xFFFFF);
                    g.thunks[f as usize].w0 ^= (t ^ g.thunks[f as usize].w0) & 0xFFFFF;
                } else {
                    {
                        let th = &mut g.thunks[ti];
                        th.w4 &= 0xFFF0_0000;
                        th.w0 &= 0xFFF0_0000;
                        th.w8 |= 0x80_0000;
                        th.w4 |= ((x | (y << 10)) as u32) & 0xFFFFF;
                        th.w0 |= first & 0xFFFFF;
                    }
                    let fi = first as usize;
                    g.thunks[fi].w4 = (g.thunks[fi].w4 & 0xFFF0_0000)
                        | (t & ((-(first as i32) >> 31) as u32) & 0xFFFFF);
                    g.thunks[fi].w8 &= !0x80_0000;
                    *g.cell_mut(cell) = t;
                }
                let g = &mut self.grid;
                let th = &mut g.thunks[ti];
                th.w8 &= 0xFF8F_FFFF;
                th.w8 |= ((simclass & 7) as u32) << 20;
                th.sub[0] = (sub[0][0] as u8) & !((v119 >> 31) as u8);
                th.sub[1] = (sub[0][1] as u8) & !((v123 >> 31) as u8);
                let v87 = x - x1;
                th.sub[2] = (sub[1][0] as u8).wrapping_add(((-1 - sub[1][0]) & (v87 >> 31)) as u8);
                th.sub[3] =
                    (sub[1][1] as u8).wrapping_add(((-1 - sub[1][1]) & ((y - y1) >> 31)) as u8);
                th.z[0] = if iz[0] >= 0 {
                    if iz[0] < 0xFFFF {
                        iz[0] as u16
                    } else {
                        0xFFFF
                    }
                } else {
                    0
                };
                th.ent = e;
                th.z[1] = if iz[1] >= 0 {
                    if iz[1] < 0xFFFF {
                        iz[1] as u16
                    } else {
                        0xFFFF
                    }
                } else {
                    0
                };
                v123 -= 1;
                y += 1;
            }
            x += 1;
            v119 -= 1;
        }
        true
    }

    /// Same cells as before: refresh the sub-cell bytes and z of the existing thunks (walked
    /// from the entity's thunk chain, which runs from the last inserted cell backwards).
    fn grid_refresh(&mut self, e: EntIdx, ixy: [[i32; 2]; 2], sub: [[i32; 2]; 2], iz: [i32; 2]) {
        let (x0, x1, y0, y1) = (ixy[0][0], ixy[1][0], ixy[0][1], ixy[1][1]);
        let mut t = self.ent(e).grid_thunk;
        let mut x = x1;
        let mut v97 = x0 - x1;
        while x >= x0 {
            let mut y = y1;
            let mut v100 = y0 - y1;
            let v101 = v97 >> 31;
            let v102 = (x - x1) >> 31;
            while y >= y0 {
                let th = &mut self.grid.thunks[t as usize];
                th.sub[0] = (sub[0][0] as u8) & !(v101 as u8);
                th.sub[1] = (sub[0][1] as u8) & !((v100 >> 31) as u8);
                th.sub[2] = (sub[1][0] as u8).wrapping_add(((-1 - sub[1][0]) & v102) as u8);
                th.sub[3] =
                    (sub[1][1] as u8).wrapping_add(((-1 - sub[1][1]) & ((y - y1) >> 31)) as u8);
                th.z[0] = if iz[0] >= 0 {
                    if iz[0] < 0xFFFF {
                        iz[0] as u16
                    } else {
                        0xFFFF
                    }
                } else {
                    0
                };
                th.z[1] = if iz[1] >= 0 {
                    if iz[1] < 0xFFFF {
                        iz[1] as u16
                    } else {
                        0xFFFF
                    }
                } else {
                    0
                };
                t = th.w8 & 0xFFFFF;
                y -= 1;
                v100 += 1;
            }
            x -= 1;
            v97 += 1;
        }
    }

    /// The `flags & 2` tail of RepositionEntity: re-list the entity when its simclass differs
    /// from the list it is in (`sub_35077650`), and signal event 2 when monitored.
    fn reposition_lists(&mut self, e: EntIdx, flags: u32, ret: i32) -> i32 {
        if flags & 2 != 0 {
            let ent = self.ent(e);
            let lc = ent.list_class();
            if lc != ent.sim_class() || ((ent.list_bits ^ (ent.list_bits << 4)) & 0xF000_0000) != 0
            {
                self.relist_entity(e);
                if self.ent(e).flags & 0x100_1000 != 0 {
                    self.signal_state_change(e, lc);
                }
            }
        }
        ret
    }
}
