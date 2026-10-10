//! `RayWorldIntersection` (world slot 42, 0x351AA730; arg-list wrapper slot 104, 0x35096EE0)
//! and its helpers: the ray context (0x351AC4F0), the grid walk (0x351AD6A0), the per-cell
//! callback (0x351AC640) and the per-part hit registration (0x351ABC10).
//!
//! # Flow of 0x351AA730 (synchronous path)
//!
//! 1. `|dir|² <= 0` or no temp pools (`world+792`) → 0. Flag 0x800 → queue ([`super::queue`]).
//! 2. `hits[i] = {dist 1e10, +60 0, collider null}` for `i < nMaxHits`; the context is built;
//!    the hit-last cache (`phitLast`: entity, ipart, iNode) is read.
//! 3. `objtypes & 0x100` and a terrain entity for the caller → terrain ray (slot 44), which
//!    may shorten `dir` to the terrain hit.
//! 4. `ndir` of the (possibly shortened) ray; `objtypes & world.waterObjtype & 0x200` → the
//!    water-plane ray (0x351A9540), then `objtypes |= 0x20`.
//! 5. If `objtypes & ~0x300` is non-zero: skip entities get the caller bit (`+64`),
//!    `geomFlagsAll/Any` from the flags, the foreign data of `skip[0]` (entities sharing it
//!    are skipped too), the caller's temp list (its GetEntitiesInBox cache is invalidated),
//!    the grid-space ray (`org − gridOrigin` and `dir` on the grid axes), and:
//!    * hit-last: the cached entity/part is tested through the callback on cell **(0, 0)**
//!      with a fake thunk; if `hits[0].dist > 0` afterwards the ray is cut to `hits[0].pt`
//!      (bug kept: also when nothing was hit, see the module docs of [`super`]);
//!    * the "beyond the ray end" threshold `(len + √2)·len + 1e-4` (double);
//!    * `grid_flags & 1` and a ray start or end outside the grid → the out-of-grid cell;
//!    * the grid walk ("DrawRayOnGrid" limit inside).
//!
//!    Then the caller bits are removed again and, with flag 0x200, the hits are ordered.
//! 6. Hit 0: valid if `dist <= 1e9` (else `dist = -1`, `pt = org + dir`); hits `1..`: valid
//!    if `dist <= 1e9` and not beyond hit 0 (else `dist = -1`). Valid hits on real entities
//!    get `foreignIdx = geom.GetForeignIdx(partid)`, `iPrim = partid`, `partid = part.id`
//!    (`-1, -1` for areas / null colliders / out-of-range parts). Returns the number of valid
//!    hits. The hit-last cache is written back with flag 0x4000.

use super::{aabb_overlap6, map_surface, rwi_flags};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::collision::pairs::box_pairs::overlap_box_ray;
use crate::geometry::primitives::{BoxPrim, RayPrim};
use crate::geometry::shapes::RayGeom;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};
use crate::world::{EntIdx, World};

/// `ray_hit` (80 bytes).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RayHit {
    /// `+0` distance along the ray (`-1` = none).
    pub dist: f32,
    /// `+8` the entity hit (`NONE` = null: water hits on areas without an entity, unused).
    pub collider: EntIdx,
    /// `+16` part index.
    pub ipart: i32,
    /// `+20` part id (the geometry-local primitive id while the query runs; see `iprim`).
    pub partid: i32,
    /// `+24` surface index (material mapping applied).
    pub surface_idx: i16,
    /// `+26` original material id.
    pub idmat_org: i16,
    /// `+28` foreign index of the primitive (geometry slot 18).
    pub foreign_idx: i32,
    /// `+32` BV node.
    pub inode: i32,
    /// `+36` point.
    pub pt: Vec3,
    /// `+48` normal.
    pub n: Vec3,
    /// `+60` 1 for terrain hits; used as the order index while sorting, 0 in valid results.
    pub terrain: i32,
    /// `+64` the primitive index within the geometry.
    pub iprim: i32,
    /// `+72` pool link (opaque; never written by synchronous queries).
    pub next: u64,
}

impl Default for RayHit {
    fn default() -> Self {
        Self {
            dist: 0.0,
            collider: EntIdx::NONE,
            ipart: 0,
            partid: 0,
            surface_idx: 0,
            idmat_org: 0,
            foreign_idx: 0,
            inode: 0,
            pt: Vec3::ZERO,
            n: Vec3::ZERO,
            terrain: 0,
            iprim: 0,
            next: 0,
        }
    }
}

impl RayHit {
    /// The 72-byte copy the hit registration uses to shift hits (`next` stays).
    #[inline]
    fn copy72_from(&mut self, o: &RayHit) {
        let next = self.next;
        *self = *o;
        self.next = next;
    }
}

/// The hit-last cache (`SRWIParams +72`: `{entity*, ipart, iNode}`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct HitLast {
    pub ent: EntIdx,
    pub ipart: i32,
    pub inode: i32,
}

/// `SRWIParams` (96 bytes) without the hit buffer and the hit-last cache, which are passed
/// separately to [`World::rwi`].
#[derive(Clone, Debug, PartialEq)]
pub struct RwiParams {
    /// `+0` / `+8` foreign data (passed through to queued results).
    pub foreign_data: u64,
    pub i_foreign_data: i32,
    /// `+24` origin.
    pub org: Vec3,
    /// `+36` direction (the full segment; its length is the ray length).
    pub dir: Vec3,
    /// `+48` entity types ([`super::ent_types`]).
    pub objtypes: u32,
    /// `+52` flags ([`rwi_flags`]).
    pub flags: u32,
    /// `+88` / `+80` entities to skip (`EntIdx::NONE` entries are allowed). The foreign data
    /// of the first one also excludes every entity sharing it.
    pub skip: Vec<EntIdx>,
}

impl Default for RwiParams {
    fn default() -> Self {
        Self {
            foreign_data: 0,
            i_foreign_data: 0,
            org: Vec3::ZERO,
            dir: Vec3::ZERO,
            objtypes: super::ent_types::ALL,
            flags: 0,
            skip: Vec::new(),
        }
    }
}

