//! Queued queries: RWI with flag 0x800 (ring `world+21696`, 120-byte records) and PWI with
//! `entTypes & 0x800` (ring `world+21752`, 176-byte records), processed by world slot 43
//! (0x351A9FC0, "RayWorldIntersection(Queued)" / "PrimitiveWorldIntersection(Queued)") into
//! events 9 and 10.
//!
//! Port: the rings are `VecDeque`s (FIFO, as the binary's head/tail indices); skip entities
//! are stored as entity ids (at most 5 for RWI, 4 for PWI; null → id −3) and looked up again
//! when processed; RWI requests without a hit buffer get `nMaxHits` fresh hits (the binary's
//! 80-byte hit pool, `bHitsFromPool`). The events go to the type-9/10 immediate clients in
//! the binary and are then logged; `PumpLoggedEvents` calls the request's own callback.
//! Here [`World::process_queued_queries`] stores the results in
//! [`super::QueryState::logged_results`] and [`World::deliver_query_results`] calls the
//! callbacks (the lead's `PumpLoggedEvents` should call it at the type-9/10 point).
//!
//! Deviation: the binary keeps a pointer to the caller's hit-last cache; the port copies the
//! cache at queue time and returns the updated cache in the result.

use super::pwi::PwiParams;
use super::rwi::{HitLast, RayHit, RwiParams};
use crate::geometry::primitives::Primitive;
use crate::math::Vec3;
use crate::world::{EntIdx, World};

/// Callback of a queued RWI (`SRWIParams +16` OnEvent); its return value is ignored by the
/// port (the pump's stop logic for types 9/10 is the lead's).
pub type RwiCallback = Box<dyn FnMut(&QueuedRwiResult) -> i32 + Send>;
/// Callback of a queued PWI.
pub type PwiCallback = Box<dyn FnMut(&QueuedPwiResult) -> i32 + Send>;

/// A queued RWI request (the 120-byte record).
pub struct QueuedRwi {
    pub params: RwiParams,
    /// `+100` skip entity ids (max 5).
    pub skip_ids: Vec<i32>,
    pub n_max_hits: i32,
    /// The caller's hit buffer contents (or a pool buffer of `n_max_hits`).
    pub hits: Vec<RayHit>,
    /// `+96` bit 16: hits from the pool.
    pub hits_from_pool: bool,
    pub hit_last: Option<HitLast>,
    /// `+96` iCaller.
    pub caller: i32,
    pub callback: Option<RwiCallback>,
}

impl std::fmt::Debug for QueuedRwi {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("QueuedRwi")
            .field("params", &self.params)
            .field("skip_ids", &self.skip_ids)
            .field("n_max_hits", &self.n_max_hits)
            .finish_non_exhaustive()
    }
}

/// A queued PWI request (the 176-byte record).
pub struct QueuedPwi {
    pub params: PwiParams,
    /// `+160` skip entity ids (max 4).
    pub skip_ids: Vec<i32>,
    pub callback: Option<PwiCallback>,
}

impl std::fmt::Debug for QueuedPwi {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("QueuedPwi")
            .field("params", &self.params)
            .field("skip_ids", &self.skip_ids)
            .finish_non_exhaustive()
    }
}

/// Event 9 (72 bytes): the result of a queued RWI.
#[derive(Clone, Debug)]
pub struct QueuedRwiResult {
    /// `+24` / `+32` the request's foreign data.
    pub foreign_data: u64,
    pub i_foreign_data: i32,
    /// `+48` the hits, `+56` nHits (the RWI return value), `+60` nMaxHits.
    pub hits: Vec<RayHit>,
    pub n_hits: i32,
    pub n_max_hits: i32,
    /// `+64` bHitsFromPool.
    pub hits_from_pool: bool,
    /// The hit-last cache after the query (port addition).
    pub hit_last: Option<HitLast>,
}

/// Event 10 (88 bytes): the result of a queued PWI.
#[derive(Clone, Debug)]
pub struct QueuedPwiResult {
    /// `+16` the entity of the first contact (`iPrim[0]` id), `NONE` = the static dummy.
    pub ent: EntIdx,
    pub foreign_data: u64,
    pub i_foreign_data: i32,
    /// `+48` the PWI result, `+52` pt, `+64` n, `+76` surface (`id[1]`), `+80` part id.
    pub dist: f32,
    pub pt: Vec3,
    pub n: Vec3,
    pub idmat: i32,
    pub partid: i32,
}

/// A processed queued query waiting for delivery.
pub enum QueuedResult {
    Rwi(QueuedRwiResult, Option<RwiCallback>),
    Pwi(QueuedPwiResult, Option<PwiCallback>),
}

impl std::fmt::Debug for QueuedResult {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            QueuedResult::Rwi(r, _) => f.debug_tuple("Rwi").field(r).finish(),
            QueuedResult::Pwi(r, _) => f.debug_tuple("Pwi").field(r).finish(),
        }
    }
}

impl World {
    /// `slot 26`-style id of a skip entity (null → −3).
    fn skip_id(&self, e: EntIdx) -> i32 {
        if e.is_none() || !self.ent_alive(e) {
            -3
        } else {
            self.ent(e).id()
        }
    }

