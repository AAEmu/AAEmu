//! `PrimitiveWorldIntersection` (world slot 64 wrapper 0x350964F0, core slot 65 0x35093100)
//! and the sweep-axis entity sort 0x3507C1E0.
//!
//! # Flow of 0x35093100
//!
//! 1. Caller index = the calling thread's physics index (TLS; 4 = external). The per-thread
//!    "best contact" record (`0x35218430 + 136·tid`) gets `t = 0`, `pt = 0`.
//! 2. `entTypes & 0x800` = queue: only when neither a contact output nor intersection params
//!    are given (then the request is queued and 0 returned); otherwise 0 is returned at once.
//! 3. Intersection params: the caller's are copied; without them, a zero sweep sets the bytes
//!    `+98` (stop at the first hit), `+99` (no area contacts) and `+100`; a non-zero sweep
//!    sets `bSweepTest`, `time_interval = 1`, `gwd1.v = sweepDir` and the best `t = 1e10`.
//! 4. The primitive becomes a geometry centred at the origin (box: `+40`, others: `+0` centre
//!    zeroed, put back afterwards) placed by `gwd1.offset = centre`; types other than box,
//!    sphere, cylinder, capsule return 0.
//! 5. The query box: the geometry's slot-6 box placed by `gwd1`, extended by the sweep
//!    (`((d ∓ |d|)·0.5)` per axis).
//! 6. Entities: `nSkip < 0` → the skip list *is* the entity list; otherwise the skip entities
//!    get the caller bit and `GetEntitiesInBox(box, entTypes, caller)` is used. With a sweep
//!    they are sorted along the dominant sweep axis (nearest first) and the best point is
//!    seeded with the farthest entity's near corner.
//! 7. Per entity (pruned against the best point along the sweep axis), per part with
//!    `flags ⊇ geomFlagsAll`, `flags ∩ geomFlagsAny ≠ ∅` and a zero or overlapping bbox: the
//!    primitive `Intersect` (`gwd1` written back, see [`crate::collision::prim_intersect`]) against
//!    the part geometry placed by `gwd2`; each new contact gets `iPrim[0] = entity id`,
//!    `iPrim[1] = part id`, `id[1] = mapped surface`. Then:
//!    * `+98` set → `*pcontacts = pool`, return 1.0 (the first part that intersects);
//!    * sweep → keep the contact with the smallest `t` (only the first contact of a call);
//!    * otherwise (caller params with `+98 = 0`) → `bSweepTest = 0`, `bKeepPrevContacts = 1`,
//!      `t += count` (all contacts accumulate in the pool).
//! 8. Returns the best `t` (0 when `>= 1e10`); `*pcontacts` = the best record (sweep) or the
//!    contact pool. The skip entities lose the caller bit.
//!
//! Not ported: the per-thread part subsets (`entity+320/+328`, living/structure code) and the
//! `nSkip < 0` nibble update for entities with flag 0x10; the debug callback.

use super::{aabb_overlap6, map_surface, prim_supported, QueryIntersectionParams};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::collision::prim_intersect::scratch::{c, ThreadScratch};
use crate::collision::prim_intersect::{env_with_generic, intersect, with_thread_scratch};
use crate::geometry::primitives::Primitive;
use crate::geometry::{GeomWorldData, Geometry};
use crate::math::{Mat33, Vec3};
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `SPWIParams` (96 bytes).
#[derive(Clone, Debug)]
pub struct PwiParams {
    /// `+0` / `+8` foreign data (passed to queued results).
    pub foreign_data: u64,
    pub i_foreign_data: i32,
    /// `+24` type and `+32` the primitive (box, sphere, cylinder or capsule).
    pub prim: Primitive,
    /// `+40` sweep direction (its length is the sweep length; zero = static test).
    pub sweep_dir: Vec3,
    /// `+52` entity types (GetEntitiesInBox objtypes; bit 0x800 = queue).
    pub ent_types: u32,
    /// `+64` / `+68`.
    pub geom_flags_all: u32,
    pub geom_flags_any: u32,
    /// `+72` intersection params (`None` = the defaults described in the module docs).
    pub ip: Option<QueryIntersectionParams>,
    /// `+88` / `+80` skip entities; with [`PwiParams::entities_only`] the entities to test.
    pub skip: Vec<EntIdx>,
    /// `nSkip < 0`: `skip` is the list of entities to test (no grid query).
    pub entities_only: bool,
    /// `+56` non-null: the caller wants the contacts (also blocks queueing).
    pub want_contacts: bool,
}