/// The fake thunk of the hit-last pass: the entity and its simclass.
#[derive(Clone, Copy, Debug)]
struct FakeThunk {
    ent: EntIdx,
    simclass: u32,
}

/// The ray-test context (`sub_351AC4F0`, 0x220+ bytes on the RWI frame).
#[derive(Debug)]
pub(crate) struct RayCtx {
    /// `+0` placement of the part being tested.
    gwd: GeomWorldData,
    /// `+96` intersection params (defaults; `+98` = flags & 0x8000 is not modelled).
    ip: IntersectionParams,
    /// `+240` nMaxHits, `+244` pierceable hits from the front, `+248` from the back.
    n_max: i32,
    n_front: i32,
    n_back: i32,
    /// `+252` objtypes.
    objtypes: u32,
    /// `+260` hits come from the entity's slot 57 (ipart = contact iNode).
    ent_slot57: i32,
    /// `+264` parts of the entity being tested.
    n_parts: i32,
    /// `+268` flags ^ 0x200.
    flags: u32,
    /// `+272` / `+276`.
    geom_flags_all: u32,
    geom_flags_any: u32,
    /// `+280..+300` the cell-beyond-ray-end test.
    c280: f32,
    c284: f32,
    c288: f32,
    c292: f32,
    len: f32,
    max_proj: f32,
    /// `+328` the caller's temp list (entities tested so far).
    list: Vec<EntIdx>,
    /// `+344` / `+352` skip foreign data.
    skip_fdata: u64,
    skip_ifdata: i32,
    /// `+356` caller.
    caller: i32,
    /// `+368` the ray geometry (`+416` origin, `+428` dir, `+440` ndir).
    ray: RayGeom,
    /// `+480` hit-last fake thunk.
    hit_last: Option<FakeThunk>,
    /// `+520` / `+524` part range of the hit-last pass, `+528` iNode of the solid hit.
    part_lo: i32,
    part_mask: i32,
    inode_out: i32,
}

impl RayCtx {
    /// `sub_351AC4F0` + the fields RWI fills.
    fn new() -> Self {
        Self {
            gwd: GeomWorldData::default(),
            ip: IntersectionParams::default(),
            n_max: 0,
            n_front: 0,
            n_back: 0,
            objtypes: 0,
            ent_slot57: 0,
            n_parts: 0,
            flags: 0,
            geom_flags_all: 0,
            geom_flags_any: 0,
            c280: 0.0,
            c284: 0.0,
            c288: 0.0,
            c292: 0.0,
            len: 0.0,
            max_proj: 0.0,
            list: Vec::new(),
            skip_fdata: 0,
            skip_ifdata: 0,
            caller: 0,
            ray: RayGeom::new(&RayPrim {
                origin: Vec3::ZERO,
                dir: Vec3::ZERO,
            }),
            hit_last: None,
            part_lo: 0,
            part_mask: 0,
            inode_out: 0,
        }
    }

    pub(crate) fn ray_ndir(&self) -> Vec3 {
        self.ray.ndir
    }

    pub(crate) fn ray_prim(&self) -> RayPrim {
        self.ray.prim
    }

    pub(crate) fn set_n_front(&mut self, n: i32) {
        self.n_front = n;
    }

    pub(crate) fn set_n_back(&mut self, n: i32) {
        self.n_back = n;
    }
}

/// What the hit registration needs about the "entity" whose part was tested: the real
/// entity, or the static dummy entity used for area (water) hits (no parts; its part-array
/// flags are written by the area path).
#[derive(Clone, Copy, Debug)]
pub(crate) enum PartSrc {
    Entity(EntIdx),
    /// The static dummy entity (`0x3520C6E0`): 0 parts, its dummy part's flags.
    Dummy {
        part_flags: u32,
    },
}

#[inline]
fn magic_floor_bits(x: f32) -> i32 {
    ((x - 0.5) + 12_582_912.0).to_bits() as i32
}

