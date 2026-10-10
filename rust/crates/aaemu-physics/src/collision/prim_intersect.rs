//! The shared primitive `Intersect` — geometry vtable slot 10 of `CBoxGeom`, `CSphereGeom`,
//! `CCylinderGeom` and `CCapsuleGeom` (x64 0x35151F20, IDA `CCylinderGeom__vf10`, 7422 bytes;
//! x86 1.2 0x3515A6D0), ported from the x64 disassembly.
//!
//! `int Intersect(IGeometry *other, geom_world_data *gwd1, geom_world_data *gwd2,
//! intersection_params *ip, geom_contact *&pcontacts)` → number of contacts.
//!
//! # Flow (addresses are x64)
//!
//! 1. `other->IsAPrimitive()` (slot 47) == 0 → the generic `CGeometry::Intersect` 0x3514FD70
//!    (BV-tree traversal, owned by P1c; reached through [`GenericIntersectFn`], which returns
//!    no contacts until it is provided).
//! 2. Per-thread state ([`ThreadScratch`]): if `!ip.bKeepPrevContacts` (+97) the border-point,
//!    contact, area-point and area counters are zeroed. Gate: return 0 (nothing written) unless
//!    `nContacts < 64` and `nBorderPts < 2040`, evaluated as `max(nb − 2040, nc − 64) < 0`
//!    with 32-bit wrapping. `*pcontacts = &contacts[nc]`; that record's `ptborder =
//!    &border[nb]`, `nborderpt = 0`, `idxborder = NULL`; `ip+128 = contacts` (not modelled);
//!    the BV pool counter, the OVERLAP scratch and the prepared-primitive ring counters are
//!    reset (the port computes the BVs directly, see below).
//! 3. Bounding volumes and the OVERLAP pre-check: A's single-box tree box placed by `gwd1`
//!    (BV slot 9) — swept along `v̂·L` (BV slot 8, `sweep_box`) for a sweep test — against B's
//!    BV (box, or the ray itself for `CRayGeom`); `OVERLAP[typeBV_A][typeBV_B]`; 0 → return 0.
//! 4. `gwd1.offset += v̂·L`, prepare A (slot 48), prepare B, `gwd1.offset −= v̂·L` (`v̂·L = 0`
//!    without a sweep). **Bug kept:** the round trip is a float add/sub through the caller's
//!    `geom_world_data`, so the caller sees `-0.0` offsets become `+0.0` (and, for sweeps, one
//!    rounding step of change); [`intersect`] therefore takes `gwd_a` mutably.
//! 5. B with collision priority 0 (a ray): `INTERSECT[tA][ray]`, then `CRayGeom::
//!    RegisterIntersection` (0x3519A380) fills the contact (no unprojection), see
//!    [`register_ray`]; returns 1.
//! 6. Otherwise `unproj_params`: `minPtDist = min_idiom(A.minVtxDist, B.minVtxDist)`,
//!    `maxcos = 1 − gap²·0.5`, `tmin = 0`, `vel = 0`, `flag = 0`; a contact area from the pool
//!    unless `bNoAreaContacts` (+99) or the pool is exhausted (32 areas / 256 points, unsigned
//!    compares).
//! 7. Sweep (+96): `UNPROJECT[0][tA][tB]` back along `−v̂` from the end pose with `tmax = L`,
//!    tunnelling back-up (the same algorithm as [`crate::collision::swept::sweep`], here inline
//!    so that the raw return value and the shared `unproj_params`/result are kept), contact
//!    `t = L − t`.
//! 8. No sweep: `INTERSECT[tA][tB]` (skipped if `ip+108` is set or the cell is `ret0`) writes
//!    the border points straight into the border pool at `&border[nb]` (no capacity check —
//!    see "overruns"); contact `nborderpt` = their count, `center` = their mean, the contact
//!    point = the first border point (0 without points), `n` = the pair's normal.
//!    * `iUnprojectionMode != 0` → rotational unprojection (`UNPROJECT[1]`) about the ip
//!      centre / axis; a zero (or NaN) axis is replaced by `n × (pt₀ − centre)` (`n × raydir`
//!      for rays; the orthogonal of `n` if that is < 1e-6) normalised; `t = atan2(sin, cos)`.
//!      (For the four solid primitives every mode-1 cell is `ret0`, so this yields no contact.)
//!    * mode 0 and `vrel_min < 1e9`: the contact point (the first border point, or — when there
//!      are none and `|wA|² + |wB|² > 0` — from slot 11 `FindClosestPoint` between each
//!      geometry and the other's BV centre, checked with slot 8 `PointInsideStatus`), the
//!      relative velocity there; if `|vrel|² > vrel_min²` linear unprojection along `−v̂rel`
//!      with `tmax = |vrel|·time_interval`, accepted only if `t < tmax`.
//!    * otherwise / on failure: minimum-distance unprojection (`dir = 0`, `tmax = maxUnproj`,
//!      `flag = ip+108 | box–capsule | capsule–box`).
//! 9. Contact record: `t`, `pt`, `n` normalised (`(0,0,1)` if `|n|² ≤ 1e-20`), `dir`, mode,
//!    `vel` from the params, `id = (−1, −1)`, `iPrim = (0, 0)`, features from the
//!    unprojection, `iNode = (−1, −1)`; an area with points is attached (area and area-point
//!    counters advance) and replaces the border when the border has fewer points and is empty
//!    or A or B is a cylinder (centre = mean of the area points); an empty border becomes
//!    `[pt]` written through `ptborder`; `bBorderConsecutive = 0` (the byte only);
//!    `nContacts += 1`, `nBorderPts += nborderpt` (**bug kept:** also when the border points
//!    are the area's, which live in the area pool). Returns the unprojection's return value
//!    (`& 1` on the sweep / velocity paths, raw on the rotational and minimum-distance paths).
//!
//! # Per-thread state
//!
//! Everything the function keeps between calls lives in the caller's [`ThreadScratch`] (one
//! per physics thread): 64 contact records, 32 areas, 256 area points (+ 4 int arrays),
//! 2048 border points, and their counters. Contacts produced while `bKeepPrevContacts` is set
//! accumulate; returned contacts are records of that pool (`IntersectOut::first`), and the
//! border points / areas they point to stay valid until the counters are reset.
//!
//! # Overruns (reproduced, not clamped)
//!
//! The INTERSECT pair functions ignore `prim_inters.nbordersz` (+68, which this caller leaves
//! uninitialised anyway): box–box emits up to 48 border points, box–cylinder/capsule more. The
//! destination is the per-thread border pool at index `nb` (< 2040 by the gate), so a call can
//! write past the pool's 2048 entries — the first word past the pool is the border-point
//! **counter**, which the pair's x coordinate overwrites before the caller adds `nborderpt` to
//! it. Likewise the unprojection area writers ignore `nmaxpt`: past the 256 area points come
//! the `piPrim[0]` pool, … and past `piFeature[1]` the area-point counter and then the border
//! pool. [`ThreadScratch`] models these pools as one word array with the original layout, so
//! every such overwrite (and its effect on later calls) is reproduced. (The 15-point buffer the
//! task mentions belongs to the tree traversal 0x3514DFD0, not to this function.)
//!
//! # Stale reads
//!
//! Some locals are read without being initialised on paths the solid primitives can reach:
//! the `prim_inters` points / feature bytes / `nbordersz` / alternative normal, the
//! `unproj_result` fields other than `t`, the `unproj_params` centre (read by no linear cell)
//! and, on the rotational path, `tmax` and the mode word before it is set. The port gives them
//! the bits of [`ThreadScratch::stale_word`] (the oracle fills the stack with that word before
//! the call). None of them was observable: 60 000 oracle cases run with a stale word different
//! from the one the DLL saw still matched every output word.
//!
//! # Not reached / deviations
//!
//! * `this` = `CRayGeom` never reaches 0x35151F20 (the ray's slot 10 is the base 0x3514FD70).
//! * A `NULL` `gwd`/`ip` (the original substitutes the statics 0x35209C40 — identity placement
//!   — and 0x35209CA0 — default params) is expressed by passing those values.
//! * `ip+128` (the contact pool pointer the function stores into the params) is not modelled:
//!   it is always the start of the caller's pool.