impl Default for PwiParams {
    fn default() -> Self {
        Self {
            foreign_data: 0,
            i_foreign_data: 0,
            prim: Primitive::Sphere(crate::geometry::primitives::SpherePrim {
                center: Vec3::ZERO,
                r: 0.5,
            }),
            sweep_dir: Vec3::ZERO,
            ent_types: super::ent_types::ALL,
            geom_flags_all: 0,
            geom_flags_any: 0xFFFF_FFFF,
            ip: None,
            skip: Vec::new(),
            entities_only: false,
            want_contacts: true,
        }
    }
}

/// What PWI returns: the float result and the contacts `*pcontacts` designates.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PwiResult {
    /// Sweep: distance travelled before the first contact (0 = none / at the start);
    /// static: 1.0 on the first intersection (default params) or the contact count
    /// (caller params with `+98 = 0`); 0 = nothing.
    pub dist: f32,
    /// Sweep: the best contact; otherwise the contact pool of the last call.
    pub contacts: Vec<GeomContact>,
}

/// The primitive geometry of a query, centred at the origin, and the centre (`gwd1.offset`).
pub(crate) fn query_geometry(prim: &Primitive) -> Option<(Arc<dyn Geometry>, Vec3)> {
    let (centred, center) = match prim {
        Primitive::Box(b) => {
            let mut b2 = *b;
            let c = b2.center;
            b2.center = Vec3::ZERO;
            (Primitive::Box(b2), c)
        }
        Primitive::Sphere(s) => {
            let mut s2 = *s;
            let c = s2.center;
            s2.center = Vec3::ZERO;
            (Primitive::Sphere(s2), c)
        }
        Primitive::Cylinder(cy) => {
            let mut c2 = *cy;
            let c = c2.center;
            c2.center = Vec3::ZERO;
            (Primitive::Cylinder(c2), c)
        }
        Primitive::Capsule(cy) => {
            let mut c2 = *cy;
            let c = c2.center;
            c2.center = Vec3::ZERO;
            (Primitive::Capsule(c2), c)
        }
        _ => return None,
    };
    crate::world::geoman::create_primitive(&centred).map(|g| (g, center))
}

/// The query box of PWI / CollideEntityWithPrimitive: the geometry's box placed at `off`,
/// extended by `d` (`((d − |−d|)·0.5) + ((c + off) − ext)` / `((d + |−d|)·0.5) + ((c + off) + ext)`).
pub(crate) fn query_box(geom: &dyn Geometry, off: Vec3, d: Vec3) -> [Vec3; 2] {
    let bb = geom.bbox();
    let b = bb.basis.to_array();
    let s = bb.size;
    let e = |i: usize| ((s.x * b[i].abs()) + (s.y * b[3 + i].abs())) + (s.z * b[6 + i].abs());
    let ext = [e(0), e(1), e(2)];
    let c = [bb.center.x + off.x, bb.center.y + off.y, bb.center.z + off.z];
    let dv = [d.x, d.y, d.z];
    let mut lo = [0f32; 3];
    let mut hi = [0f32; 3];
    for k in 0..3 {
        let a = (-dv[k]).abs();
        lo[k] = ((dv[k] - a) * 0.5) + (c[k] - ext[k]);
        hi[k] = ((dv[k] + a) * 0.5) + (c[k] + ext[k]);
    }
    [Vec3::new(lo[0], lo[1], lo[2]), Vec3::new(hi[0], hi[1], hi[2])]
}

/// The part placement `gwd2`: `pos + q·part.pos`, `R(q ⊗ part.q)`, `part.scale`.
pub(crate) fn part_gwd(w: &World, e: EntIdx, ipart: usize) -> GeomWorldData {
    let ent = w.ent(e);
    let p = &ent.parts[ipart];
    let r = ent.q.rotate(p.pos);
    GeomWorldData {
        offset: Vec3::new(r.x + ent.pos.x, r.y + ent.pos.y, r.z + ent.pos.z),
        r: Mat33::from_quat(ent.q.product(p.q)),
        scale: p.scale,
        ..GeomWorldData::default()
    }
}

/// A copy of a contact record (the best sweep contact), decoded later through a scratch clone
/// so that its border pointers see the pool as it is at the end of the query.
#[derive(Clone)]
struct RawContact {
    words: [u32; 34],
}

impl RawContact {
    fn read(s: &ThreadScratch, i: i32) -> Self {
        let o = ThreadScratch::contact_off(i);
        let mut words = [0u32; 34];
        for (k, w) in words.iter_mut().enumerate() {
            *w = s.rd(o + 4 * k as i64);
        }
        Self { words }
    }

    fn decode(&self, s: &ThreadScratch) -> GeomContact {
        let mut tmp = s.clone();
        let o = ThreadScratch::contact_off(0);
        for (k, w) in self.words.iter().enumerate() {
            tmp.wr(o + 4 * k as i64, *w);
        }
        tmp.contact(0)
    }

    fn t(&self) -> f64 {
        f64::from_bits(u64::from(self.words[0]) | (u64::from(self.words[1]) << 32))
    }