impl World {
    /// `RayWorldIntersection(SRWIParams&, tag, iCaller)` (world slot 42, 0x351AA730) and the
    /// legacy wrapper slot 104 (0x35096EE0, same parameters as a list).
    ///
    /// * `hits`: the caller's hit buffer (`nMaxHits = hits.len()`; must not be empty). Its
    ///   previous contents matter in one case (the hit-last bug, see [`super`]).
    /// * `hit_last`: the hit-last cache (read; written with [`rwi_flags::UPDATE_LAST_HIT`]).
    /// * `caller`: the physics-thread index of the caller (4 = external, as the game uses).
    ///
    /// Returns the number of valid hits (hit 0 counts if a solid hit was found).
    /// With [`rwi_flags::QUEUE`] use [`World::rwi_queued`] instead (this function then
    /// ignores the flag and runs synchronously).
    pub fn rwi(
        &mut self,
        p: &RwiParams,
        hits: &mut [RayHit],
        hit_last: Option<&mut HitLast>,
        caller: i32,
    ) -> i32 {
        let dir0 = p.dir;
        if ((dir0.x * dir0.x + dir0.y * dir0.y) + dir0.z * dir0.z) <= 0.0
            || !self.temp_pools_ready()
        {
            return 0;
        }
        assert!(!hits.is_empty(), "RWI needs at least one hit slot");
        let n_max = hits.len() as i32;
        let ci = caller.clamp(0, 4) as usize;
        let flags = p.flags & !rwi_flags::QUEUE;
        let org = p.org;
        // v159..v161: the ray direction (terrain may shorten it)
        let mut dir = dir0;
        for h in hits.iter_mut() {
            h.dist = f32::from_bits(0x5015_02F9); // 1e10
            h.terrain = 0;
            h.collider = EntIdx::NONE;
        }
        let mut ctx = RayCtx::new();
        let (mut hl_ent, mut hl_ipart, mut hl_inode) = (EntIdx::NONE, 0i32, 0i32);
        if let Some(h) = hit_last.as_deref() {
            hl_ent = h.ent;
            hl_ipart = h.ipart;
            hl_inode = h.inode;
        }
        let mut objtypes = p.objtypes;
        if objtypes & 0x100 != 0 && !self.terrain[ci].is_none() {
            self.terrain_ray(org, &mut dir, &mut hits[0], flags, ci);
        }
        // ndir of the (possibly shortened) ray
        let l2 = (dir.x * dir.x + dir.y * dir.y) + dir.z * dir.z;
        let ndir = if l2 <= f32::from_bits(0x1E3C_E508) {
            Vec3::new(0.0, 0.0, 1.0)
        } else {
            let inv = 1.0 / l2.sqrt();
            Vec3::new(dir.x * inv, dir.y * inv, dir.z * inv)
        };
        ctx.ray.prim.origin = org;
        ctx.ray.prim.dir = dir;
        ctx.ray.ndir = ndir;
        ctx.n_max = n_max;
        if objtypes & self.water_objtype & 0x200 != 0 {
            self.rwi_water(org, dir, &mut ctx, flags, hits);
            objtypes |= 0x20;
        }
        let mut solid_iprim_cache = (hl_ent, hl_ipart);
        if objtypes & 0xFFFF_FCFF != 0 {
            let bit = 1u32 << caller;
            for &s in &p.skip {
                if !s.is_none() && self.ent_alive(s) {
                    let e = self.ent_mut(s);
                    e.processed = e.processed.wrapping_add(bit & !e.processed);
                }
            }
            ctx.objtypes = objtypes;
            ctx.flags = flags ^ 0x200;
            let mut all = flags >> 16;
            if all == 0 {
                all = 0x8000;
            }
            if flags & 0x40 != 0 {
                all |= 1;
            }
            if flags & 0x400 != 0 {
                ctx.geom_flags_any = all;
                ctx.geom_flags_all = 0;
            } else {
                ctx.geom_flags_all = all;
                ctx.geom_flags_any = 0xFDFF;
            }
            if let Some(&s0) = p.skip.first() {
                if !s0.is_none() && self.ent_alive(s0) {
                    let e = self.ent(s0);
                    ctx.skip_ifdata = i32::from(e.i_foreign_data);
                    ctx.skip_fdata = e.foreign_data;
                }
            }
            ctx.caller = caller;
            // the temp list is shared with GetEntitiesInBox: its cache is invalidated
            self.box_cache[ci].objtypes = -1;
            let g = &self.grid;
            let (ax, ay, az) = (g.iaxis[0], g.iaxis[1], g.axisz);
            let rel = [org.x - g.origin.x, org.y - g.origin.y, org.z - g.origin.z];
            // v177..v179: grid-space origin, v165..v167: grid-space direction
            let mut gorg = [rel[ax], rel[ay], rel[az]];
            let dv = [dir.x, dir.y, dir.z];
            let mut gdir = [dv[ax], dv[ay], dv[az]];
            let (invx, invy) = (g.step_r[0], g.step_r[1]);
            ctx.c280 = 0.5 - invx * gorg[0];
            ctx.c284 = 0.5 - invy * gorg[1];
            ctx.c292 = invy * gdir[1];
            ctx.c288 = invx * gdir[0];
            let mut len = (ctx.c292 * ctx.c292 + ctx.c288 * ctx.c288).sqrt();
            ctx.len = len;
            if !hl_ent.is_none() && self.ent_alive(hl_ent) {
                ctx.hit_last = Some(FakeThunk {
                    ent: hl_ent,
                    simclass: (self.ent(hl_ent).sim_class() as u32) & 7,
                });
                ctx.max_proj = 1.0e20;
                ctx.part_lo = hl_ipart;
                ctx.part_mask = -1;
                let mut end = n_max;
                // bug kept: the cached entity is only reached through cell (0, 0)
                self.ray_cell(&mut ctx, hits, [0, 0], &mut end);
                if !(0.0 >= hits[0].dist) {
                    let pt = hits[0].pt;
                    dir = Vec3::new(pt.x - org.x, pt.y - org.y, pt.z - org.z);
                    ctx.ray.prim.dir = dir;
                    let g = &self.grid;
                    let dv = [dir.x, dir.y, dir.z];
                    gdir = [dv[ax], dv[ay], dv[az]];
                    ctx.c288 = gdir[0] * g.step_r[0];
                    ctx.c292 = gdir[1] * g.step_r[1];
                    len = (ctx.c292 * ctx.c292 + ctx.c288 * ctx.c288).sqrt();
                    ctx.len = len;
                }
                ctx.hit_last = None;
                ctx.part_lo = 0;
                ctx.part_mask = 0;
                // the iStartNode seed (+88) is cleared again
            }
            // (len + sqrt2)·len + 1e-4 in double
            let l = f64::from(len);
            ctx.max_proj = ((l + std::f64::consts::SQRT_2) * l
                + f64::from(f32::from_bits(0x38D1_B717))) as f32;
            if self.vars.grid_flags & 1 != 0 {
                let g = &self.grid;
                let nx = g.size[0] as f32;
                let ny = g.size[1] as f32;
                let out = (((g.step_r[0] * gorg[0]) * 2.0) - nx).abs() > nx
                    || (((g.step_r[1] * gorg[1]) * 2.0) - ny).abs() > ny
                    || ((((gdir[0] + gorg[0]) * g.step_r[0]) * 2.0) - nx).abs() > nx
                    || ((((gdir[1] + gorg[1]) * g.step_r[1]) * 2.0) - ny).abs() > ny;
                if out {
                    let mut end = n_max;
                    self.ray_cell(&mut ctx, hits, [-1, -1], &mut end);
                }
            }
            self.ray_grid_walk(&mut ctx, hits, &mut gorg, &mut gdir);
            // remove the caller bits
            for &e in &ctx.list {
                if self.ent_alive(e) {
                    let ent = self.ent_mut(e);
                    ent.processed = ent.processed.wrapping_add((-1i32 << caller) as u32);
                }
            }
            for &s in &p.skip {
                if !s.is_none() && self.ent_alive(s) {
                    let e = self.ent_mut(s);
                    e.processed = e.processed.wrapping_sub(bit & e.processed);
                }
            }
            if flags & 0x200 != 0 {
                sort_hits(hits, ctx.n_front, ctx.n_back);
            }
        }
        let mut n_valid: i32;
        // hit 0
        if hits[0].dist <= 1.0e9 {
            let (e, valid) = self.hit_part_ok(&hits[0]);
            if valid {
                let part = &self.ent(e).parts[hits[0].ipart as usize];
                let fi = part.pgeom.geom.foreign_idx(hits[0].partid);
                hits[0].foreign_idx = fi;
                hits[0].iprim = hits[0].partid;
                solid_iprim_cache.0 = e;
                solid_iprim_cache.1 = hits[0].ipart;
                hits[0].partid = part.id;
                // `+528`: the iNode of the last solid hit registered through the parts (stale
                // in the original for terrain hits; the port's context starts at 0)
                hl_inode = ctx.inode_out;
            } else {
                hits[0].partid = -1;
                hits[0].foreign_idx = -1;
            }
            n_valid = 1;
        } else {
            hits[0].dist = -1.0;
            n_valid = 0;
            hits[0].pt = Vec3::new(dir.x + org.x, dir.y + org.y, dir.z + org.z);
        }
        for i in 1..hits.len() {
            let d = hits[i].dist;
            if d > 1.0e9 || (hits[0].dist > 0.0 && d > hits[0].dist) {
                hits[i].dist = -1.0;
            } else {
                let (e, valid) = self.hit_part_ok(&hits[i]);
                if valid {
                    let part = &self.ent(e).parts[hits[i].ipart as usize];
                    hits[i].foreign_idx = part.pgeom.geom.foreign_idx(hits[i].partid);
                    hits[i].iprim = hits[i].partid;
                    hits[i].partid = part.id;
                } else {
                    hits[i].partid = -1;
                    hits[i].foreign_idx = -1;
                }
                hits[i].terrain = 0;
                n_valid += 1;
            }
        }
        if let Some(h) = hit_last {
            if flags & rwi_flags::UPDATE_LAST_HIT != 0 {
                h.ent = solid_iprim_cache.0;
                h.ipart = solid_iprim_cache.1;
                h.inode = hl_inode;
            }
        }
        n_valid
    }

