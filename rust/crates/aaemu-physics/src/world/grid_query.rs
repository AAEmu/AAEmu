//! `GetEntitiesInBox` / `GetEntitiesAround` (`sub_35084680`; world slot 40 wraps it with
//! caller 4) and its sort helper `sub_35079CC0`.

use super::grid::aabb_overlap;
use super::{EntIdx, World};
use crate::math::Vec3;

/// The magic-number floor of the grid code: `(int)((x − 0.5) + 12582912.0) − 0x4B400000`.
#[inline]
fn floor_m(x: f32) -> i32 {
    ((x - 0.5) + 12_582_912.0).to_bits() as i32 - 1_262_485_504
}

/// `min(x, lim)` (branch-free form of the binary).
#[inline]
fn min_bf(x: i32, lim: i32) -> i32 {
    let d = lim.wrapping_sub(x);
    x.wrapping_add(d & (d >> 31))
}

/// `max(x, -1)`.
#[inline]
fn max_m1(x: i32) -> i32 {
    let d = -1i32 - x;
    -1 - (d & (d >> 31))
}

impl World {
    /// `sub_35084680` GetEntitiesInBox(min, max, &list, objtypes, petitioner, prealloc,
    /// iCaller): the entities whose grid cells and bbox overlap the box, filtered by
    /// `objtypes & (1 << simclass)`.
    ///
    /// * Per-caller cache: a caller index < 4 asking for the identical box (exact float
    ///   compare of the summed squared differences) with the same objtypes gets the previous
    ///   result list back unchanged — even when entities moved since (kept from the binary).
    /// * Cells are walked `x` outer, `y` inner; within a cell in thunk order (an areas-only
    ///   query, `objtypes == 32`, stops at the first non-area thunk).
    /// * An entity is reported once (the `+64` caller bit), deleted entities are skipped,
    ///   with objtypes `0x10000` only entities with a part flag in `0xFFB`; during a
    ///   flagged-only step (`world+1112`) only entities with flag 0x800 or simclass 0.
    /// * `0x100`: the caller's terrain entity is appended; `0x20000`: static entities are
    ///   moved to the end and the rest sorted by inverse mass; `0x20`: big areas are
    ///   appended.
    /// * Not ported: POD cells (disabled with no POD pages: the lookups hit the dummy cell),
    ///   placeholders (`+56` links), breakable sub-entities, the type-0 bbox events for areas
    ///   flagged in the petitioner's foreign flags.
    pub fn get_entities_in_box(
        &mut self,
        bmin: Vec3,
        bmax: Vec3,
        objtypes: i32,
        petitioner: Option<EntIdx>,
        caller: usize,
    ) -> Vec<EntIdx> {
        // `world+976` (grid) and `world+792` (the per-caller temp lists, allocated with the
        // entity pools at the first TimeStep that adds an ordinary entity): no result before.
        if self.grid.pages.is_none() || self.pool_size == 0 {
            return Vec::new();
        }
        let _ = petitioner;
        self.box_query_count[caller] += 1;
        {
            let c = &self.box_cache[caller];
            let d = |a: f32, b: f32| (a - b) * (a - b);
            let s = ((d(bmin.x, c.bbox[0].x) + d(bmin.y, c.bbox[0].y)) + d(bmin.z, c.bbox[0].z))
                + ((d(bmax.y, c.bbox[1].y) + d(bmax.x, c.bbox[1].x)) + d(bmax.z, c.bbox[1].z));
            if s == 0.0 {
                let v31 = objtypes.wrapping_sub(c.objtypes);
                if v31.wrapping_mul(v31) + ((caller as i32 - 4) >> 31) == -1 {
                    return c.list.clone();
                }
            }
        }
        let g = &self.grid;
        let (ax, ay, az) = (g.iaxis[0], g.iaxis[1], g.axisz);
        let lo = Vec3::new(bmin.x - g.origin.x, bmin.y - g.origin.y, bmin.z - g.origin.z);
        let hi = Vec3::new(bmax.x - g.origin.x, bmax.y - g.origin.y, bmax.z - g.origin.z);
        let v44 = lo[ax] * g.step_r[0];
        let v46 = lo[ay] * g.step_r[1];
        let v271 = lo[az] * g.zstep_r;
        let v269 = hi[ax] * g.step_r[0];
        let v270 = hi[ay] * g.step_r[1];
        let v266 = hi[az] * g.zstep_r;
        let v51 = floor_m(v44);
        let v52 = floor_m(v269);
        let v53 = floor_m(v46);
        let v54 = floor_m(v270);
        let (nx, ny) = (g.size[0], g.size[1]);
        let (mut x0, mut x1, mut y0, mut y1);
        if self.vars.grid_flags & 2 != 0 {
            x0 = max_m1(min_bf(v51, nx));
            x1 = max_m1(min_bf(v52, nx));
            y0 = max_m1(min_bf(v53, ny));
            y1 = max_m1(min_bf(v54, ny));
        } else {
            // clamp to [0, n-1]; a box leaving the grid returns nothing
            let clamp = |v: i32, n: i32| -> (i32, i32) {
                let a = v & !(v >> 31);
                let t = (n - 1 - a) >> 31;
                ((t & (n - 1)) | (a & !t), t | (v >> 31))
            };
            let (a, fa) = clamp(v51, nx);
            let (b, fb) = clamp(v52, nx);
            let (c, fc) = clamp(v53, ny);
            let (d, fd) = clamp(v54, ny);
            if (fa | fb | fc | fd) != 0 {
                return Vec::new();
            }
            x0 = a;
            x1 = b;
            y0 = c;
            y1 = d;
        }
        let ncells = (x1 - x0 + 1) * (y1 - y0 + 1);
        if ncells > self.vars.unk_348 {
            // "GetEntitiesInBox: too many cells requested by %s": shrink to a sqrt(+348)/2
            // square around the centre
            let h = ((self.vars.unk_348 as f32).sqrt() * 0.5) as i32;
            let cx = (x0 + x1) / 2;
            let cy = (y1 + y0) / 2;
            x0 = cx - h - ((cx - h) & ((cx - h) >> 31));
            y0 = cy - h - ((cy - h) & ((cy - h) >> 31));
            let v80 = h + cx;
            x1 = v80 - ((v80 - x0) & ((v80 - x0) >> 31));
            y1 = h + cy - ((h + cy - y0) & ((h + cy - y0) >> 31));
        }
        let bit = 1u32 << caller;
        let mut out: Vec<EntIdx> = Vec::new();
        let areas_only = objtypes == 32;
        let mut x = x0;
        while x <= x1 {
            let mut y = y0;
            while y <= y1 {
                let cell = self.grid.cell_index(x, y, self.vars.grid_flags);
                let mut t = self.grid.cell(cell);
                let sel_of = |w: &World, t: u32| -> bool {
                    (objtypes as u32) & (1u32 << ((w.grid.thunks[t as usize].w8 >> 20) & 7)) != 0
                };
                let mut sel = sel_of(self, t);
                while t != 0 {
                    // `(v116 | v68) == 0`: an areas-only query stops at the first thunk that
                    // is not an area (areas are kept at the front of a cell)
                    if areas_only && !sel {
                        break;
                    }
                    let th = self.grid.thunks[t as usize];
                    t = th.w0 & 0xFFFFF;
                    if sel && self.ent(th.ent).processed & bit == 0 {
                        let v120 = (f32::from(th.sub[2]) + 1.0) * 0.003_906_25 + x as f32;
                        let v121 = (f32::from(th.sub[3]) + 1.0) * 0.003_906_25 + y as f32;
                        let v122 = f32::from(th.z[1]);
                        let v123 = f32::from(th.sub[0]) * 0.003_906_25 + x as f32;
                        let v124 = f32::from(th.z[0]);
                        let v125 = f32::from(th.sub[1]) * 0.003_906_25 + y as f32;
                        let v126 = ((((v270 + v46) - v125) - v121).abs() - (v270 - v46)) - (v121 - v125);
                        let v127 = ((((v269 + v44) - v123) - v120).abs() - (v269 - v44)) - (v120 - v123);
                        let v128 = ((v127 - v126).abs() + (v127 + v126)) * 0.5;
                        let v129 = ((((v266 + v271) - v124) - v122).abs() - (v266 - v271)) - (v122 - v124);
                        if (((v128 - v129).abs() + (v129 + v128)) * 0.5) < 0.0 {
                            let e = th.ent;
                            let eb = self.ent(e).bbox;
                            if aabb_overlap(bmin, bmax, eb[0], eb[1]) {
                                let ent = self.ent(e);
                                let sc = ent.sim_class();
                                if (sc - 5) as u32 <= 1 {
                                    if sc == 5 && ent.deleted == 0 {
                                        out.push(e);
                                        self.ent_mut(e).processed |= bit;
                                    }
                                } else {
                                    let ok_parts = objtypes & 0x10000 == 0
                                        || ent.parts.iter().any(|p| p.flags & 0xFFB != 0);
                                    let v144 = self.flagged_step & (ent.flags ^ 0x800);
                                    if ok_parts && ent.deleted == 0 && (v144 == 0 || sc == 0) {
                                        out.push(e);
                                        self.ent_mut(e).processed |= bit;
                                    }
                                }
                            }
                        }
                    }
                    sel = t != 0 && sel_of(self, t);
                }
                y += 1;
            }
            x += 1;
        }
        for &o in &out {
            self.ent_mut(o).processed &= !bit;
        }
        if objtypes & 0x100 != 0 && !self.terrain[caller].is_none() {
            out.push(self.terrain[caller]);
        }
        if objtypes & 0x20000 != 0 && out.len() > 1 {
            let mut keys: Vec<f32> = out.iter().map(|&o| crate::entities::mass_inv(self, o)).collect();
            sort_static_last(&mut out, &mut keys, self);
        }
        if objtypes & 0x20 != 0 && !self.global_area.is_none() {
            let mut a = self.area(self.global_area).next_big;
            while !a.is_none() {
                let eb = self.ent(a).bbox;
                if self.area(a).deleted == 0 && aabb_overlap(bmin, bmax, eb[0], eb[1]) {
                    out.push(a);
                }
                a = self.area(a).next_big;
            }
        }
        let c = &mut self.box_cache[caller];
        c.bbox = [bmin, bmax];
        c.objtypes = objtypes;
        c.list.clone_from(&out);
        out
    }
}