    /// RWI with flag 0x800 (the head of 0x351AA730): queue the request; returns 1 (0 for a
    /// zero direction or before the temp pools exist). `hits = None` takes `n_max_hits`
    /// hits from the pool.
    pub fn rwi_queued(
        &mut self,
        p: RwiParams,
        hits: Option<Vec<RayHit>>,
        n_max_hits: i32,
        hit_last: Option<HitLast>,
        caller: i32,
        callback: Option<RwiCallback>,
    ) -> i32 {
        let d = p.dir;
        if ((d.x * d.x + d.y * d.y) + d.z * d.z) <= 0.0 || !self.temp_pools_ready() {
            return 0;
        }
        let skip_ids: Vec<i32> = p.skip.iter().take(5).map(|&e| self.skip_id(e)).collect();
        let (hits, pool) = match hits {
            Some(h) => (h, false),
            None => (vec![RayHit::default(); n_max_hits.max(1) as usize], true),
        };
        let mut params = p;
        params.flags &= !super::rwi_flags::QUEUE;
        self.queries.rwi_queue.push_back(QueuedRwi {
            params,
            skip_ids,
            n_max_hits,
            hits,
            hits_from_pool: pool,
            hit_last,
            caller,
            callback,
        });
        1
    }

    /// PWI with `entTypes & 0x800` and neither contact output nor params: queue it (the
    /// primitive is copied by type).
    pub fn pwi_queued(&mut self, p: PwiParams, callback: Option<PwiCallback>) -> f32 {
        let skip_ids: Vec<i32> = p.skip.iter().take(4).map(|&e| self.skip_id(e)).collect();
        let mut params = p;
        params.ent_types &= !0x800;
        params.ip = None;
        params.want_contacts = true;
        self.queries.pwi_queue.push_back(QueuedPwi {
            params,
            skip_ids,
            callback,
        });
        0.0
    }

    /// World slot 43 (0x351A9FC0): process (`mode != 0`) or drop (`mode == 0`, results with
    /// no hits) every queued RWI, then every queued PWI, in FIFO order; `mode == 2` does
    /// nothing. Returns the number of processed requests.
    pub fn process_queued_queries(&mut self, mode: i32) -> i32 {
        if mode == 2 {
            return 0;
        }
        let caller = self.tls_thread;
        let mut count = 0;
        while let Some(mut q) = self.queries.rwi_queue.pop_front() {
            count += 1;
            q.params.skip = q
                .skip_ids
                .iter()
                .map(|&id| self.entity_by_id(id).unwrap_or(EntIdx::NONE))
                .collect();
            let mut hl = q.hit_last;
            let n = if mode != 0 && !q.hits.is_empty() {
                self.rwi(&q.params, &mut q.hits, hl.as_mut(), caller)
            } else {
                0
            };
            let r = QueuedRwiResult {
                foreign_data: q.params.foreign_data,
                i_foreign_data: q.params.i_foreign_data,
                hits: q.hits,
                n_hits: n,
                n_max_hits: q.n_max_hits,
                hits_from_pool: q.hits_from_pool,
                hit_last: hl,
            };
            self.queries
                .logged_results
                .push(QueuedResult::Rwi(r, q.callback));
        }
        // the event-10 record is a local of the loop: the entity / point / normal / ids of a
        // request without a contact are those of the previous one (kept; the first request's
        // are the static dummy entity and stack garbage — zeros here)
        let mut r = QueuedPwiResult {
            ent: EntIdx::NONE,
            foreign_data: 0,
            i_foreign_data: 0,
            dist: 0.0,
            pt: Vec3::ZERO,
            n: Vec3::ZERO,
            idmat: 0,
            partid: 0,
        };
        while let Some(mut q) = self.queries.pwi_queue.pop_front() {
            count += 1;
            q.params.skip = q
                .skip_ids
                .iter()
                .map(|&id| self.entity_by_id(id).unwrap_or(EntIdx::NONE))
                .collect();
            r.foreign_data = q.params.foreign_data;
            r.i_foreign_data = q.params.i_foreign_data;
            r.dist = 0.0;
            if mode != 0
                && matches!(
                    q.params.prim,
                    Primitive::Box(_)
                        | Primitive::Sphere(_)
                        | Primitive::Cylinder(_)
                        | Primitive::Capsule(_)
                )
            {
                let res = self.pwi(&q.params);
                r.dist = res.dist;
                if res.dist != 0.0 {
                    if let Some(c) = res.contacts.first() {
                        r.pt = c.pt;
                        r.n = c.n;
                        r.idmat = c.id[1];
                        r.partid = c.i_prim[1];
                        r.ent = self.entity_by_id(c.i_prim[0]).unwrap_or(EntIdx::NONE);
                    }
                }
            }
            self.queries
                .logged_results
                .push(QueuedResult::Pwi(r.clone(), q.callback));
        }
        count
    }

    /// Deliver the logged results of queued queries to their callbacks (what
    /// `PumpLoggedEvents` does for event types 9 and 10). Returns how many were delivered.
    pub fn deliver_query_results(&mut self) -> usize {
        let v = std::mem::take(&mut self.queries.logged_results);
        let n = v.len();
        for r in v {
            match r {
                QueuedResult::Rwi(res, Some(mut cb)) => {
                    cb(&res);
                }
                QueuedResult::Pwi(res, Some(mut cb)) => {
                    cb(&res);
                }
                _ => {}
            }
        }
        n
    }
}