    /// Whether `e` refers to a live object of the store.
    #[inline]
    pub(crate) fn ent_alive(&self, e: EntIdx) -> bool {
        (e.0 as usize) < self.ents.len() && self.ents[e.0 as usize].is_some()
    }

    /// The finalisation test: `collider != null && simclass != 5 && ipart < nParts`.
    fn hit_part_ok(&self, h: &RayHit) -> (EntIdx, bool) {
        let e = h.collider;
        if e.is_none() || !self.ent_alive(e) {
            return (e, false);
        }
        let ent = self.ent(e);
        if ent.id_bits & 0xFF00_0000 == 0x0500_0000 || h.ipart >= ent.parts.len() as i32 {
            return (e, false);
        }
        (e, h.ipart >= 0)
    }

    /// `sub_351AD6A0`: walk the grid cells along the ray (`org`, `dir` in grid space, both
    /// clipped to the grid by the function), calling [`World::ray_cell`] until it returns
    /// true or the end cell is reached. Returns 0 when the ray misses the grid, is too short,
    /// or is rejected by the "DrawRayOnGrid" limit.
    #[allow(clippy::too_many_lines)]
    fn ray_grid_walk(
        &mut self,
        ctx: &mut RayCtx,
        hits: &mut [RayHit],
        a2: &mut [f32; 3],
        a3: &mut [f32; 3],
    ) -> i32 {
        if !self.queries.env_byte_289 {
            if let Some(lim) = self.queries.ray_dir_limit {
                let v8 = lim as f32;
                if a3[0].abs() > v8 || a3[1].abs() > v8 {
                    // X2Log(2, "DrawRayOnGrid, dir.x(%.1f), dir.y(%.1f)")
                    return 0;
                }
            }
        }
        let (step, inv, nx, ny) = {
            let g = &self.grid;
            (g.step, g.step_r, g.size[0], g.size[1])
        };
        let v10 = a3[0].to_bits() as i32;
        let v11 = a3[1];
        let v12 = a2[0];
        let v13 = a2[1];
        let s = [
            ((v10 >> 30) & !1) + 1,
            (((v11.to_bits() as i32) >> 30) & !1) + 1,
        ];
        let negx = ((v10 as u32) >> 31) as usize;
        let negy = (v11.to_bits() >> 31) as usize;
        let mut x = [0f32; 4];
        let mut y = [0f32; 4];
        let v15 = s[0] as f32;
        x[2 * negx + 1] = v15 * a3[0];
        x[2 * negx] = -(v15 * v12);
        let v16 = 1 - negx;
        x[2 * v16 + 1] = v15 * a3[0];
        x[2 * v16] = ((nx as f32) * step[0] - v12) * v15;
        let v19 = s[1] as f32;
        y[2 * negy + 1] = v19 * v11;
        y[2 * negy] = -(v19 * v13);
        let v20 = 1 - negy;
        y[2 * v20 + 1] = v19 * v11;
        y[2 * v20] = ((ny as f32) * step[1] - v13) * v19;
        let e20 = f32::from_bits(0x1E3C_E508); // 9.9999997e-21
                                               // tmin = max(tx_in, ty_in, 0)
        let v23 = x[0];
        let tin = if ((y[1] * v23) - (x[1] * y[0])) + ((v23 - y[0]) * e20) <= 0.0 {
            (y[0], y[1])
        } else {
            (x[0], x[1])
        };
        let (v27, v77) = tin;
        let (v31, v32) = if ((-v27) * e20) + ((v77 * 0.0) - v27) <= 0.0 {
            (v27, v77)
        } else {
            (0.0f32, 1.0f32)
        };
        // tmax = min(tx_out, ty_out, 1)
        let tout = if ((x[2] * y[3]) - (y[2] * x[3])) + ((x[2] - y[2]) * e20) >= 0.0 {
            (y[2], y[3])
        } else {
            (x[2], x[3])
        };
        let (v34, v77b) = tout;
        let (v36, v37) = if ((1.0 - v34) * e20) + (v77b - v34) >= 0.0 {
            (v34, v77b)
        } else {
            (1.0f32, 1.0f32)
        };
        let v38 = v32 * v36;
        let v39 = v37 * v31;
        if ((v31 - v36) * e20) + (v39 - v38) >= 0.0 {
            return 0;
        }
        let v40 = v31 - v32 * 0.0;
        let mut l26 = false;
        if v40 > 0.0 {
            let v41 = if v32 == 0.0 { 0.0 } else { v31 / v32 };
            let (v78, v79, v42) = (a3[0], a3[1], a3[2]);
            a2[0] = v41 * v78 + v12;
            a2[1] = v41 * v79 + v13;
            a2[2] += v41 * v42;
            l26 = true;
        }
        if l26 || (v36 - v37) < 0.0 {
            let v43 = v37 * v32;
            let v44 = v38 - v39;
            let v45 = if v43 == 0.0 { 0.0 } else { v44 / v43 };
            let v46 = v45 * a3[0];
            let v47 = v45 * a3[1];
            a3[2] *= v45;
            a3[0] = v46;
            a3[1] = v47;
        }
        // the DDA
        let v48 = a3[1];
        let v49 = nx - 1;
        let v50 = inv[0];
        let v51 = a3[0];
        let v52 = a2[1];
        let v53 = a2[0];
        let v54 = inv[1];
        let v75 = (a3[0].abs() * v50) - (v48.abs() * v54);
        let maj = (v75.to_bits() >> 31) as usize;
        let mnr = usize::from(v75 >= 0.0);
        let v59 = s[mnr];
        // X reused as ints: X[maj] = s[maj], X[mnr] = v59
        let mut xi = [0i32; 2];
        xi[maj] = s[maj];
        xi[mnr] = v59;
        let v60 = a3[maj].abs() * inv[maj];
        let v62 = a3[mnr].abs() * inv[mnr];
        let v72b = magic_floor_bits(v54 * v52);
        let v63b = magic_floor_bits((v52 + v48) * v54);
        let v64 = ny - 1;
        let clampc = |bits: i32, nm1: i32| -> i32 {
            let a = 1_262_485_504i32.wrapping_sub(bits);
            let t = a & (a >> 31);
            let u = nm1 + t;
            (u & (u >> 31)) - t
        };
        let ix0 = clampc(magic_floor_bits(v50 * v53), v49);
        let iy0 = clampc(v72b, v64);
        let a63 = 1_262_485_504i32.wrapping_sub(v63b);
        let t63 = a63 & (a63 >> 31);
        let v55e = v64 + t63;
        let a75 = 1_262_485_504i32.wrapping_sub(magic_floor_bits((v53 + v51) * v50));
        let t75 = a75 & (a75 >> 31);
        let v65 = v49 + t75;
        let end_x = (v65 & (v65 >> 31)) - t75;
        let hi = (((v55e as u32) << 16) & ((v55e >> 15) as u32) & 0xFFFF_0000) as i32;
        let mut end = end_x | (-65536i32).wrapping_mul(t63).wrapping_add(hi);
        let mut cell = [ix0, iy0];
        let packed = |c: [i32; 2]| c[0] | c[1].wrapping_shl(16);
        if self.ray_cell(ctx, hits, cell, &mut end) || (ix0 | iy0.wrapping_shl(16)) == end {
            return 1;
        }
        let v66 = a3[maj];
        if v66.abs() * inv[maj] < f32::from_bits(0x38D1_B717) {
            return 0;
        }
        let v67 = xi[maj];
        let v68 = if v66 == 0.0 {
            0.0
        } else {
            ((((cell[maj] + ((xi[maj] + 1) >> 1)) as f32) * step[maj]) - a2[maj]) / v66
        };
        let mut v69 = ((((v68 * a3[mnr]) + a2[mnr]) * inv[mnr]) - (cell[mnr] as f32))
            * (v59 as f32)
            + (((1 - v59) >> 1) as f32);
        if !(v69 <= 1.0) {
            cell[mnr] += v59;
            if !self.ray_cell(ctx, hits, cell, &mut end) && packed(cell) != end {
                v69 += -1.0;
            } else {
                return 1;
            }
        }
        // LABEL_41
        cell[maj] += v67;
        let mut i = v69 * v60;
        let mut v24: i32 = 0;
        loop {
            if self.ray_cell(ctx, hits, cell, &mut end) {
                break;
            }
            if packed(cell) == end {
                break;
            }
            let v71 = i + ((v24 ^ 1) as f32 * v62);
            v24 = ((v60 - v71).to_bits() >> 31) as i32;
            cell[mnr] += v24 * v59;
            cell[maj] += v67 & (v24 - 1);
            i = v71 - (v24 as f32 * v60);
        }
        1
    }