/// The `0x20000` post-processing of GetEntitiesInBox: entities with `GetMassInv == 0` are
/// moved to the end, then the entities with simclass 0, then the rest is quicksorted by the
/// inverse mass (`sub_35079CC0`, descending).
fn sort_static_last(list: &mut [EntIdx], keys: &mut [f32], w: &World) {
    let n = list.len() as i64;
    let mut last = n - 1;
    let mut i = n - 1;
    while i > 0 {
        if keys[i as usize] == 0.0 {
            if i != last {
                list.swap(i as usize, last as usize);
                keys.swap(i as usize, last as usize);
            }
            last -= 1;
        }
        i -= 1;
    }
    let mut v165 = n - 1;
    let mut v63 = n - 1;
    let mut i = n - 1;
    while i > 0 {
        if w.ent(list[i as usize]).id_bits & 0xFF00_0000 == 0 {
            if i != v63 {
                list.swap(i as usize, v63 as usize);
                keys.swap(i as usize, v63 as usize);
            }
            v165 -= 1;
            v63 -= 1;
        }
        i -= 1;
    }
    if v165 > 0 {
        quicksort_desc(list, keys, 0, v165 as i32);
    }
}

/// `sub_35079CC0`: the engine's quicksort (middle pivot swapped to the front, descending by
/// key, recursion on the left part, loop on the right; a range whose summed `|key − pivot|`
/// is zero is left as is).
pub fn quicksort_desc(list: &mut [EntIdx], keys: &mut [f32], mut lo: i32, hi: i32) {
    while lo < hi {
        let mid = ((lo + hi) >> 1) as usize;
        list.swap(lo as usize, mid);
        keys.swap(lo as usize, mid);
        let pivot = lo as usize;
        let mut diff = 0.0f32;
        let mut store = lo;
        for k in (lo + 1)..=hi {
            let v = keys[k as usize];
            diff += (v - keys[pivot]).abs();
            if v > keys[pivot] {
                store += 1;
                list.swap(store as usize, k as usize);
                keys.swap(store as usize, k as usize);
            }
        }
        list.swap(pivot, store as usize);
        keys.swap(pivot, store as usize);
        if diff <= 0.0 {
            break;
        }
        quicksort_desc(list, keys, lo, store - 1);
        lo = store + 1;
    }
}