// `!(a >= b)` is deliberate: the binary's `comiss` + `jae`/`jbe` outcome for NaN operands.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

pub mod scratch;

pub use scratch::ThreadScratch;

use crate::collision::contact::{
    GeomContact, IntersectionParams, PrimInters, UnprojParams, UnprojResult,
};
use crate::collision::pairs::{intersect_none, PairTables};
use crate::collision::swept::common::normalize_in_place;
use crate::collision::swept::UnprojTables;
use crate::geometry::bvtree::swept::{sweep_box, transform_box};
use crate::geometry::primitives::{BoxPrim, Primitive, RayPrim};
use crate::geometry::shapes::ray::ray_ndir;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{crt, Vec3};
use scratch::{a as ar, c, BORDER};
use std::sync::OnceLock;

/// What 0x35151F20 returns: the contact count (`ret`) and where `*pcontacts` points (the index
/// of the record in the thread's contact pool; `None` when the function returned before
/// setting it). The records `first .. first + ret` are the new contacts
/// ([`ThreadScratch::contacts`]).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct IntersectOut {
    pub ret: i32,
    pub first: Option<i32>,
}

/// The generic `CGeometry::Intersect` (x64 0x3514FD70, BV-tree traversal; owned by P1c), which
/// the primitive `Intersect` falls back to when `other` is not a primitive. It shares the
/// thread's scratch. Signature: `(scratch, env, this, other, gwd1, gwd2, ip)`.
pub type GenericIntersectFn = fn(
    &mut ThreadScratch,
    &PrimIntersectEnv,
    &dyn Geometry,
    &dyn Geometry,
    &mut GeomWorldData,
    &GeomWorldData,
    &mut IntersectionParams,
) -> IntersectOut;

/// The dispatch tables the function uses, plus the generic fallback.
#[derive(Clone, Copy)]
pub struct PrimIntersectEnv {
    pub pairs: PairTables,
    pub unproj: UnprojTables,
    /// `CGeometry::Intersect`; `None` → the fallback produces no contacts (until P1c).
    pub generic: Option<GenericIntersectFn>,
}

impl PrimIntersectEnv {
    /// The binary's tables, no generic fallback.
    #[must_use]
    pub fn new() -> Self {
        Self {
            pairs: PairTables::build(),
            unproj: UnprojTables::build(),
            generic: None,
        }
    }
}

impl Default for PrimIntersectEnv {
    fn default() -> Self {
        Self::new()
    }
}

impl std::fmt::Debug for PrimIntersectEnv {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("PrimIntersectEnv")
            .field("generic", &self.generic.is_some())
            .finish_non_exhaustive()
    }
}

// ------------------------------------------------------------------------------------------
// helpers