    /// `sub_351AC640`: test the entities of cell `c` (or, in the hit-last pass, the cached
    /// entity) against the ray. Returns true when the walk should stop: the cell lies beyond
    /// the ray end, or it is the end cell (`*end`, moved to the cell of a solid hit).
    #[allow(clippy::too_many_lines)]
    fn ray_cell(
        &mut self,
        ctx: &mut RayCtx,
        hits: &mut [RayHit],
        c: [i32; 2],
        end: &mut i32,
    ) -> bool {
        let (x, y) = (c[0], c[1]);
        let v7 = x as f32;
        let v8 = y as f32;
        if ((v8 + ctx.c284) * ctx.c292) + ((v7 + ctx.c280) * ctx.c288) > ctx.max_proj
            && (x & y) != -1
        {
            return true;
        }
        // POD activation (vars +360-gated, needs the POD grid): not ported.
        let (nx, ny) = (self.grid.size[0], self.grid.size[1]);
        let in_grid = ((-1 - y) >> 31) & ((-1 - x) >> 31) & ((y - ny) >> 31) & ((x - nx) >> 31);
        let cell_idx = self.grid.cell_index(x, y, 2);
        let mut t = self.grid.cell(cell_idx);
        let hit_last = ctx.hit_last;
        let box_test = (in_grid & i32::from(hit_last.is_none())) != 0;
        let caller = ctx.caller;
        while t != 0 {
            let (ent, simclass, sub, z) = match hit_last {
                Some(f) => {
                    t = 0;
                    (f.ent, f.simclass, [0u8; 4], [0u16; 2])
                }
                None => {
                    let th = self.grid.thunks[t as usize];
                    t = th.w0 & 0xFFFFF;
                    (th.ent, (th.w8 >> 20) & 7, th.sub, th.z)
                }
            };
            if ctx.objtypes & (1u32 << simclass) == 0 {
                continue;
            }
            if (self.ent(ent).processed >> (caller as u32 & 31)) & 1 != 0 {
                continue;
            }
            if ctx.skip_fdata != 0 {
                let e = self.ent(ent);
                let fd = if i32::from(e.i_foreign_data) != ctx.skip_ifdata {
                    0
                } else {
                    e.foreign_data
                };
                if fd == ctx.skip_fdata {
                    continue;
                }
            }
            if box_test {
                let g = &self.grid;
                let (ax, ay, az) = (g.iaxis[0], g.iaxis[1], g.axisz);
                let (sx, sy) = (g.step[0], g.step[1]);
                let zs = g.zstep;
                let o = [g.origin.x, g.origin.y, g.origin.z];
                let v48 = f32::from(sub[0]);
                let v49 = f32::from(sub[1]);
                let v50 = f32::from(z[0]);
                let v52 = f32::from(z[1]);
                let v59 = f32::from(sub[2]) + 1.0;
                let v60 = f32::from(sub[3]) + 1.0;
                let mut center = [0f32; 3];
                let mut size = [0f32; 3];
                center[ax] = ((((v59 + v48) * 0.001_953_125) + v7) * sx) + o[ax];
                center[ay] = ((((v60 + v49) * 0.001_953_125) + v8) * sy) + o[ay];
                center[az] = (((v52 + v50) * 0.5) * zs) + o[az];
                size[ax] = ((v59 - v48) * 0.001_953_125) * sx;
                size[ay] = ((v60 - v49) * 0.001_953_125) * sy;
                size[az] = ((v52 - v50) * 0.5) * zs;
                let bx = BoxPrim {
                    basis: Mat33::IDENTITY,
                    oriented: 0,
                    center: Vec3::new(center[0], center[1], center[2]),
                    size: Vec3::new(size[0], size[1], size[2]),
                };
                if !overlap_box_ray(&bx, &ctx.ray.prim) {
                    continue;
                }
            }
            self.ray_test_entity(ctx, hits, ent, end);
        }
        let v110 = (x | y.wrapping_shl(16)).wrapping_sub(*end);
        v110 == 0
    }