    fn pt(&self) -> Vec3 {
        Vec3::new(
            f32::from_bits(self.words[2]),
            f32::from_bits(self.words[3]),
            f32::from_bits(self.words[4]),
        )
    }
}

/// `sub_3507C1E0`: quicksort of the entity list by `kmax·bbmax + kmin·bbmin` (ascending; the
/// middle element is the pivot, swapped to the front).
fn sort_by_axis(w: &World, list: &mut [EntIdx], kmin: [f32; 3], kmax: [f32; 3], lo: i32, hi: i32) {
    let key = |e: EntIdx| {
        let b = w.ent(e).bbox;
        let dmax = ((kmax[1] * b[1].y) + (kmax[0] * b[1].x)) + (kmax[2] * b[1].z);
        let dmin = ((kmin[1] * b[0].y) + (kmin[0] * b[0].x)) + (kmin[2] * b[0].z);
        (dmax, dmin)
    };
    let mut lo = lo;
    while lo < hi {
        let mid = ((lo + hi) >> 1) as usize;
        list.swap(lo as usize, mid);
        let (pmax, pmin) = key(list[lo as usize]);
        let pk = pmin + pmax;
        let mut store = lo;
        for j in (lo + 1)..=hi {
            let (a, b) = key(list[j as usize]);
            if (a + b) < pk {
                store += 1;
                list.swap(store as usize, j as usize);
            }
        }
        list.swap(lo as usize, store as usize);
        sort_by_axis(w, list, kmin, kmax, lo, store - 1);
        lo = store + 1;
    }
}