#[inline]
fn sgn(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

/// `(x − 1) ^ x` has its sign bit set iff `x == 0` (or `x == i32::MIN`).
#[inline]
fn is_zero_idiom(x: i32) -> i32 {
    ((x.wrapping_sub(1) ^ x) >> 31).wrapping_neg()
}

fn stale_vec(w: u32) -> Vec3 {
    let f = f32::from_bits(w);
    Vec3::new(f, f, f)
}

/// The world-space point `p` in a geometry's frame as slot 8 gets it: `Rᵀ·(p − offset)·(1/s)`
/// with `1/s = 1` when `s == 1` **or NaN** (`ucomiss` + `je`).
fn to_local(p: Vec3, g: &GeomWorldData) -> Vec3 {
    let inv = if g.scale == 1.0 || g.scale.is_nan() {
        1.0
    } else {
        1.0 / g.scale
    };
    let r = &g.r.m;
    let d = Vec3::new(p.x - g.offset.x, p.y - g.offset.y, p.z - g.offset.z);
    Vec3::new(
        ((d.y * r[1][0] + d.x * r[0][0]) + d.z * r[2][0]) * inv,
        inv * ((d.x * r[0][1] + d.y * r[1][1]) + d.z * r[2][1]),
        inv * ((d.x * r[0][2] + d.y * r[1][2]) + d.z * r[2][2]),
    )
}

/// The BV primitive of a primitive geometry placed by `g` (slot 46 → BV slot 9): the
/// single-box tree's box transformed (`CSingleBoxTree` 0x351AE3F0), or the ray itself
/// (`CRayBV` 0x35199DC0).
fn node_bv(geom: &dyn Geometry, g: &GeomWorldData) -> Primitive {
    if let Some(Primitive::Ray(r)) = (geom.geom_type() == crate::ids::GeomType::Ray)
        .then(|| geom.primitive(0))
        .flatten()
    {
        return Primitive::Ray(RayPrim {
            origin: g.r.mul_vec(r.origin) * g.scale + g.offset,
            dir: g.r.mul_vec(r.dir) * g.scale,
        });
    }
    let bb = geom.bbox();
    Primitive::Box(transform_box(
        &bb.basis, bb.center, bb.size, &g.r, g.offset, g.scale,
    ))
}

/// The `prim_inters` record as the function leaves it before INTERSECT: `n = 0`,
/// tolerance 0, `id = (−1, −1)`, `iNode = (0, 0)`, empty border; the rest stale.
fn fresh_inters(w: u32) -> PrimInters {
    let b = w.to_le_bytes();
    PrimInters {
        pt: [stale_vec(w); 2],
        n: Vec3::ZERO,
        i_feature: [[b[0], b[1]], [b[2], b[3]]],
        tolerance: 0.0,
        id: [-1, -1],
        i_node: [0, 0],
        border: Vec::new(),
        border_max: w as i32,
        n_alt: stale_vec(w),
        unk_84: w as i32,
    }
}

/// Call `UNPROJECT[mode][tA][tB]` with the area record at `area` (arena offset) loaded from
/// and stored back to the scratch.
#[allow(clippy::too_many_arguments)]
fn unproject(
    env: &PrimIntersectEnv,
    s: &mut ThreadScratch,
    mode: usize,
    p: &mut UnprojParams,
    pa: &Primitive,
    pb: &Primitive,
    y: &mut UnprojResult,
    area: Option<i64>,
) -> i32 {
    let before = area.map(|o| s.load_area(o));
    let mut a = before.clone();
    let r = env.unproj.unproject(mode, p, pa, -1, pb, -1, y, a.as_mut());
    if let (Some(o), Some(b), Some(a)) = (area, before, a) {
        s.store_area(o, &b, &a);
    }
    r
}

// ------------------------------------------------------------------------------------------
// the function

/// The primitive `Intersect` (0x35151F20) of `a` (box, sphere, cylinder or capsule) against
/// `b`. See the module docs; `gwd_a` is written back like the original's (`-0.0` → `+0.0`
/// offsets, sweep round trip).
#[allow(clippy::too_many_lines)]
pub fn intersect(
    env: &PrimIntersectEnv,
    s: &mut ThreadScratch,
    a: &dyn Geometry,
    b: &dyn Geometry,
    gwd_a: &mut GeomWorldData,
    gwd_b: &GeomWorldData,
    ip: &mut IntersectionParams,
) -> IntersectOut {
    // 1. not a primitive: the generic CGeometry::Intersect
    if !b.is_a_primitive() {
        return match env.generic {
            Some(f) => f(s, env, a, b, gwd_a, gwd_b, ip),
            None => IntersectOut::default(),
        };
    }
    let w = s.stale_word;

    // 2. per-thread counters, gate, the contact slot
    if !ip.keep_prev_contacts {
        s.set_n_border(0);
        s.set_n_contacts(0);
        s.set_n_area_pts(0);
        s.set_n_areas(0);
    }
    let nc = s.n_contacts();
    let x = s.n_border().wrapping_sub(2040);
    let d = x.wrapping_sub(nc.wrapping_sub(64));
    let m = (d >> 31) & d;
    if x.wrapping_sub(m) >= 0 {
        return IntersectOut::default();
    }
    let slot = ThreadScratch::contact_off(nc);
    let first = Some(nc);
    let out0 = IntersectOut { ret: 0, first };
    let nb = s.n_border();
    let pborder = s.addr(i64::from(BORDER) + 12 * i64::from(nb));
    s.wr_q(slot + i64::from(c::PTBORDER), pborder);
    s.wr(slot + i64::from(c::NBORDER), 0);
    s.wr_q(slot + i64::from(c::IDXBORDER), 0);

    // 3. bounding volumes, OVERLAP
    let (mut vh, mut l) = (Vec3::ZERO, 0.0f32);
    let bb_a = a.bbox();
    let bv_a_unswept = transform_box(
        &bb_a.basis,
        bb_a.center,
        bb_a.size,
        &gwd_a.r,
        gwd_a.offset,
        gwd_a.scale,
    );
    let bv_a = if ip.sweep_test {
        let v = gwd_a.v;
        let len = ((v.x * v.x + v.y * v.y) + v.z * v.z).sqrt();
        let inv = 1.0 / len;
        l = len * ip.time_interval;
        vh = Vec3::new(v.x * inv, inv * v.y, inv * v.z);
        sweep_box(&bv_a_unswept, vh, l)
    } else {
        bv_a_unswept
    };
    let bv_a = Primitive::Box(bv_a);
    let bv_b = node_bv(b, gwd_b);
    if !(env.pairs.overlap_fn(bv_a.geom_type(), bv_b.geom_type()))(&bv_a, &bv_b) {
        return out0;
    }

    // 4. prepare both primitives (A at the sweep end pose)
    gwd_a.offset = Vec3::new(
        vh.x * l + gwd_a.offset.x,
        vh.y * l + gwd_a.offset.y,
        vh.z * l + gwd_a.offset.z,
    );
    let pa = a.prepare_primitive(gwd_a);
    let pb = b.prepare_primitive(gwd_b);
    gwd_a.offset = Vec3::new(
        gwd_a.offset.x - vh.x * l,
        gwd_a.offset.y - vh.y * l,
        gwd_a.offset.z - vh.z * l,
    );
    let (Some(pa), Some(pb)) = (pa, pb) else {
        return out0;
    };
    let ta = pa.geom_type();
    let tb = pb.geom_type();
    let (tai, tbi) = (ta.id(), tb.id());

    // 5. B is a ray
    if b.priority() == 0 {
        let mut inters = fresh_inters(w);
        if !(env.pairs.intersect_fn(ta, tb))(&pa, &pb, &mut inters) {
            return out0;
        }
        register_ray(s, nc, b, gwd_b, &inters);
        s.set_n_contacts(s.n_contacts().wrapping_add(1));
        // the caller's fix-ups on the record (two swaps, the second one reading the already
        // swapped word: id = (id[1], −1), iPrim = (−1, 0)), then n is negated back
        let id0 = s.rd(slot + i64::from(c::ID));
        let id1 = s.rd(slot + i64::from(c::ID) + 4);
        s.wr(slot + i64::from(c::ID), id1);
        s.wr(slot + i64::from(c::ID) + 4, id0);
        let p0 = s.rd(slot + i64::from(c::IPRIM));
        s.wr(
            slot + i64::from(c::IPRIM),
            s.rd(slot + i64::from(c::ID) + 4),
        );
        s.wr(slot + i64::from(c::IPRIM) + 4, p0);
        for k in 0..3 {
            let o = slot + i64::from(c::N) + 4 * k;
            s.wr(o, s.rd(o) ^ 0x8000_0000);
        }
        return IntersectOut { ret: 1, first };
    }

    // 6. unprojection parameters, contact area
    let (ma, mb) = (a.min_vtx_dist(), b.min_vtx_dist());
    let gap = ip.max_surface_gap_angle;
    let mut p = UnprojParams {
        mode: w as i32,
        dir: stale_vec(w),
        center: stale_vec(w),
        vel: 0.0,
        tmax: f32::from_bits(w),
        tmin: 0.0,
        min_pt_dist: ((ma + mb) - (ma - mb).abs()) * 0.5,
        max_cos: 1.0 - (gap * gap) * 0.5,
        flag: 0,
    };
    let mut y = UnprojResult {
        t: 0.0,
        cos_t: f64::from_bits(u64::from(w) | (u64::from(w) << 32)),
        pt: stale_vec(w),
        n: stale_vec(w),
        i_feature: [w as i32; 2],
    };
    let area =
        if ip.no_area_contacts || (s.n_areas() as u32) >= 32 || (s.n_area_pts() as u32) >= 256 {
            None
        } else {
            let ao = ThreadScratch::area_off(s.n_areas());
            let used = i64::from(s.n_area_pts());
            let pool = |base: u32, k: i64| s.addr(i64::from(base) + k * used);
            let (ppt, p0, f0, p1, f1) = (
                pool(scratch::AREA_PTS, 12),
                pool(scratch::AREA_PRIM0, 4),
                pool(scratch::AREA_FEAT0, 4),
                pool(scratch::AREA_PRIM1, 4),
                pool(scratch::AREA_FEAT1, 4),
            );
            s.wr_q(ao + i64::from(ar::PT), ppt);
            s.wr_q(ao + i64::from(ar::PRIM0), p0);
            s.wr_q(ao + i64::from(ar::FEAT0), f0);
            s.wr_q(ao + i64::from(ar::PRIM1), p1);
            s.wr(ao + i64::from(ar::NPT), 0);
            s.wr_q(ao + i64::from(ar::FEAT1), f1);
            s.wr(ao + i64::from(ar::NMAX), 256u32.wrapping_sub(used as u32));
            s.wr(ao + i64::from(ar::MINEDGE), 0);
            s.wr_v(ao + i64::from(ar::N), Vec3::ZERO);
            Some(ao)
        };

    let mut r9: i32;
    let mut ta_final = tai;
    if ip.sweep_test {
        // 7. sweep
        p.vel = l;
        p.tmax = l;
        p.dir = Vec3::new(-vh.x, -vh.y, -vh.z);
        p.mode = 0;
        p.flag = 1;
        let r = unproject(env, s, 0, &mut p, &pa, &pb, &mut y, area);
        let diff = (y.t - f64::from(p.tmax)) as f32;
        r9 = r & sgn(diff);
        if r9 == 0 {
            return out0;
        }
        let dot = |n: Vec3, d: Vec3| (n.y * d.y + d.x * n.x) + n.z * d.z;
        if dot(y.n, p.dir) > 0.0 {
            // tunnelled through B: back up along the sweep and unproject again
            let t0 = y.t;
            let bbox_b: BoxPrim = b.bbox();
            let r = &gwd_b.r.m;
            let wv = Vec3::new(
                (vh.y * r[1][0] + vh.x * r[0][0]) + vh.z * r[2][0],
                (vh.y * r[1][1] + vh.x * r[0][1]) + vh.z * r[2][1],
                (vh.y * r[1][2] + vh.x * r[0][2]) + vh.z * r[2][2],
            );
            let bb = &bbox_b.basis.m;
            let loc = [
                (bb[0][1] * wv.y + bb[0][0] * wv.x) + bb[0][2] * wv.z,
                (bb[1][1] * wv.y + bb[1][0] * wv.x) + bb[1][2] * wv.z,
                (bb[2][1] * wv.y + bb[2][0] * wv.x) + bb[2][2] * wv.z,
            ];
            let sz = [bbox_b.size.x, bbox_b.size.y, bbox_b.size.z];
            let arr = [
                (loc[0].abs() * sz[1]) * sz[2],
                (loc[1].abs() * sz[0]) * sz[2],
                (loc[2].abs() * sz[0]) * sz[1],
            ];
            let c1 = sgn(arr[0] - arr[1]);
            let c2 = sgn(arr[c1 as usize] - arr[2]);
            let mm = c1 | (c2 << 1);
            let i = ((((mm ^ 2) | 4) >> 1) & mm) as usize;
            let tb = t0 + f64::from((sz[i] / loc[i].abs()) * 0.1);
            let lb = (f64::from(l) - tb) as f32;
            p.tmax = lb;
            gwd_a.offset = Vec3::new(
                vh.x * lb + gwd_a.offset.x,
                lb * vh.y + gwd_a.offset.y,
                lb * vh.z + gwd_a.offset.z,
            );
            let pa2 = a.prepare_primitive(gwd_a);
            gwd_a.offset = Vec3::new(
                gwd_a.offset.x - vh.x * lb,
                gwd_a.offset.y - lb * vh.y,
                gwd_a.offset.z - lb * vh.z,
            );
            let Some(pa2) = pa2 else {
                return out0;
            };
            ta_final = pa2.geom_type().id();
            let mode = p.mode as usize & 1;
            let r2 = unproject(env, s, mode, &mut p, &pa2, &pb, &mut y, area);
            let diff2 = (y.t - f64::from(p.tmax)) as f32;
            let d2 = dot(y.n, p.dir);
            y.t += tb;
            p.tmax = l;
            r9 = r2 & sgn(diff2) & sgn(d2);
        }
    } else {
        // 8. contact generation
        let mut pt = Vec3::ZERO;
        let mut n = Vec3::ZERO;
        let mut inters = fresh_inters(w);
        if !ip.no_intersection {
            let f = env.pairs.intersect_fn(ta, tb);
            if !std::ptr::eq(f as *const (), intersect_none as *const ()) {
                let ok = f(&pa, &pb, &mut inters);
                // the pair writes its border points straight into the pool
                for (k, q) in inters.border.iter().enumerate() {
                    s.wr_v_at(pborder.wrapping_add(12 * k as u64), *q);
                }
                if !ok {
                    return out0;
                }
                let cnt = inters.border.len() as i32;
                s.wr(slot + i64::from(c::NBORDER), cnt as u32);
                s.wr_v(slot + i64::from(c::CENTER), Vec3::ZERO);
                if cnt > 0 {
                    pt = s.rd_v_at(pborder);
                    for k in 0..cnt {
                        let pb_now = s.rd_q(slot + i64::from(c::PTBORDER));
                        let q = s.rd_v_at(pb_now.wrapping_add(12 * k as u64));
                        let cc = s.rd_v(slot + i64::from(c::CENTER));
                        s.wr_v(
                            slot + i64::from(c::CENTER),
                            Vec3::new(q.x + cc.x, q.y + cc.y, q.z + cc.z),
                        );
                    }
                    let k = 1.0 / cnt as f32;
                    let cc = s.rd_v(slot + i64::from(c::CENTER));
                    s.wr_v(
                        slot + i64::from(c::CENTER),
                        Vec3::new(k * cc.x, k * cc.y, k * cc.z),
                    );
                }
                n = inters.n;
            }
        }

        if ip.i_unprojection_mode != 0 {
            // rotational unprojection about the ip centre / axis
            p.mode = 1;
            p.center = ip.center_of_rotation;
            let ax = ip.axis_of_rotation;
            let l2 = (ax.x * ax.x + ax.y * ax.y) + ax.z * ax.z;
            if l2 != 0.0 && !l2.is_nan() {
                p.dir = ax;
            } else {
                let cr = if tbi == 3 {
                    let e = match pb {
                        Primitive::Ray(r) => r.dir,
                        _ => Vec3::ZERO,
                    };
                    Vec3::new(
                        e.z * n.y - e.y * n.z,
                        e.x * n.z - e.z * n.x,
                        e.y * n.x - e.x * n.y,
                    )
                } else {
                    // pt[0] of the pair (stale if the pair did not write it)
                    let q = inters.pt[0];
                    let dd = Vec3::new(q.x - p.center.x, q.y - p.center.y, q.z - p.center.z);
                    Vec3::new(
                        dd.z * n.y - dd.y * n.z,
                        n.z * dd.x - dd.z * n.x,
                        n.x * dd.y - n.y * dd.x,
                    )
                };
                let mut dir = cr;
                let len2 = (cr.y * cr.y + cr.x * cr.x) + cr.z * cr.z;
                if !(len2 >= f32::from_bits(0x3586_37BD)) {
                    let k = f32::from_bits(0x3F4F_5C28);
                    let t = ((n.y * n.y + n.x * n.x) + n.z * n.z) * k - n.x * n.x;
                    let i = (t.to_bits() >> 31) as usize;
                    let (inc, dec) = ([1usize, 2, 0][i], [2usize, 0, 1][i]);
                    let nn = [n.x, n.y, n.z];
                    let mut v = [dir.x, dir.y, dir.z];
                    v[i] = 0.0;
                    v[inc] = nn[dec];
                    v[dec] = -nn[inc];
                    // the stack temporary starts stale; all three lanes are written
                    dir = Vec3::new(v[0], v[1], v[2]);
                }
                normalize_in_place(&mut dir);
                p.dir = dir;
            }
            r9 = unproject(env, s, 1, &mut p, &pa, &pb, &mut y, area);
            if r9 != 0 {
                y.t = crt::atan2(y.t, y.cos_t);
            }
        } else if !(f32::from_bits(0x4E6E_6B28) > ip.vrel_min) {
            r9 = 0;
        } else {
            // the contact point
            let wa = gwd_a.w;
            let wb = gwd_b.w;
            let wsum = ((wa.x * wa.x + wa.y * wa.y) + wa.z * wa.z)
                + ((wb.x * wb.x + wb.y * wb.y) + wb.z * wb.z);
            if s.rd(slot + i64::from(c::NBORDER)) == 0 && wsum > 0.0 {
                let ca = match bv_a {
                    Primitive::Box(bx) => bx.center,
                    _ => Vec3::ZERO,
                };
                let cb = match bv_b {
                    Primitive::Box(bx) => bx.center,
                    _ => Vec3::ZERO,
                };
                let mut i_prim = s.i_caller;
                let mut i_feat = 0;
                let mut res_a = [stale_vec(w); 2];
                a.find_closest_point(gwd_a, &mut i_prim, &mut i_feat, cb, cb, &mut res_a, 10);
                let mut res_b = [res_a[1], stale_vec(w)];
                b.find_closest_point(gwd_b, &mut i_prim, &mut i_feat, ca, ca, &mut res_b, 10);
                let (pta, ptb) = (res_a[0], res_b[0]);
                pt = if b.point_inside_status(to_local(pta, gwd_b)) != 0 {
                    pta
                } else if a.point_inside_status(to_local(ptb, gwd_a)) != 0 {
                    ptb
                } else {
                    Vec3::new(
                        (pta.x + ptb.x) * 0.5,
                        (ptb.y + pta.y) * 0.5,
                        (ptb.z + pta.z) * 0.5,
                    )
                };
            }
            // relative velocity at the point
            let (va, vb) = (gwd_a.v, gwd_b.v);
            let (ca, cb) = (gwd_a.center_of_mass, gwd_b.center_of_mass);
            let db = Vec3::new(pt.x - cb.x, pt.y - cb.y, pt.z - cb.z);
            let da = Vec3::new(pt.x - ca.x, pt.y - ca.y, pt.z - ca.z);
            let wbx = db.z * wb.y - db.y * wb.z;
            let wby = db.x * wb.z - wb.x * db.z;
            let wbz = wb.x * db.y - db.x * wb.y;
            let wax = wa.y * da.z - wa.z * da.y;
            let way = da.x * wa.z - wa.x * da.z;
            let waz = wa.x * da.y - da.x * wa.y;
            let mut vx = ((wax + va.x) - vb.x) - wbx;
            let mut vy = ((way + va.y) - vb.y) - wby;
            let mut vz = ((waz + va.z) - vb.z) - wbz;
            let l2 = (vy * vy + vx * vx) + vz * vz;
            if (l2 == 0.0 || l2.is_nan()) && tbi == 3 {
                // a ray B (not reachable: rays take step 5): the ray's normal direction
                let e = match pb {
                    Primitive::Ray(r) => r.dir,
                    _ => Vec3::ZERO,
                };
                let cx = e.z * n.y - e.y * n.z;
                let cy = e.x * n.z - n.x * e.z;
                let cz = n.x * e.y - e.x * n.y;
                let ux = cz * e.y - cy * e.z;
                let uy = cx * e.z - e.x * cz;
                let uz = e.x * cy - cx * e.y;
                let q = (ux * ux + uy * uy) + uz * uz;
                if q > 1e-20 {
                    let inv = 1.0 / q.sqrt();
                    vx = ux * inv;
                    vy = inv * uy;
                    vz = inv * uz;
                } else {
                    vx = 0.0;
                    vy = 0.0;
                    vz = 1.0;
                }
            }
            let l2 = (vy * vy + vx * vx) + vz * vz;
            if l2 > ip.vrel_min * ip.vrel_min {
                let len = l2.sqrt();
                let inv = 1.0 / len;
                p.mode = 0;
                p.vel = len;
                p.tmax = len * ip.time_interval;
                p.dir = Vec3::new(-vx * inv, inv * -vy, inv * -vz);
                let r = unproject(env, s, 0, &mut p, &pa, &pb, &mut y, area);
                let diff = (y.t - f64::from(p.tmax)) as f32;
                r9 = r & sgn(diff);
            } else {
                r9 = 0;
            }
        }

        if (ip.i_unprojection_mode | r9) == 0 {
            // minimum-distance unprojection
            p.dir = Vec3::ZERO;
            p.vel = 0.0;
            p.tmax = ip.max_unproj;
            p.mode = 0;
            p.flag = i32::from(ip.no_intersection)
                | is_zero_idiom((tai - 6) | tbi)
                | is_zero_idiom((tbi - 6) | tai);
            r9 = unproject(env, s, 0, &mut p, &pa, &pb, &mut y, area);
        }
    }

    // 9. the contact record
    if r9 == 0 {
        return out0;
    }
    let so = |k: u32| slot + i64::from(k);
    let mut t = y.t;
    if ip.sweep_test {
        t = f64::from(p.tmax) - t;
    }
    s.wr_d(so(c::T), t);
    s.wr_v(so(c::PT), y.pt);
    let nn = y.n;
    let l2 = (nn.y * nn.y + nn.x * nn.x) + nn.z * nn.z;
    let n = if l2 > 1e-20 {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(inv * nn.x, nn.y * inv, nn.z * inv)
    } else {
        Vec3::new(0.0, 0.0, 1.0)
    };
    s.wr_v(so(c::N), n);
    s.wr_v(so(c::DIR), p.dir);
    s.wr(so(c::MODE), p.mode as u32);
    s.wr_f(so(c::VEL), p.vel);
    s.wr(so(c::ID), u32::MAX);
    s.wr(so(c::ID) + 4, u32::MAX);
    s.wr(so(c::IPRIM), 0);
    s.wr(so(c::IPRIM) + 4, 0);
    s.wr(so(c::IFEATURE), y.i_feature[0] as u32);
    s.wr(so(c::IFEATURE) + 4, y.i_feature[1] as u32);
    s.wr(so(c::INODE), u32::MAX);
    s.wr(so(c::INODE) + 4, u32::MAX);
    let area_npt = area.map_or(0, |ao| s.rd_i(ao + i64::from(ar::NPT)));
    match area {
        Some(ao) if area_npt != 0 => {
            s.wr_q(so(c::AREA), s.addr(ao));
            s.set_n_areas(s.n_areas().wrapping_add(1));
            let npt = s.rd_i(ao + i64::from(ar::NPT));
            s.set_n_area_pts(s.n_area_pts().wrapping_add(npt));
            let nbp = s.rd_i(so(c::NBORDER));
            let npt = s.rd_i(ao + i64::from(ar::NPT));
            if nbp < npt && (nbp == 0 || is_zero_idiom(tbi - 5) | is_zero_idiom(ta_final - 5) != 0)
            {
                s.wr_q(so(c::PTBORDER), s.rd_q(ao + i64::from(ar::PT)));
                s.wr(so(c::NBORDER), npt as u32);
                s.wr_v(so(c::CENTER), Vec3::ZERO);
                let mut k = 0;
                while k < s.rd_i(ao + i64::from(ar::NPT)) {
                    let pp = s.rd_q(ao + i64::from(ar::PT));
                    let q = s.rd_v_at(pp.wrapping_add(12 * k as u64));
                    let cc = s.rd_v(so(c::CENTER));
                    s.wr_v(so(c::CENTER), Vec3::new(q.x + cc.x, q.y + cc.y, q.z + cc.z));
                    k += 1;
                }
                let kk = 1.0 / s.rd_i(ao + i64::from(ar::NPT)) as f32;
                let cc = s.rd_v(so(c::CENTER));
                s.wr_v(so(c::CENTER), Vec3::new(kk * cc.x, kk * cc.y, kk * cc.z));
            }
        }
        _ => s.wr_q(so(c::AREA), 0),
    }
    if s.rd(so(c::NBORDER)) == 0 {
        let q = s.rd_v(so(c::PT));
        s.wr_v(so(c::CENTER), q);
        let pb_now = s.rd_q(so(c::PTBORDER));
        s.wr_v_at(pb_now, s.rd_v(so(c::CENTER)));
        s.wr(so(c::NBORDER), 1);
    }
    let wd = s.rd(so(c::CONSECUTIVE));
    s.wr(so(c::CONSECUTIVE), wd & !0xFF);
    s.set_n_contacts(s.n_contacts().wrapping_add(1));
    let nbp = s.rd_i(so(c::NBORDER));
    s.set_n_border(s.n_border().wrapping_add(nbp));
    IntersectOut { ret: r9, first }
}

/// `CRayGeom::RegisterIntersection` (slot 54, x64 0x3519A380) as the primitive `Intersect`
/// calls it for a ray `b` (with `gtest[1] = NULL` and its own count): contact `nc` gets
/// `t = ((Rᵀ·(pt₀ − offset_B)) − origin)·ndir` (no scale), `pt = center = pt₀`, `n = −n`,
/// `dir = 0`, mode 0, `vel = 0`, `id = (−1, inters.id[1])`, `iPrim = (0, 0)`, `iFeature =
/// (0x20, byte +37)`, `iNode` from the pair, border `[pt]` through `ptborder = &contact.pt`,
/// no area. `+124` is not written.
fn register_ray(
    s: &mut ThreadScratch,
    nc: i32,
    b: &dyn Geometry,
    gwd_b: &GeomWorldData,
    inters: &PrimInters,
) {
    let o = ThreadScratch::contact_off(nc);
    let so = |k: u32| o + i64::from(k);
    let (org, nd) = match b.primitive(0) {
        // `CRayGeom +72` as stored (RWI keeps the original direction after shortening `dir`)
        Some(Primitive::Ray(r)) => (
            r.origin,
            b.ray_stored_ndir().unwrap_or_else(|| ray_ndir(r.dir)),
        ),
        _ => (Vec3::ZERO, Vec3::Z),
    };
    s.wr(so(c::NBORDER), 1);
    s.wr_q(so(c::AREA), 0);
    s.wr_q(so(c::PTBORDER), s.addr(so(c::PT)));
    let q = inters.pt[0];
    let r = &gwd_b.r.m;
    let d = Vec3::new(
        q.x - gwd_b.offset.x,
        q.y - gwd_b.offset.y,
        q.z - gwd_b.offset.z,
    );
    let ly = ((d.y * r[1][1] + d.x * r[0][1]) + d.z * r[2][1]) - org.y;
    let lx = ((d.y * r[1][0] + d.x * r[0][0]) + d.z * r[2][0]) - org.x;
    let lz = ((d.y * r[1][2] + d.x * r[0][2]) + d.z * r[2][2]) - org.z;
    let t = (ly * nd.y + nd.x * lx) + lz * nd.z;
    s.wr_d(so(c::T), f64::from(t));
    s.wr_v(so(c::CENTER), q);
    s.wr_v(so(c::PT), q);
    s.wr_v(so(c::N), Vec3::new(-inters.n.x, -inters.n.y, -inters.n.z));
    s.wr_v(so(c::DIR), Vec3::ZERO);
    s.wr(so(c::MODE), 0);
    s.wr(so(c::VEL), 0);
    s.wr(so(c::ID), u32::MAX);
    s.wr(so(c::IPRIM), 0);
    s.wr(so(c::ID) + 4, i32::from(inters.id[1]) as u32);
    s.wr(so(c::IFEATURE), 0x20);
    s.wr(so(c::IPRIM) + 4, 0);
    s.wr(so(c::IFEATURE) + 4, u32::from(inters.i_feature[0][1]));
    s.wr(so(c::INODE), inters.i_node[0] as u32);
    s.wr(so(c::INODE) + 4, inters.i_node[1] as u32);
}

// ------------------------------------------------------------------------------------------
// Geometry::intersect glue

static ENV: OnceLock<PrimIntersectEnv> = OnceLock::new();
static GENERIC: OnceLock<GenericIntersectFn> = OnceLock::new();

thread_local! {
    static SCRATCH: std::cell::RefCell<ThreadScratch> = std::cell::RefCell::new(ThreadScratch::new(0));
}

/// Install the generic `CGeometry::Intersect` used by [`Geometry::intersect`] of the
/// primitives when `other` is not a primitive (P1c). Returns `false` if one was installed
/// already.
pub fn install_generic_intersect(f: GenericIntersectFn) -> bool {
    GENERIC.set(f).is_ok()
}

/// The generic `CGeometry::Intersect` installed with [`install_generic_intersect`], if any
/// (added for the world queries, which call [`intersect`] directly to keep the `gwd` write-back).
pub fn installed_generic() -> Option<GenericIntersectFn> {
    GENERIC.get().copied()
}

/// The shared tables with the installed generic fallback (what [`geometry_intersect`] uses).
pub fn env_with_generic() -> PrimIntersectEnv {
    let mut env = *shared_env();
    env.generic = installed_generic();
    env
}

/// The shared tables used by the [`Geometry::intersect`] glue.
pub fn shared_env() -> &'static PrimIntersectEnv {
    ENV.get_or_init(PrimIntersectEnv::new)
}

/// Run `f` with this thread's scratch (the one [`Geometry::intersect`] uses).
pub fn with_thread_scratch<R>(f: impl FnOnce(&mut ThreadScratch) -> R) -> R {
    SCRATCH.with(|s| f(&mut s.borrow_mut()))
}

/// `Geometry::intersect` of the four primitive classes: [`intersect`] with the thread's
/// scratch and a copy of `gwd1` (use [`intersect`] directly to see the write-back). `contacts`
/// is replaced by copies of the new contact records.
pub fn geometry_intersect(
    a: &dyn Geometry,
    b: &dyn Geometry,
    gwd1: &GeomWorldData,
    gwd2: &GeomWorldData,
    params: &mut IntersectionParams,
    contacts: &mut Vec<GeomContact>,
) -> usize {
    let mut env = *shared_env();
    env.generic = GENERIC.get().copied();
    let mut g1 = *gwd1;
    with_thread_scratch(|s| {
        let out = intersect(&env, s, a, b, &mut g1, gwd2, params);
        contacts.clear();
        if let (Some(first), true) = (out.first, out.ret > 0) {
            contacts.extend(s.contacts(first, out.ret));
        }
        out.ret.max(0) as usize
    })
}