    /// The entity part of the cell callback (from `LABEL_37`).
    fn ray_test_entity(
        &mut self,
        ctx: &mut RayCtx,
        hits: &mut [RayHit],
        ent: EntIdx,
        end: &mut i32,
    ) {
        let caller = ctx.caller;
        ctx.ent_slot57 = 0;
        let mut v71: i32 = 0;
        if self.ent(ent).id_bits & 0xFF00_0000 == 0x0500_0000 {
            // simclass 5: areas (water medium only)
            let is_water = match &self.ent(ent).kind {
                crate::entities::base::EntityKind::Area(a) => a.buoyancy.i_medium == 0,
                _ => false,
            };
            if !is_water {
                return;
            }
            let org = ctx.ray.prim.origin;
            let dir = ctx.ray.prim.dir;
            let Some(h) = self.area_ray(ent, org, dir) else {
                return;
            };
            let contact = GeomContact {
                t: f64::from(h.dist),
                pt: h.pt,
                n: h.n,
                id: [self.water_mat, 0],
                i_node: [-1, 0],
                ..GeomContact::default()
            };
            ctx.n_parts = 0;
            // the dummy part's flags: 0x8000 unless flag 0x1000 (written into the static
            // dummy part array by the original)
            let fl = ctx.flags;
            let part_flags = (((fl & 0x1000) ^ (fl & 0x1000).wrapping_sub(1)) >> 31) & 1;
            let part_flags = part_flags * 0x8000;
            self.particle_tls.dummy_part_flags = part_flags;
            // `(nContacts − 1) >> 31` with one contact is 0
            v71 = 0;
            self.register_ray_contacts(
                ctx,
                hits,
                std::slice::from_ref(&contact),
                PartSrc::Dummy { part_flags },
                EntIdx::NONE,
                0,
                end,
            );
            // the part loop's bound is 0 (or the hit-last part range, not reachable)
            self.ray_list_push(ctx, ent, v71, caller);
            return;
        }
        // CPhysicalEntity path (placeholders not ported: GetEntity = self)
        self.grid_changed = 0;
        if self.ent(ent).deleted != 0 {
            return;
        }
        let nparts = self.ent(ent).parts.len() as i32;
        ctx.n_parts = nparts;
        let mut i = ctx.part_lo;
        let mut first_slot57 = nparts == 0 || self.ent(ent).flags & 0x2000_0000 != 0;
        if first_slot57 {
            i = 0;
        }
        loop {
            if first_slot57 {
                // entity slot 57: base `return 0`; particles 0x3503AB10 (P5); ropes: P6
                first_slot57 = false;
                ctx.ent_slot57 = 1;
                let contacts: Vec<GeomContact> = match self.ent(ent).kind {
                    crate::entities::base::EntityKind::Particle(_) => {
                        crate::entities::particle::ray_trace(self, ent, &ctx.ray)
                            .into_iter()
                            .collect()
                    }
                    _ => Vec::new(),
                };
                let n_contacts = contacts.len() as i32;
                v71 = ctx.part_mask & ((1 - ctx.n_parts) >> 31) & ((n_contacts - 1) >> 31);
                self.register_ray_contacts(ctx, hits, &contacts, PartSrc::Entity(ent), ent, i, end);
            } else {
                let bound = ctx.n_parts + (ctx.part_mask & (ctx.part_lo - ctx.n_parts + 1));
                if i >= bound {
                    break;
                }
                if let Some(n) = self.ray_test_part(ctx, hits, ent, i, end) {
                    v71 = n;
                }
            }
            i += 1;
            let bound = ctx.n_parts + (ctx.part_mask & (ctx.part_lo - ctx.n_parts + 1));
            if i >= bound {
                break;
            }
        }
        self.ray_list_push(ctx, ent, v71, caller);
        ctx.part_lo = 0;
        ctx.part_mask = 0;
    }