impl World {
    /// `PrimitiveWorldIntersection(SPWIParams&, tag)` (slot 65, 0x35093100; slot 64
    /// 0x350964F0 builds the params from an argument list). The caller index is the calling
    /// thread's ([`World::tls_thread`]). See the module docs for the flow; with `entTypes &
    /// 0x800` use [`World::pwi_queued`].
    #[allow(clippy::too_many_lines)]
    pub fn pwi(&mut self, p: &PwiParams) -> PwiResult {
        let caller = self.tls_thread.clamp(0, 4);
        if p.ent_types & 0x800 != 0 {
            if !p.want_contacts && p.ip.is_none() && prim_supported(&p.prim) {
                self.pwi_queued(p.clone(), None);
            }
            return PwiResult::default();
        }
        let Some((geom, center)) = query_geometry(&p.prim) else {
            return PwiResult::default();
        };
        let mut best: Option<RawContact> = None;
        let mut best_t = 0.0f64;
        let mut best_pt = Vec3::ZERO;
        let mut gwd1 = GeomWorldData::default();
        let mut ip: IntersectionParams;
        let stop: bool;
        let d = p.sweep_dir;
        match &p.ip {
            Some(qip) => {
                ip = qip.ip;
                stop = qip.stop_at_first;
            }
            None => {
                ip = IntersectionParams::default();
                if ((d.y * d.y) + (d.x * d.x)) + (d.z * d.z) <= 0.0 {
                    stop = true;
                    ip.no_area_contacts = true;
                } else {
                    stop = false;
                    ip.sweep_test = true;
                    best_t = f64::from_bits(0x4202_A05F_2000_0000);
                    ip.time_interval = 1.0;
                    gwd1.v = d;
                }
            }
        }
        gwd1.offset = center;
        let qbox = query_box(geom.as_ref(), gwd1.offset, d);
        let bit = 1u32 << caller;
        let mut list: Vec<EntIdx>;
        if p.entities_only {
            list = p.skip.clone();
        } else {
            for &s in &p.skip {
                if !s.is_none() && self.ent_alive(s) && (self.ent(s).processed >> caller) & 1 == 0 {
                    let e = self.ent_mut(s);
                    e.processed = e.processed.wrapping_add(bit);
                }
            }
            list = self.get_entities_in_box(qbox[0], qbox[1], p.ent_types as i32, None, caller as usize);
        }
        let n = list.len() as i32;
        let mut kmin = [0f32; 3];
        let mut kmax = [0f32; 3];
        if ip.sweep_test && n > 0 {
            let ad = [d.x.abs(), d.y.abs(), d.z.abs()];
            let a = ((ad[0] - ad[1]).to_bits() >> 31) as usize;
            let b = ((ad[a] - ad[2]).to_bits() >> 31) as i32;
            let m = (2 * b) | a as i32;
            let axis = (m & (((m ^ 2) | 4) >> 1)) as usize;
            let dv = [d.x, d.y, d.z];
            let sign = (dv[axis].to_bits() >> 31) as usize;
            let val = (1 - 2 * sign as i32) as f32;
            if sign == 0 {
                kmin[axis] = val;
            } else {
                kmax[axis] = val;
            }
            sort_by_axis(self, &mut list, kmin, kmax, 0, n - 1);
            let last = self.ent(list[(n - 1) as usize]).bbox;
            best_pt = last[sign];
        }
        let env = env_with_generic();
        let mut out_contacts: Option<Vec<GeomContact>> = None;
        let mut stopped = false;
        'ents: for &e in &list {
            if !self.ent_alive(e) {
                continue;
            }
            let bb = self.ent(e).bbox;
            let v142 = (((bb[1].y - bb[0].y) * (bb[1].y - bb[0].y)) + ((bb[1].x - bb[0].x) * (bb[1].x - bb[0].x)))
                + ((bb[1].z - bb[0].z) * (bb[1].z - bb[0].z));
            let lhs = ((((kmin[1] * bb[0].y) + (kmin[0] * bb[0].x)) + (kmin[2] * bb[0].z))
                + (((kmax[1] * bb[1].y) + (kmax[0] * bb[1].x)) + (kmax[2] * bb[1].z)))
                * v142;
            let rhs = ((((kmax[0] + kmin[0]) * best_pt.x) + ((kmax[1] + kmin[1]) * best_pt.y))
                + ((kmax[2] + kmin[2]) * best_pt.z))
                * v142;
            if !(lhs <= rhs) {
                continue;
            }
            let nparts = self.ent(e).parts.len();
            let ent_id = self.ent(e).id();
            for ip_i in 0..nparts {
                let (pf, pbb, pid) = {
                    let part = &self.ent(e).parts[ip_i];
                    (part.flags, part.bbox, part.id)
                };
                if (pf & p.geom_flags_all) != p.geom_flags_all || (pf & p.geom_flags_any) == 0 {
                    continue;
                }
                let diag = (((pbb[1].x - pbb[0].x) * (pbb[1].x - pbb[0].x))
                    + ((pbb[1].y - pbb[0].y) * (pbb[1].y - pbb[0].y)))
                    + ((pbb[1].z - pbb[0].z) * (pbb[1].z - pbb[0].z));
                if !(diag == 0.0 || aabb_overlap6(&pbb, &qbox)) {
                    continue;
                }
                let gwd2 = part_gwd(self, e, ip_i);
                let parts_snapshot = &self.ent(e).parts;
                let pgeom = parts_snapshot[ip_i].pgeom.geom.clone();
                let r = with_thread_scratch(|s| {
                    let out = intersect(&env, s, geom.as_ref(), pgeom.as_ref(), &mut gwd1, &gwd2, &mut ip);
                    if out.ret == 0 {
                        return None;
                    }
                    let first = out.first.unwrap_or(0);
                    for j in 0..out.ret.max(0) {
                        let o = ThreadScratch::contact_off(first + j);
                        s.wr(o + i64::from(c::IPRIM), ent_id as u32);
                        s.wr(o + i64::from(c::IPRIM) + 4, pid as u32);
                        let id1 = s.rd_i(o + i64::from(c::ID) + 4);
                        let m = map_surface(parts_snapshot, id1, ip_i as i32);
                        s.wr(o + i64::from(c::ID) + 4, m as u32);
                    }
                    if stop {
                        let n = s.n_contacts();
                        return Some((out, None, Some(s.contacts(0, n))));
                    }
                    if ip.sweep_test {
                        let rc = RawContact::read(s, first);
                        return Some((out, Some(rc), None));
                    }
                    Some((out, None, None))
                });
                let Some((out, rc, pool)) = r else {
                    continue;
                };
                if stop {
                    out_contacts = pool;
                    best_t = 1.0;
                    stopped = true;
                    break 'ents;
                }
                if ip.sweep_test {
                    if let Some(rc) = rc {
                        if best_t > rc.t() {
                            best_t = rc.t();
                            best_pt = rc.pt();
                            best = Some(rc);
                        }
                    }
                } else {
                    ip.sweep_test = false;
                    ip.keep_prev_contacts = true;
                    best_t += f64::from(out.ret);
                }
            }
        }
        let contacts = if stopped {
            out_contacts.unwrap_or_default()
        } else if ip.sweep_test {
            with_thread_scratch(|s| best.as_ref().map(|b| vec![b.decode(s)]).unwrap_or_default())
        } else {
            with_thread_scratch(|s| {
                let n = s.n_contacts();
                s.contacts(0, n)
            })
        };
        for &s in &p.skip {
            if !p.entities_only && !s.is_none() && self.ent_alive(s) && (self.ent(s).processed >> caller) & 1 != 0 {
                let e = self.ent_mut(s);
                e.processed = e.processed.wrapping_add((-1i32 << caller) as u32);
            }
        }
        let dist = if best_t >= 1.0e10 { 0.0 } else { best_t as f32 };
        PwiResult {
            dist,
            contacts: if p.want_contacts { contacts } else { Vec::new() },
        }
    }
}