    /// The list bookkeeping after an entity: `list[n] = ent; n += v71 + 1;
    /// ent.+64 += (v71 + 1) << caller` (v71 = −1 drops the entry: a hit-last miss on a
    /// multi-part entity leaves it to the grid walk).
    fn ray_list_push(&mut self, ctx: &mut RayCtx, ent: EntIdx, v71: i32, caller: i32) {
        let k = v71 + 1;
        if k == 1 {
            ctx.list.push(ent);
        }
        let e = self.ent_mut(ent);
        e.processed = e.processed.wrapping_add((k << caller) as u32);
    }

    /// One part of an entity (the body of the part loop). Returns the `v71` value of the
    /// call when the part was tested.
    fn ray_test_part(
        &mut self,
        ctx: &mut RayCtx,
        hits: &mut [RayHit],
        ent: EntIdx,
        i: i32,
        end: &mut i32,
    ) -> Option<i32> {
        let e = self.ent(ent);
        let part = &e.parts[i as usize];
        let pf = part.flags;
        if (pf & ctx.geom_flags_all) != ctx.geom_flags_all || (pf & ctx.geom_flags_any) == 0 {
            return None;
        }
        if ctx.n_parts > 1 {
            let b = part.bbox;
            let bx = BoxPrim {
                basis: Mat33::IDENTITY,
                oriented: 0,
                center: Vec3::new(
                    (b[0].x + b[1].x) * 0.5,
                    (b[0].y + b[1].y) * 0.5,
                    (b[0].z + b[1].z) * 0.5,
                ),
                size: Vec3::new(
                    (b[1].x - b[0].x) * 0.5,
                    (b[1].y - b[0].y) * 0.5,
                    (b[1].z - b[0].z) * 0.5,
                ),
            };
            if !overlap_box_ray(&bx, &ctx.ray.prim) {
                return None;
            }
        }
        let r = e.q.rotate(part.pos);
        ctx.gwd.offset = Vec3::new(r.x + e.pos.x, r.y + e.pos.y, r.z + e.pos.z);
        ctx.gwd.r = Mat33::from_quat(e.q.product(part.q));
        ctx.gwd.scale = part.scale;
        let geom = part.pgeom.geom.clone();
        let mut gwd = ctx.gwd;
        let ray = ctx.ray.clone();
        let contacts = self.query_geom_intersect(
            geom.as_ref(),
            &ray,
            &mut gwd,
            &GeomWorldData::default(),
            &mut ctx.ip,
        );
        ctx.gwd = gwd;
        let n = contacts.len() as i32;
        let v71 = ctx.part_mask & ((1 - ctx.n_parts) >> 31) & ((n - 1) >> 31);
        self.register_ray_contacts(ctx, hits, &contacts, PartSrc::Entity(ent), ent, i, end);
        Some(v71)
    }

    /// `sub_351ABC10`: register the contacts of one part test (last to first) as hits.
    #[allow(clippy::too_many_arguments, clippy::too_many_lines)]
    pub(crate) fn register_ray_contacts(
        &mut self,
        ctx: &mut RayCtx,
        hits: &mut [RayHit],
        contacts: &[GeomContact],
        src: PartSrc,
        collider: EntIdx,
        ipart: i32,
        end: &mut i32,
    ) {
        let (ox, oy) = (self.grid.origin, self.grid.step_r);
        let (ax, ay) = (self.grid.iaxis[0], self.grid.iaxis[1]);
        let n_max = ctx.n_max;
        let a5 = ctx.flags;
        let mut k = contacts.len() as i32 - 1;
        while k >= 0 {
            let c = &contacts[k as usize];
            k -= 1;
            if f64::from(hits[0].dist) <= c.t {
                continue;
            }
            let nd = ctx.ray.ndir;
            let v29 = ((nd.y * c.n.y) + (nd.x * c.n.x)) + nd.z * c.n.z;
            if ((a5 & 0x80) as f32 * v29) > 0.0 {
                continue;
            }
            let (surf, part_flags, part_surface_bits) = match src {
                PartSrc::Entity(e) => {
                    let parts = &self.ent(e).parts;
                    let s = map_surface(parts, c.id[0], ipart);
                    let p = parts.get(ipart.max(0) as usize);
                    // a part-less entity (particle, slot 57) points at the static dummy part
                    // `dword_3520C8A0`, whose flags are global state (P5)
                    let dummy = if parts.is_empty() {
                        self.particle_tls.dummy_part_flags
                    } else {
                        0
                    };
                    (s, p.map_or(dummy, |p| p.flags), p.map_or(0, |p| p.surface_bits))
                }
                PartSrc::Dummy { part_flags } => (c.id[0], part_flags, 0),
            };
            let v31 = self.surfaces.flags[(surf & 0x1FF) as usize] & 0xF;
            let v33 = part_flags as i32;
            let m = v33 & 0x8FFB;
            let thr =
                ((16 - v31) & ((-((a5 & 0x1000) as i32)) >> 31) & ((m ^ m.wrapping_sub(1)) >> 31))
                    + v31;
            let slot: usize;
            if ((a5 & 0xF) as i32) < thr {
                // pierceable
                let sflags = self.surfaces.flags[(surf & 0x1FF) as usize];
                if ((a5 as i32 | sflags) & 0x200) == 0 {
                    // from the back of the buffer
                    let mut a15 = n_max - 1;
                    if a15 >= n_max - ctx.n_back {
                        loop {
                            let v41 = a15;
                            if f64::from(hits[v41 as usize].dist) >= c.t {
                                break;
                            }
                            a15 = v41 - 1;
                            if v41 - 1 < n_max - ctx.n_back {
                                break;
                            }
                        }
                    }
                    let v42 = n_max - ctx.n_back;
                    if a15 < v42 {
                        if ctx.n_front + ctx.n_back >= n_max - 1 {
                            continue;
                        }
                    } else {
                        let d = v42 - 1 - (ctx.n_front + 1);
                        let mut v43 = v42 - 1 - (d & (d >> 31));
                        while v43 < a15 {
                            let src_h = hits[(v43 + 1) as usize];
                            hits[v43 as usize].copy72_from(&src_h);
                            v43 += 1;
                        }
                    }
                    let v46 = n_max - ctx.n_front - 1;
                    let d = ctx.n_back + 1 - v46;
                    ctx.n_back = v46 + (d & (d >> 31));
                    slot = a15 as usize;
                } else {
                    // from the front
                    let mut a15 = 1;
                    if ctx.n_front >= 1 {
                        loop {
                            let v34 = a15;
                            if f64::from(hits[v34 as usize].dist) >= c.t {
                                break;
                            }
                            a15 = v34 + 1;
                            if v34 + 1 > ctx.n_front {
                                break;
                            }
                        }
                    }
                    let v35 = ctx.n_front;
                    if a15 > v35 {
                        if v35 + 1 == n_max {
                            continue;
                        }
                    } else {
                        let d = v35 + 1 - (n_max - 1);
                        let mut v36 = (d & (d >> 31)) + n_max - 1 - 1;
                        while v36 >= a15 {
                            let src_h = hits[v36 as usize];
                            hits[(v36 + 1) as usize].copy72_from(&src_h);
                            v36 -= 1;
                        }
                    }
                    let d = ctx.n_front + 1 - (n_max - 1);
                    let v39 = n_max - 1 + (d & (d >> 31));
                    ctx.n_front = v39;
                    let lim = n_max - v39 - 1;
                    let d = ctx.n_back - lim;
                    ctx.n_back = lim + (d & (d >> 31));
                    slot = a15 as usize;
                }
            } else {
                if ((a5 & 0x100) as f32 * v29) > 0.0 {
                    continue;
                }
                // solid hit: the walk ends at the hit's cell, the ray ends at the hit
                let pt = [c.pt.x, c.pt.y, c.pt.z];
                let o = [ox.x, ox.y, ox.z];
                let hy = magic_floor_bits((pt[ay] - o[ay]) * oy[1]);
                let hx = magic_floor_bits((pt[ax] - o[ax]) * oy[0]);
                *end = hy.wrapping_shl(16) | hx.wrapping_sub(1_262_485_504);
                let org = ctx.ray.prim.origin;
                ctx.ray.prim.dir = Vec3::new(c.pt.x - org.x, c.pt.y - org.y, c.pt.z - org.z);
                ctx.inode_out = c.i_node[0];
                slot = 0;
            }
            let h = &mut hits[slot];
            h.dist = c.t as f32;
            h.collider = collider;
            h.ipart = ipart + ((-ctx.ent_slot57) & (c.i_node[0].wrapping_sub(ipart)));
            h.partid = c.i_prim[0];
            h.surface_idx = surf as i16;
            h.idmat_org = super::idmat_org(c.id[0], part_surface_bits);
            h.pt = c.pt;
            h.n = c.n;
            h.inode = c.i_node[0];
            h.terrain = 0;
        }
    }
}

/// The 0x200 ordering at the end of RWI: interleave the front run (`1..=n_front`, ascending)
/// and the back run (`n_max-1` down, `n_back` entries) into order indices (`+60`), mark the
/// unused middle `n_max + 1`, then permute by swapping (cycle sort).
fn sort_hits(hits: &mut [RayHit], n_front: i32, n_back: i32) {
    let n_max = hits.len() as i32;
    let mut idx = [1i32, n_max - 1]; // [front, back]
    let mut order = 1;
    if (n_max - idx[1]) - 1 < n_front + n_back {
        loop {
            let d = hits[idx[1] as usize].dist - hits[idx[0] as usize].dist;
            let a = ((d.to_bits() as i32 | (n_front - idx[0])) as u32 >> 31) as i32;
            let b = ((n_max - idx[1]) - n_back - 1) >> 31;
            let sel = (a & b) as usize;
            let k = idx[sel];
            hits[k as usize].terrain = order;
            idx[sel] = k - 2 * sel as i32 + 1;
            order += 1;
            if (n_max - idx[1]) + idx[0] - 2 >= n_front + n_back {
                break;
            }
        }
    }
    let mut i = n_front + 1;
    while i < n_max - n_back {
        hits[i as usize].terrain = n_max + 1;
        i += 1;
    }
    let mut j = 1;
    while j < n_max {
        let k = hits[j as usize].terrain;
        if k == j || k >= n_max {
            j += 1;
        } else {
            hits.swap(k as usize, j as usize);
        }
    }
}

/// `aabb_overlap6` re-export for the sibling modules.
#[allow(dead_code)]
pub(crate) fn bbox_overlap(a: &[Vec3; 2], b: &[Vec3; 2]) -> bool {
    aabb_overlap6(a, b)
}
