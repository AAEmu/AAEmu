//! `Core/Managers/AIManager`: every NPC AI (in `AddAi` order), the 100 ms tick
//! (`TickManager.OnTick.Subscribe(Tick, 100 ms, useAsync: true)`), and the way other code
//! reaches an AI.
//!
//! Each AI lives behind its own mutex. Code outside the AI (skills, npcs, hooks) never blocks on
//! it: [`post`] queues the call and runs the queue when the AI is free; when the AI is busy
//! (another thread ticking it, or the call comes from inside the AI's own tick through a skill /
//! aggro hook) the current holder runs the queued calls before releasing it. This replaces the
//! C# unsynchronised re-entrant calls (`Ai.OnAggroTargetChanged()` from a damage effect while the
//! AI ticks) with "run right after the current AI step".
//!
//! The tick does **not** visit every registered AI. Its candidates are the AIs whose NPCs sit in
//! a region with player activity (gathered from the world regions) plus the explicitly maintained
//! sets: `woken` (persistent paths / follow targets / command queues / finite ongoing work /
//! calls still pending), `initializing` (newly added AIs) and `always`
//! (`NpcTemplate.ActivateAiAlways`). Every handle
//! carries its monotonic `AddAi` ordinal; only the wanted handles are collected (the sets are
//! keyed by ordinal, so an object-id reuse cannot mix a replaced handle into the selection),
//! deduplicated, sorted by ordinal and visited. A dormant AI far from every player is neither
//! locked nor visited (the full registry is only read by the low-frequency maintenance sweep,
//! `MAINTENANCE_SWEEP_TICKS`, which drops stale owners / NPCs deleted without the `npc_deleted`
//! hook). Pending calls always keep their AI woken. The plan is executed once per tick: the
//! default `World.AiWorkerThreads = 1` keeps the strict `AddAi` order, a configured value
//! (clamped to [`MAX_AI_WORKERS`]) splits it into a bounded number of contiguous chunk jobs on
//! the Tokio blocking pool.

use std::collections::{BTreeMap, HashSet, VecDeque};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, TryLockError};
use std::time::{Duration, Instant};

use dashmap::DashMap;

use crate::server::GameServer;
use crate::world::{NpcRef, WorldObject};

use super::framework::{AiCtx, AiKind, NpcAi};

/// Ticks a newly added AI stays a candidate so its initialization (queued `GoToSpawn` /
/// home position / path, `NpcSpawnerNpc.Spawn`) is never starved by the region selection.
const INITIALIZING_TICKS: u64 = 20;

/// Ticks between the dormant-handle maintenance sweeps (stale owners / NPCs deleted without the
/// `npc_deleted` hook). The sweep is the C# "visit every AI" behaviour at 1/50 the cost.
const MAINTENANCE_SWEEP_TICKS: u64 = 50;

/// Upper bound of concurrent chunk jobs in one tick batch (`World.AiWorkerThreads` is clamped
/// to `1..=MAX_AI_WORKERS`): a bounded number of jobs per batch instead of one task per NPC.
pub const MAX_AI_WORKERS: usize = 4;

/// A call queued for an AI.
pub type AiFn = Box<dyn FnOnce(&mut AiCtx<'_>) + Send>;

/// One NPC's AI.
pub struct AiHandle {
    pub obj_id: u32,
    /// Monotonic `AddAi` ordinal: total order of the registry, unique across object-id reuse.
    ordinal: u64,
    state: Mutex<NpcAi>,
    pending: Mutex<VecDeque<AiFn>>,
    removed: AtomicBool,
    /// `NpcAi::Tick` calls on this handle (tests / diagnostics).
    visits: AtomicU64,
}

impl std::fmt::Debug for AiHandle {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AiHandle")
            .field("obj_id", &self.obj_id)
            .field("ordinal", &self.ordinal)
            .finish()
    }
}

fn lock<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

fn try_lock<T>(m: &Mutex<T>) -> Option<MutexGuard<'_, T>> {
    match m.try_lock() {
        Ok(guard) => Some(guard),
        Err(TryLockError::Poisoned(e)) => Some(e.into_inner()),
        Err(TryLockError::WouldBlock) => None,
    }
}

impl AiHandle {
    fn detach_locked(&self, st: &mut NpcAi) {
        st.owner = None;
        st.should_tick = false;
        lock(&self.pending).clear();
    }

    /// Runs the queued calls while holding the state (the holder's duty).
    fn drain_locked(&self, server: &GameServer, st: &mut NpcAi) {
        loop {
            if self.removed.load(Ordering::Acquire) {
                self.detach_locked(st);
                return;
            }
            let next = lock(&self.pending).pop_front();
            let Some(f) = next else { return };
            let Some(npc) = st.owner() else {
                continue; // Owner == null
            };
            let obj = WorldObject::from(&npc);
            let mut ctx = AiCtx {
                server,
                npc,
                obj,
                ai: st,
            };
            if catch_unwind(AssertUnwindSafe(|| f(&mut ctx))).is_err() {
                tracing::error!("AI call on npc {} panicked", self.obj_id);
            }
        }
    }

    /// Tries to become the holder and run the queue (repeats while calls keep arriving after a
    /// release, so nothing is left behind).
    fn try_drain(&self, server: &GameServer) {
        loop {
            {
                let Some(mut st) = try_lock(&self.state) else {
                    return; // the holder runs the queue
                };
                self.drain_locked(server, &mut st);
            }
            if lock(&self.pending).is_empty() {
                return;
            }
        }
    }
}

/// Counts and stage wall times of one [`tick_all`] / [`tick_batch`] pass (tests / diagnostics).
/// Counts are exact; the stage times are millisecond wall times (truncated) and are not
/// reproducible between passes.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct AiTickDiagnostics {
    /// AIs selected for the tick after deduplication (active regions + woken sets).
    pub candidates: usize,
    /// AIs whose `NpcAi::Tick` ran (the rest were locked by another thread).
    pub ticked: usize,
    /// Candidates left alone because another thread held their AI lock (try-lock).
    pub busy: usize,
    /// Stale AIs removed during the tick (owner gone / NPC object deleted).
    pub removed: usize,
    /// Wall time of the candidate plan construction (`tick_candidates`: selecting,
    /// deduplicating and ordering the handles), ms. In a batch it is measured inside the plan
    /// job, so blocking-pool queue time is not included.
    pub plan_ms: u64,
    /// Wall time of behavior processing (`process_plan`: `NpcAi::Tick` over the plan plus the
    /// try-lock skips and drains), ms. Sequential passes measure `process_plan` directly; a
    /// multi-worker batch measures one span over its whole chunk phase (dispatch through all
    /// chunk awaits), so that span also includes chunk dispatch, blocking-pool queueing and
    /// await overhead.
    pub tick_ms: u64,
    /// Wall time of the maintenance sweep of this pass, ms; 0 when the pass had no sweep
    /// (`MAINTENANCE_SWEEP_TICKS` cadence).
    pub maintenance_ms: u64,
}

/// `AIManager`.
#[derive(Debug, Default)]
pub struct AiManager {
    ais: DashMap<u32, Arc<AiHandle>>,
    /// Monotonic `AddAi` ordinal source; the only ordering of the registry (no id list to scan).
    next_ordinal: AtomicU64,
    /// Explicit candidates outside player activity: persistent paths / follow targets / command
    /// queues / finite ongoing work (`NpcAi::has_persistent_ai`) and AIs with calls still
    /// pending, keyed by handle ordinal. Refreshed after every AI state change ([`post`],
    /// [`run_now`], the tick drains).
    woken: DashMap<u64, Arc<AiHandle>>,
    /// `ordinal -> (tick sequence of AddAi, handle)`: candidates for [`INITIALIZING_TICKS`] ticks
    /// even while no callback has reached them yet.
    initializing: DashMap<u64, (u64, Arc<AiHandle>)>,
    /// `NpcTemplate.ActivateAiAlways`: candidates on every tick, players or not, by ordinal.
    always: DashMap<u64, Arc<AiHandle>>,
    /// Tick counter of [`tick_all`] (initialization freshness + sweep cadence).
    tick_seq: AtomicU64,
    /// Counts of the last tick.
    last_tick: Mutex<AiTickDiagnostics>,
    /// Serializes asynchronous batches: [`tick_batch`] is public and may be called
    /// concurrently, and two batches must never overlap on one manager.
    batch_lock: tokio::sync::Mutex<()>,
    /// Test-only: ordinals of the handles whose `NpcAi::Tick` ran, in execution order.
    #[cfg(test)]
    tick_order: Mutex<Vec<u64>>,
    started: AtomicBool,
}

impl AiManager {
    /// `AIManager.AddAi(ai)` (+ `npc.Ai = ai`). An already registered object id (recycled id) is
    /// a replacement: the old handle is marked removed and its claims are dropped, so a stale
    /// snapshot can never tick again or touch the replacement's candidate state.
    pub fn add_ai(&self, npc: &NpcRef, kind: AiKind) -> Arc<AiHandle> {
        let h = Arc::new(AiHandle {
            obj_id: npc.obj_id,
            ordinal: self.next_ordinal.fetch_add(1, Ordering::Relaxed),
            state: Mutex::new(NpcAi::new(kind, npc)),
            pending: Mutex::new(VecDeque::new()),
            removed: AtomicBool::new(false),
            visits: AtomicU64::new(0),
        });
        if let Some(old) = self.ais.insert(npc.obj_id, h.clone()) {
            self.retire(&old);
        }
        self.initializing.insert(
            h.ordinal,
            (self.tick_seq.load(Ordering::Relaxed), h.clone()),
        );
        npc.data().has_ai = true;
        h
    }

    /// Marks an AI as always active (`NpcTemplate.ActivateAiAlways`): it stays a candidate
    /// even when no player is in its region.
    pub fn mark_always_active(&self, obj_id: u32) {
        if let Some(h) = self.get(obj_id) {
            if !h.removed.load(Ordering::Acquire) {
                self.always.insert(h.ordinal, h);
            }
        }
    }

    /// The AI of an NPC.
    pub fn get(&self, obj_id: u32) -> Option<Arc<AiHandle>> {
        self.ais.get(&obj_id).map(|h| h.clone())
    }

    /// `Npc.Delete()`: `Ai.Owner = null; Ai = null` (the AI stops ticking and is forgotten).
    pub fn remove(&self, obj_id: u32) {
        if let Some((_, h)) = self.ais.remove(&obj_id) {
            self.retire(&h);
        }
    }

    /// Removes the registry entry only while it still is the expected handle (a stale snapshot
    /// of a replaced handle must never drop the replacement).
    fn remove_if_current(&self, h: &Arc<AiHandle>) -> bool {
        let Some((_, current)) = self.ais.remove_if(&h.obj_id, |_, v| Arc::ptr_eq(v, h)) else {
            return false;
        };
        self.retire(&current);
        true
    }

    /// Whether `h` still is the current registry entry of its object id. Claims are keyed by the
    /// handle ordinal, so an object-id reuse replaces the registry entry with a different handle.
    fn is_current_handle(&self, h: &Arc<AiHandle>) -> bool {
        self.ais
            .get(&h.obj_id)
            .is_some_and(|cur| Arc::ptr_eq(cur.value(), h))
    }

    /// Stops an AI and drops every claim it holds (registry entry or sets). Idempotent; the
    /// state itself detaches when a busy holder drains.
    fn retire(&self, h: &AiHandle) {
        h.removed.store(true, Ordering::Release);
        lock(&h.pending).clear();
        if let Some(mut st) = try_lock(&h.state) {
            h.detach_locked(&mut st);
        }
        self.drop_handle_sets(h.ordinal);
    }

    /// Drops the candidate claims of one handle ordinal (replacement / removal clean-up). A
    /// stale handle can only ever touch its own ordinal, never a replacement's.
    fn drop_handle_sets(&self, ordinal: u64) {
        self.woken.remove(&ordinal);
        self.initializing.remove(&ordinal);
        self.always.remove(&ordinal);
    }

    /// Cleanup safety net of one sweep: drops the claims whose handle no longer is the current
    /// registry entry of its object id (replaced / removed) and the initialization claims past
    /// their grace period. Each claim is verified against the **current** registry, never
    /// against the sweep's snapshot: a handle registered while the sweep runs keeps its
    /// initialization grace and `ActivateAiAlways` claim. The claim handles are collected with
    /// the registry locks released before the registry is read, so no two registry locks are
    /// held at once; removals use the claim's own ordinal key, which is unique per handle.
    fn prune_claims(&self) {
        let seq = self.tick_seq.load(Ordering::Relaxed);
        let woken: Vec<(u64, Arc<AiHandle>)> = self
            .woken
            .iter()
            .map(|e| (*e.key(), e.value().clone()))
            .collect();
        let always: Vec<(u64, Arc<AiHandle>)> = self
            .always
            .iter()
            .map(|e| (*e.key(), e.value().clone()))
            .collect();
        let initializing: Vec<(u64, u64, Arc<AiHandle>)> = self
            .initializing
            .iter()
            .map(|e| {
                let (born, h) = e.value();
                (*e.key(), *born, h.clone())
            })
            .collect();
        for (ordinal, h) in woken {
            if !self.is_current_handle(&h) {
                self.woken.remove(&ordinal);
            }
        }
        for (ordinal, h) in always {
            if !self.is_current_handle(&h) {
                self.always.remove(&ordinal);
            }
        }
        for (ordinal, born, h) in initializing {
            if !self.is_current_handle(&h) || seq.saturating_sub(born) > INITIALIZING_TICKS {
                self.initializing.remove(&ordinal);
            }
        }
    }

    pub fn len(&self) -> usize {
        self.ais.len()
    }

    pub fn is_empty(&self) -> bool {
        self.ais.is_empty()
    }

    /// Reads an AI's state (tests / diagnostics; blocks while the AI runs).
    pub fn inspect<R>(&self, obj_id: u32, f: impl FnOnce(&NpcAi) -> R) -> Option<R> {
        let h = self.get(obj_id)?;
        let st = lock(&h.state);
        Some(f(&st))
    }

    /// Reads an AI's state only while it is actually readable (tests / diagnostics; never
    /// blocks). `None` when there is no such AI, it was replaced, or another thread currently
    /// holds its state (the tick's try-lock discipline).
    pub fn try_inspect<R>(&self, obj_id: u32, f: impl FnOnce(&NpcAi) -> R) -> Option<R> {
        let h = self.get(obj_id)?;
        if h.removed.load(Ordering::Acquire) {
            return None;
        }
        let st = try_lock(&h.state)?;
        if h.removed.load(Ordering::Acquire) {
            return None; // retired between the check above and acquiring the state
        }
        Some(f(&st))
    }

    /// Counts of the last [`tick_all`] pass.
    pub fn tick_stats(&self) -> AiTickDiagnostics {
        *lock(&self.last_tick)
    }

    /// How often `NpcAi::Tick` ran on an AI (`None` = no such AI).
    pub fn visits(&self, obj_id: u32) -> Option<u64> {
        self.ais
            .get(&obj_id)
            .map(|h| h.visits.load(Ordering::Relaxed))
    }

    /// Re-evaluates the explicit candidate set after an AI state change (a persistent path /
    /// follow target / queued command / finite ongoing work or calls still pending keep it
    /// woken; a dormant AI with none of those leaves the set). A replaced handle (`removed`) is
    /// a no-op: it must not clear the wake state of the handle that replaced it.
    fn refresh_woken(&self, h: &Arc<AiHandle>) {
        if h.removed.load(Ordering::Acquire) {
            return;
        }
        if let Some(st) = try_lock(&h.state) {
            let keep = st.has_persistent_ai() || !lock(&h.pending).is_empty();
            self.set_woken(h, keep);
        } else if !lock(&h.pending).is_empty() {
            // The current holder runs the queue; keep the AI in reach of the next tick.
            self.set_woken(h, true);
        }
    }

    fn set_woken(&self, h: &Arc<AiHandle>, woken: bool) {
        if h.removed.load(Ordering::Acquire) {
            return;
        }
        if woken {
            self.woken.insert(h.ordinal, h.clone());
        } else {
            self.woken.remove(&h.ordinal);
        }
    }
}

/// `npc.Ai != null`.
pub fn has_ai(server: &GameServer, obj_id: u32) -> bool {
    server.systems.ai.ais.get(obj_id).is_some()
}

/// Runs `f` on the NPC's AI now, or right after its current step (see the module docs).
/// Nothing happens for an NPC without an AI (`npc.Ai?.X()`).
pub fn post(server: &GameServer, obj_id: u32, f: impl FnOnce(&mut AiCtx<'_>) + Send + 'static) {
    let Some(h) = server.systems.ai.ais.get(obj_id) else {
        return;
    };
    lock(&h.pending).push_back(Box::new(f));
    h.try_drain(server);
    server.systems.ai.ais.refresh_woken(&h);
}

/// Runs `f` on the NPC's AI right away (waiting for its current step to finish) and returns
/// the result: a synchronous `npc.Ai.X()` call from outside the AI (GM commands). `None` for an
/// NPC without an AI (`npc.Ai == null`). Calls queued meanwhile run afterwards.
pub fn run_now<R>(
    server: &GameServer,
    obj_id: u32,
    f: impl FnOnce(&mut AiCtx<'_>) -> R,
) -> Option<R> {
    let h = server.systems.ai.ais.get(obj_id)?;
    let res = {
        let mut st = lock(&h.state);
        if h.removed.load(Ordering::Acquire) {
            return None;
        }
        let npc = st.owner()?;
        let obj = WorldObject::from(&npc);
        let r = {
            let mut ctx = AiCtx {
                server,
                npc,
                obj,
                ai: &mut st,
            };
            f(&mut ctx)
        };
        h.drain_locked(server, &mut st);
        r
    };
    h.try_drain(server);
    server.systems.ai.ais.refresh_woken(&h);
    Some(res)
}

/// Collects the handles wanted for one tick: the NPCs whose regions have player activity
/// (`Region.HasPlayerActivity()`), plus the woken / initializing / always-active sets, keyed by
/// handle ordinal. Only wanted handles are touched (the registry is never scanned here); the
/// monotonic `AddAi` ordinal gives the order and the deduplication. The returned set holds the
/// ordinals of the locally active handles (their tick gets `region_activity = true`).
fn tick_candidates(server: &GameServer, seq: u64) -> (Vec<Arc<AiHandle>>, HashSet<u64>) {
    let manager = &server.systems.ai.ais;
    let mut local = HashSet::new();
    let mut wanted: BTreeMap<u64, Arc<AiHandle>> = BTreeMap::new();
    let mut want = |h: Arc<AiHandle>, is_local: bool| {
        if h.removed.load(Ordering::Acquire) {
            return;
        }
        if is_local {
            local.insert(h.ordinal);
        }
        wanted.entry(h.ordinal).or_insert(h);
    };
    for world in server.world.worlds() {
        let mut sectors = HashSet::new();
        for p in world.all_characters() {
            let Some((inst, x, y)) = p.region() else {
                continue;
            };
            if inst != world.id {
                continue;
            }
            sectors.extend(world.neighbors((x, y)));
        }
        for sector in sectors {
            for obj in world.objects_in_region(sector) {
                if let Some(npc) = obj.as_npc() {
                    if let Some(h) = manager.get(npc.obj_id) {
                        want(h, true);
                    }
                }
            }
        }
    }
    for e in manager.woken.iter() {
        want(e.value().clone(), false);
    }
    for e in manager.initializing.iter() {
        let (born, h) = e.value();
        if seq.saturating_sub(*born) <= INITIALIZING_TICKS {
            want(h.clone(), false);
        }
    }
    for e in manager.always.iter() {
        want(e.value().clone(), false);
    }
    (wanted.into_values().collect(), local)
}

/// Processes one selected handle: the shared body of the sequential pass and the batch chunk
/// jobs (so the two paths cannot drift apart). Try-lock discipline unchanged: a busy AI is left
/// for the next tick, a replaced / gone one is detached through its own handle only.
fn tick_handle(
    server: &GameServer,
    h: &Arc<AiHandle>,
    local: &HashSet<u64>,
    delta_ms: i64,
    diag: &mut AiTickDiagnostics,
) {
    let manager = &server.systems.ai.ais;
    if h.removed.load(Ordering::Acquire) {
        return; // replaced after collection: `add_ai` already dropped its claims
    }
    let id = h.obj_id;
    let mut gone = false;
    {
        // External skill/aggro calls also run under this lock. Leave a busy
        // NPC for the next tick instead of stopping every other NPC's AI.
        let Some(mut st) = try_lock(&h.state) else {
            diag.busy += 1;
            return;
        };
        if h.removed.load(Ordering::Acquire) {
            // Replaced while waiting for the lock: retire the snapshot, not the replacement.
            h.detach_locked(&mut st);
            manager.drop_handle_sets(h.ordinal);
            return;
        }
        match st.owner() {
            None => gone = true,
            Some(npc) => {
                let region = npc.region();
                if region.is_some() {
                    st.was_in_world = true;
                } else if st.was_in_world && server.world.object(npc.obj_id).is_none() {
                    // Npc.Delete(): the AI is detached
                    gone = true;
                }
                if !gone {
                    let obj = WorldObject::from(&npc);
                    let activity =
                        local.contains(&h.ordinal) || manager.always.contains_key(&h.ordinal);
                    let mut ctx = AiCtx {
                        server,
                        npc,
                        obj,
                        ai: &mut st,
                    };
                    h.visits.fetch_add(1, Ordering::Relaxed);
                    diag.ticked += 1;
                    #[cfg(test)]
                    lock(&manager.tick_order).push(h.ordinal);
                    if catch_unwind(AssertUnwindSafe(|| ctx.tick(delta_ms, activity))).is_err() {
                        tracing::error!("AI tick of npc {id} panicked");
                    }
                }
            }
        }
        if !gone {
            h.drain_locked(server, &mut st);
        }
    }
    if gone {
        if manager.remove_if_current(h) {
            diag.removed += 1;
        }
    } else {
        h.try_drain(server);
        manager.refresh_woken(h);
    }
}

/// Runs a slice of the candidate plan sequentially (shared by [`tick_all`] and every chunk job).
/// A panic on one handle is contained: the remaining handles still run and the panicking
/// handle's lock / selection state is released by unwinding through the existing guards.
fn process_plan(
    server: &GameServer,
    candidates: &[Arc<AiHandle>],
    local: &HashSet<u64>,
    delta_ms: i64,
    diag: &mut AiTickDiagnostics,
) {
    for h in candidates {
        if catch_unwind(AssertUnwindSafe(|| {
            tick_handle(server, h, local, delta_ms, diag)
        }))
        .is_err()
        {
            tracing::error!("AI tick of npc {} panicked", h.obj_id);
        }
    }
}

/// One chunk job of a multi-worker batch: its own partial diagnostics, no shared mutable state
/// between chunks.
fn process_chunk(
    server: &GameServer,
    chunk: &[Arc<AiHandle>],
    local: &HashSet<u64>,
    delta_ms: i64,
) -> AiTickDiagnostics {
    let mut diag = AiTickDiagnostics::default();
    process_plan(server, chunk, local, delta_ms, &mut diag);
    diag
}

/// Splits the ordinal-sorted plan into at most `workers` contiguous chunks: a bounded number of
/// jobs per batch (never one task per NPC) that keeps the `AddAi` order inside every chunk. An
/// empty plan costs no jobs.
fn plan_chunks(candidates: &[Arc<AiHandle>], workers: usize) -> Vec<Vec<Arc<AiHandle>>> {
    if candidates.is_empty() {
        return Vec::new();
    }
    let workers = workers.clamp(1, MAX_AI_WORKERS);
    if workers == 1 {
        return vec![candidates.to_vec()];
    }
    let chunk_size = candidates.len().div_ceil(workers).max(1);
    candidates.chunks(chunk_size).map(|c| c.to_vec()).collect()
}

/// Shared tail of one pass: the low-frequency maintenance sweep (once per batch) and the
/// diagnostics snapshot. `maintenance_ms` is the sweep's own wall time; a pass without a sweep
/// keeps the zeroed default.
fn finish_pass(server: &GameServer, seq: u64, diag: &mut AiTickDiagnostics) {
    if seq.is_multiple_of(MAINTENANCE_SWEEP_TICKS) {
        let started = Instant::now();
        maintenance_sweep(server, diag);
        diag.maintenance_ms = started.elapsed().as_millis() as u64;
    }
    *lock(&server.systems.ai.ais.last_tick) = *diag;
}

/// `AIManager.Tick(delta)`: the candidate AIs tick (`NpcAi.Tick`), in `AddAi` order. The
/// synchronous, always-sequential entry kept for existing callers / tests; production uses
/// [`tick_batch`].
///
/// Candidates are the NPCs in regions with player activity plus the woken / initializing /
/// always-active AIs, collected by handle (deduplicated by ordinal, sorted by ordinal). A busy
/// AI is left for the next tick; a gone / replaced one is detached through its own handle.
/// Dormant AIs outside every player neighbourhood are neither locked nor visited.
pub fn tick_all(server: &GameServer, delta_ms: i64) -> AiTickDiagnostics {
    let manager = &server.systems.ai.ais;
    let seq = manager.tick_seq.fetch_add(1, Ordering::Relaxed) + 1;
    let plan_started = Instant::now();
    let (candidates, local) = tick_candidates(server, seq);
    let plan_ms = plan_started.elapsed().as_millis() as u64;
    let mut diag = AiTickDiagnostics {
        candidates: candidates.len(),
        plan_ms,
        ..AiTickDiagnostics::default()
    };
    let tick_started = Instant::now();
    process_plan(server, &candidates, &local, delta_ms, &mut diag);
    diag.tick_ms = tick_started.elapsed().as_millis() as u64;
    finish_pass(server, seq, &mut diag);
    diag
}

/// Concurrent chunk jobs for one asynchronous batch: `World.AiWorkerThreads` clamped to
/// `1..=MAX_AI_WORKERS`. Read once per batch (the configuration is not re-read while the
/// batch runs); the default `1` keeps the strict `AddAi` execution order of [`tick_all`].
fn batch_worker_count(server: &GameServer) -> usize {
    (server.config.world.ai_worker_threads as usize).clamp(1, MAX_AI_WORKERS)
}

/// One asynchronous tick batch on the existing Tokio blocking pool. The candidate plan is built
/// once; the default worker count runs it as the single sequential pass of [`tick_all`], a
/// configured count splits it into at most that many contiguous chunk jobs (never one task per
/// NPC). Every job is awaited before the diagnostics are aggregated once and the maintenance
/// sweep runs once per batch. Concurrent calls serialize on the manager's batch lock; a
/// panicking job is logged and cannot hold up or overlap the next batch. The batch runs in an
/// owned supervisor (see `tick_batch_with_workers`), so cancelling the caller cannot release
/// the lock while its jobs are still running.
pub async fn tick_batch(server: &Arc<GameServer>, delta_ms: i64) -> AiTickDiagnostics {
    let started = Instant::now();
    let diag = tick_batch_with_workers(server, delta_ms, batch_worker_count(server)).await;
    // Opt-in per-batch profile (`RUST_LOG` at debug). `total_ms` is the whole awaited
    // batch as seen here: supervisor spawn, batch-lock serialization and blocking-pool dispatch
    // are included; `plan_ms`/`maintenance_ms` are measured inside their jobs and the parallel
    // `tick_ms` spans the whole chunk phase (see `AiTickDiagnostics`). Any remaining gap can
    // include supervisor, batch-lock and finalization overhead; it does not identify one cause.
    tracing::debug!(
        total_ms = started.elapsed().as_millis() as u64,
        plan_ms = diag.plan_ms,
        tick_ms = diag.tick_ms,
        maintenance_ms = diag.maintenance_ms,
        candidates = diag.candidates,
        ticked = diag.ticked,
        busy = diag.busy,
        removed = diag.removed,
        "AI tick batch profile"
    );
    diag
}

/// [`tick_batch`] with an explicit worker count (clamped; tests): spawns the owning supervisor
/// and awaits its result. The supervisor gets its own `Arc<GameServer>` and runs the complete
/// serialized batch in `run_batch`; cancelling this future (dropping the caller's await) only
/// abandons the `JoinHandle` await, while the detached supervisor keeps working and releases the
/// batch lock after the last job. The next batch can therefore never overlap the in-flight work.
async fn tick_batch_with_workers(
    server: &Arc<GameServer>,
    delta_ms: i64,
    workers: usize,
) -> AiTickDiagnostics {
    let srv = server.clone();
    let supervisor = tokio::spawn(run_batch(srv, delta_ms, workers));
    match supervisor.await {
        Ok(diag) => diag,
        Err(e) => {
            tracing::error!("AI tick batch failed: {e}");
            *lock(&server.systems.ai.ais.last_tick)
        }
    }
}

/// The in-flight batch owned by the [`tick_batch_with_workers`] supervisor: acquires the batch
/// lock and holds it over the whole serialized pass (plan, chunk jobs, aggregation, sweep). Only
/// that supervisor calls it, so a cancelled caller can neither drop a half-finished batch nor
/// release the lock early.
async fn run_batch(server: Arc<GameServer>, delta_ms: i64, workers: usize) -> AiTickDiagnostics {
    let manager = &server.systems.ai.ais;
    let _batch = manager.batch_lock.lock().await;
    let workers = workers.clamp(1, MAX_AI_WORKERS);

    if workers == 1 {
        // Default: exactly the sequential pass, on one blocking thread.
        let srv = server.clone();
        return match tokio::task::spawn_blocking(move || tick_all(&srv, delta_ms)).await {
            Ok(diag) => diag,
            Err(e) => {
                tracing::error!("AI tick task failed: {e}");
                *lock(&manager.last_tick)
            }
        };
    }

    // Build the deduplicated plan once, on the blocking pool as well (it walks the player
    // regions): a failure leaves the candidate sets untouched for the next batch. `plan_ms` is
    // measured inside the job (candidate construction only; queue time excluded).
    let srv = server.clone();
    let plan = tokio::task::spawn_blocking(move || {
        let seq = srv.systems.ai.ais.tick_seq.fetch_add(1, Ordering::Relaxed) + 1;
        let started = Instant::now();
        let (candidates, local) = tick_candidates(&srv, seq);
        let plan_ms = started.elapsed().as_millis() as u64;
        (seq, candidates, local, plan_ms)
    })
    .await;
    let (seq, candidates, local, plan_ms) = match plan {
        Ok(plan) => plan,
        Err(e) => {
            tracing::error!("AI tick planning failed: {e}");
            return *lock(&manager.last_tick);
        }
    };

    let local = Arc::new(local);
    let mut jobs = Vec::new();
    // One wall-clock span over the whole parallel processing phase: it starts just before the
    // chunk split/dispatch and ends when every chunk await has completed. It therefore includes
    // chunk dispatch, blocking-pool queueing and await overhead in addition to the workers'
    // execution; `plan_ms` and `maintenance_ms` are measured inside their jobs. The difference
    // between the batch total and these stages is not attributed to any single cause.
    let tick_started = Instant::now();
    for chunk in plan_chunks(&candidates, workers) {
        let srv = server.clone();
        let local = Arc::clone(&local);
        jobs.push(tokio::task::spawn_blocking(move || {
            process_chunk(&srv, &chunk, &local, delta_ms)
        }));
    }
    let mut diag = AiTickDiagnostics {
        candidates: candidates.len(),
        plan_ms,
        ..AiTickDiagnostics::default()
    };
    for job in jobs {
        match job.await {
            Ok(part) => {
                diag.ticked += part.ticked;
                diag.busy += part.busy;
                diag.removed += part.removed;
            }
            Err(e) => tracing::error!("AI tick chunk failed: {e}"),
        }
    }
    diag.tick_ms = tick_started.elapsed().as_millis() as u64;

    // The registry sweep and the diagnostics snapshot stay on the blocking pool, once.
    let base = diag;
    let srv = server.clone();
    match tokio::task::spawn_blocking(move || {
        let mut diag = base;
        finish_pass(&srv, seq, &mut diag);
        diag
    })
    .await
    {
        Ok(diag) => diag,
        Err(e) => {
            tracing::error!("AI tick finalize failed: {e}");
            *lock(&manager.last_tick)
        }
    }
}

/// Low-frequency sweep over every registered AI: drop the ones whose owner / NPC object is gone
/// (normal deletion detaches through `npc_deleted`; this covers direct `world.delete` and
/// dropped owners) and re-wake the ones with persistent state or pending calls. Runs with the
/// same try-lock discipline as the tick, so a busy AI is left to its holder; a stale snapshot
/// only removes the registry entry while it still is the current handle, and the claim cleanup
/// verifies every claim against the current registry, never against the snapshot.
fn maintenance_sweep(server: &GameServer, diag: &mut AiTickDiagnostics) {
    maintenance_sweep_with(server, diag, || {});
}

/// [`maintenance_sweep`] with a callback between the registry walk and the claim cleanup: the
/// production entry passes a no-op, the regression test registers an AI exactly inside the
/// window. The callback runs while no registry lock is held.
fn maintenance_sweep_with(
    server: &GameServer,
    diag: &mut AiTickDiagnostics,
    after_walk: impl FnOnce(),
) {
    let manager = &server.systems.ai.ais;
    // Snapshot the registry first: never hold a registry lock while locking an AI.
    let handles: Vec<Arc<AiHandle>> = manager.ais.iter().map(|e| e.value().clone()).collect();
    let mut stale = Vec::new();
    let mut wake = Vec::new();
    for h in handles {
        if h.removed.load(Ordering::Acquire) {
            continue;
        }
        let Some(mut st) = try_lock(&h.state) else {
            continue;
        };
        match st.owner() {
            None => stale.push(h.clone()),
            Some(npc) => {
                let region = npc.region();
                if region.is_some() {
                    st.was_in_world = true;
                } else if st.was_in_world && server.world.object(npc.obj_id).is_none() {
                    stale.push(h.clone());
                    continue;
                }
                if st.has_persistent_ai() || !lock(&h.pending).is_empty() {
                    wake.push(h.clone());
                }
            }
        }
    }
    for h in stale {
        if manager.remove_if_current(&h) {
            diag.removed += 1;
        }
    }
    for h in wake {
        manager.set_woken(&h, true);
    }
    after_walk();
    // Safety net for claims of handles that are no longer current (replaced / removed), and
    // age-out of the initialization grace so the set stays proportional to recent adds. The
    // verification consults the current registry per claim, so a handle registered after the
    // snapshot above keeps its claims.
    manager.prune_claims();
}

/// `AIManager.Initialize()`: the 100 ms tick (one batch at a time, measured delta), on the
/// blocking pool so long ticks do not stall the runtime.
pub fn start_tick(server: &Arc<GameServer>) {
    let manager = &server.systems.ai.ais;
    if manager.started.swap(true, Ordering::SeqCst) {
        return;
    }
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut last = Instant::now();
        loop {
            tokio::time::sleep(Duration::from_millis(20)).await;
            let delta = last.elapsed();
            if delta <= Duration::from_millis(100) {
                continue;
            }
            last = Instant::now();
            let Some(server) = weak.upgrade() else { return };
            let delta_ms = delta.as_millis() as i64;
            let started = Instant::now();
            let diag = tick_batch(&server, delta_ms).await;
            let took = started.elapsed();
            if took > Duration::from_millis(100) {
                tracing::warn!(
                    "AI tick took {}ms to finish (plan {}ms, tick {}ms, maintenance {}ms; candidates {}, ticked {}, busy {}, removed {})",
                    took.as_millis(),
                    diag.plan_ms,
                    diag.tick_ms,
                    diag.maintenance_ms,
                    diag.candidates,
                    diag.ticked,
                    diag.busy,
                    diag.removed
                );
            }
        }
    });
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::{mpsc, Arc};
    use std::time::Duration;

    use crate::data::GameData;
    use crate::models::npc::Npc;
    use crate::systems::ai::framework::BehaviorKind;
    use crate::systems::ai::params::{AiCommandCategory, AiCommands};
    use crate::systems::ai::path_handler::{AiPathPoint, AiPathPointAction};
    use crate::systems::npcs::npc::data::NpcTemplate;
    use crate::systems::skills::controllers::Vec3;
    use crate::test_support::*;
    use crate::world::{Entity, NpcRef, PlayerRef};

    use super::*;

    fn server() -> Arc<GameServer> {
        let server = Arc::new(test_server_with(GameData::empty()));
        server.set_self();
        server
    }

    /// Creates an AI-backed NPC and spawns it (without the spawner hooks: `GoToSpawn` stays
    /// unqueued, so the behavior state is `None` and ticking only runs the aggro check).
    fn spawn_npc(
        server: &GameServer,
        obj_id: u32,
        template: u32,
        x: f32,
        y: f32,
        kind: AiKind,
    ) -> NpcRef {
        let mut n = Npc::new(template);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(x, y, 100.0);
        n.unit.transform.zone_id = TEST_ZONE;
        let npc = Entity::new(obj_id, n);
        server.systems.ai.ais.add_ai(&npc, kind);
        let world = server.world.world(0).expect("world");
        world.spawn(&npc, &server.view_ctx());
        npc
    }

    fn spawn_player_at(
        server: &GameServer,
        obj_id: u32,
        x: f32,
        y: f32,
    ) -> (TestClient, PlayerRef) {
        let client = TestClient::new(server, obj_id, 1);
        let c = test_character(obj_id, "Tester", x, y);
        let p = spawn_player(server, &client, c, obj_id);
        (client, p)
    }

    fn idle(server: &GameServer, ticks: usize) {
        for _ in 0..ticks {
            tick_all(server, 100);
        }
    }

    /// Production-equivalent spawn (`NpcSpawnerNpc.Spawn`): the home / idle position and
    /// `GoToSpawn()` are posted, so the behavior machine starts in `Spawning`.
    fn spawn_initialized_npc(
        server: &GameServer,
        obj_id: u32,
        template: u32,
        x: f32,
        y: f32,
        kind: AiKind,
    ) -> NpcRef {
        let npc = spawn_npc(server, obj_id, template, x, y, kind);
        crate::systems::ai::hooks::spawn_positioned(server, &npc);
        npc
    }

    fn behavior(server: &GameServer, obj_id: u32) -> Option<BehaviorKind> {
        server
            .systems
            .ai
            .ais
            .inspect(obj_id, |ai| ai.current_behavior())
            .flatten()
    }

    /// Without players and without persistent state an AI leaves the candidate set once its
    /// initialization grace is over: no lock, no visit.
    #[tokio::test]
    async fn dormant_ai_is_not_visited_without_player_activity() {
        let server = server();
        let local = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let far = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 0, "nothing to tick: {stats:?}");
        assert_eq!(stats.ticked, 0);
        let local_visits = server.systems.ai.ais.visits(0x210).unwrap();
        let far_visits = server.systems.ai.ais.visits(0x220).unwrap();
        idle(&server, 5);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 0, "{stats:?}");
        assert_eq!(stats.ticked, 0);
        assert_eq!(server.systems.ai.ais.visits(0x210).unwrap(), local_visits);
        assert_eq!(server.systems.ai.ais.visits(0x220).unwrap(), far_visits);
        drop((local, far));
    }

    /// Production-equivalent spawn without players: the initialization grace actually runs the
    /// behavior machine (`Spawning` -> `RunCommandSet` -> `Idle`) and the NPC then leaves the
    /// candidate set. A distant unrelated idle NPC stays unvisited during and after the settle.
    #[tokio::test]
    async fn prewarmed_spawn_initialization_finishes_and_then_sleeps() {
        let server = server();
        let unrelated = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        assert_eq!(server.systems.ai.ais.tick_stats().candidates, 0);
        let unrelated_visits = server.systems.ai.ais.visits(0x220).unwrap();

        let npc = spawn_initialized_npc(&server, 0x210, 1, 100.0, 100.0, AiKind::Default);
        assert_eq!(behavior(&server, 0x210), Some(BehaviorKind::Spawning));

        // No player anywhere: the grace still advances the spawning steps to the settle state.
        let mut settled = false;
        for _ in 0..INITIALIZING_TICKS as usize {
            let stats = tick_all(&server, 100);
            assert_eq!(
                (stats.candidates, stats.ticked),
                (1, 1),
                "only the spawning NPC is a candidate: {stats:?}"
            );
            if behavior(&server, 0x210) == Some(BehaviorKind::Idle) {
                settled = true;
                break;
            }
        }
        assert!(
            settled,
            "the prewarmed NPC did not finish spawning: {:?}",
            behavior(&server, 0x210)
        );
        assert_eq!(
            server.systems.ai.ais.visits(0x220).unwrap(),
            unrelated_visits,
            "the unrelated distant idle NPC must not be visited"
        );

        // Past the grace the settled NPC is dormant again: no selection, no visits.
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 0, "{stats:?}");
        assert_eq!(stats.ticked, 0, "{stats:?}");
        let visits = server.systems.ai.ais.visits(0x210).unwrap();
        idle(&server, 5);
        assert_eq!(server.systems.ai.ais.visits(0x210).unwrap(), visits);
        assert_eq!(
            server.systems.ai.ais.visits(0x220).unwrap(),
            unrelated_visits
        );
        drop((npc, unrelated));
    }

    /// A displaced production-spawned NPC walks back to its home even after the player that kept
    /// its region active leaves before the return completes.
    #[tokio::test]
    async fn displaced_return_finishes_after_region_activity_disappears() {
        let server = server();
        let npc = spawn_initialized_npc(&server, 0x210, 1, 1500.0, 1500.0, AiKind::HoldPosition);
        let (_client, player) = spawn_player_at(&server, 0x100, 1510.0, 1500.0);
        idle(&server, 3);

        // Displace the NPC 6 m from home and start the return.
        npc.data().unit.transform.set_xyz(1506.0, 1500.0, 100.0);
        post(&server, 0x210, |c| c.go_to_return());
        assert_eq!(behavior(&server, 0x210), Some(BehaviorKind::ReturnState));

        // The player leaves: no region activity marks the NPC local anymore.
        player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
        let world = server.world.world(0).expect("world");
        world.add_visible_object(&player, &server.view_ctx());
        let (candidates, local) = tick_candidates(&server, 0);
        assert_eq!(candidates.len(), 1, "the returning NPC stays selected");
        assert!(local.is_empty(), "no region activity marks it local");

        // It still walks home and settles, with no player anywhere near it.
        let mut home = false;
        for _ in 0..30 {
            tick_all(&server, 500);
            let (x, y) = {
                let t = npc.data().unit.transform;
                (t.x, t.y)
            };
            if behavior(&server, 0x210) == Some(BehaviorKind::Idle)
                && (x - 1500.0).abs() < 1.5
                && (y - 1500.0).abs() < 1.5
            {
                home = true;
                break;
            }
        }
        assert!(
            home,
            "the return did not finish without region activity: {:?}",
            behavior(&server, 0x210)
        );
        drop(npc);
    }

    /// A production-spawned NPC in combat keeps ticking after the player leaves its region: the
    /// stale aggro is cleared and the NPC returns home even though no region activity selects it.
    #[tokio::test]
    async fn combat_cleanup_finishes_after_the_player_leaves_the_region() {
        let server = server();
        let npc = spawn_initialized_npc(&server, 0x210, 1, 1500.0, 1500.0, AiKind::HoldPosition);
        let (_client, player) = spawn_player_at(&server, 0x100, 1510.0, 1500.0);
        idle(&server, 3);

        // Enter combat with an aggro entry, then the player leaves the whole neighbourhood.
        let player_obj = WorldObject::from(&player);
        let npc_obj = WorldObject::from(&npc);
        super::super::combat::add_unit_aggro(
            &server,
            &npc_obj,
            crate::systems::skills::state::AggroKind::Damage,
            &player_obj,
            10,
        );
        post(&server, 0x210, |c| c.go_to_combat());
        assert_eq!(behavior(&server, 0x210), Some(BehaviorKind::Attack));
        player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
        let world = server.world.world(0).expect("world");
        world.add_visible_object(&player, &server.view_ctx());

        // No region activity selects it, but the combat cleanup must still run.
        let (candidates, local) = tick_candidates(&server, 0);
        assert_eq!(candidates.len(), 1, "the combat NPC stays selected");
        assert!(local.is_empty(), "no region activity marks it local");
        let mut cleared = false;
        for _ in 0..5 {
            tick_all(&server, 100);
            if behavior(&server, 0x210) == Some(BehaviorKind::Idle)
                && npc.data().unit.skill_state.aggro_table.is_empty()
            {
                cleared = true;
                break;
            }
        }
        assert!(
            cleared,
            "the stale combat state was not cleared: {:?}",
            behavior(&server, 0x210)
        );
        assert!(!npc.data().unit.is_in_battle);
        drop(npc);
    }

    /// An in-progress AI command keeps the NPC ticking after its queue entry was popped (the
    /// queue is empty, only `CurrentCommand` remains): the wait still finishes and the NPC
    /// settles to idle, with no player anywhere.
    #[tokio::test]
    async fn in_progress_command_with_empty_queue_finishes_without_players() {
        let server = server();
        let npc = spawn_initialized_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        assert_eq!(server.systems.ai.ais.tick_stats().candidates, 0);

        post(&server, 0x220, |c| {
            c.enqueue_ai_commands(
                &[AiCommands {
                    id: 0,
                    cmd_set_id: 0,
                    cmd_id: AiCommandCategory::Timeout,
                    param1: 250,
                    param2: String::new(),
                }],
                false,
            );
        });
        assert_eq!(behavior(&server, 0x220), Some(BehaviorKind::RunCommandSet));

        // One tick pops the queued command: only the in-progress `CurrentCommand` remains.
        tick_all(&server, 100);
        assert_eq!(
            server.systems.ai.ais.inspect(0x220, |ai| (
                ai.commands_queue.is_empty(),
                ai.current_command.is_some()
            )),
            Some((true, true)),
            "the queued command must be popped and in progress"
        );
        assert_eq!(
            server
                .systems
                .ai
                .ais
                .inspect(0x220, |ai| ai.has_persistent_ai()),
            Some(true),
            "the in-progress command must keep the NPC awake"
        );

        // The wait (250 ms, overshooting the 100 ms tick) still finishes: the command is
        // cleared and the behavior settles to Idle without any region activity.
        let mut finished = false;
        for _ in 0..10 {
            tick_all(&server, 100);
            if behavior(&server, 0x220) == Some(BehaviorKind::Idle) {
                finished = true;
                break;
            }
        }
        assert!(
            finished,
            "the in-progress command froze: {:?}",
            behavior(&server, 0x220)
        );
        assert_eq!(
            server
                .systems
                .ai
                .ais
                .inspect(0x220, |ai| ai.current_command.is_none()),
            Some(true)
        );
        drop(npc);
    }

    /// A player arriving wakes the dormant AI on the very next tick; leaving the whole
    /// neighbourhood lets it rest again.
    #[tokio::test]
    async fn player_proximity_wakes_and_departure_sleeps_dormant_ai() {
        let server = server();
        let npc = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        assert_eq!(server.systems.ai.ais.tick_stats().candidates, 0);
        let before = server.systems.ai.ais.visits(0x210).unwrap();

        let (_client, player) = spawn_player_at(&server, 0x100, 115.0, 100.0);
        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "{stats:?}");
        assert_eq!(stats.ticked, 1);
        assert_eq!(server.systems.ai.ais.visits(0x210).unwrap(), before + 1);

        player.data().unit.transform.set_xyz(1500.0, 1500.0, 100.0);
        let world = server.world.world(0).expect("world");
        world.add_visible_object(&player, &server.view_ctx());
        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 0, "{stats:?}");
        let visits = server.systems.ai.ais.visits(0x210).unwrap();
        idle(&server, 3);
        assert_eq!(server.systems.ai.ais.visits(0x210).unwrap(), visits);
        drop(npc);
    }

    /// Two players watching the same regions still yield one candidate and one tick.
    #[tokio::test]
    async fn overlapping_player_regions_tick_each_ai_once() {
        let server = server();
        let npc = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let (_c1, _p1) = spawn_player_at(&server, 0x100, 115.0, 100.0);
        let (_c2, _p2) = spawn_player_at(&server, 0x101, 105.0, 100.0);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let before = server.systems.ai.ais.visits(0x210).unwrap();
        tick_all(&server, 100);
        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "one AI, two players: {stats:?}");
        assert_eq!(stats.ticked, 1);
        assert_eq!(server.systems.ai.ais.visits(0x210).unwrap(), before + 2);
        drop(npc);
    }

    /// A queued path point (persistent AI) keeps a distant NPC ticking without players; the
    /// same AI seen both locally and as woken is still ticked exactly once.
    #[tokio::test]
    async fn persistent_paths_keep_ticking_without_players() {
        let server = server();
        let npc = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        post(&server, 0x220, |c| {
            c.enqueue_path_point(AiPathPoint {
                position: Vec3::new(1500.0, 1500.0, 100.0),
                action: AiPathPointAction::None,
                param: String::new(),
            });
        });
        assert!(server
            .systems
            .ai
            .ais
            .inspect(0x220, |ai| ai.has_persistent_ai())
            .unwrap());
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "persistent AI must stay: {stats:?}");
        let before = server.systems.ai.ais.visits(0x220).unwrap();
        idle(&server, 5);
        assert_eq!(server.systems.ai.ais.visits(0x220).unwrap(), before + 5);

        let (_client, _player) = spawn_player_at(&server, 0x100, 1510.0, 1500.0);
        let before = server.systems.ai.ais.visits(0x220).unwrap();
        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "{stats:?}");
        assert_eq!(server.systems.ai.ais.visits(0x220).unwrap(), before + 1);
        drop(npc);
    }

    /// `follow_unit` escorts and queued AI commands are persistent too: they keep ticking
    /// without players, like the path-driven AIs.
    #[tokio::test]
    async fn escorts_and_queued_commands_keep_ticking_without_players() {
        let server = server();
        let escort = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        post(&server, 0x220, |c| {
            let target = WorldObject::from(&c.npc);
            c.ai.follow_unit = Some(target);
        });
        let queued = spawn_npc(&server, 0x221, 1, 1600.0, 1500.0, AiKind::Default);
        post(&server, 0x221, |c| {
            c.enqueue_ai_commands(
                &[AiCommands {
                    id: 0,
                    cmd_set_id: 0,
                    cmd_id: AiCommandCategory::UseSkill,
                    param1: 0,
                    param2: String::new(),
                }],
                true,
            );
        });
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 2, "{stats:?}");
        let escort_before = server.systems.ai.ais.visits(0x220).unwrap();
        let queued_before = server.systems.ai.ais.visits(0x221).unwrap();
        idle(&server, 4);
        assert_eq!(
            server.systems.ai.ais.visits(0x220).unwrap(),
            escort_before + 4
        );
        assert_eq!(
            server.systems.ai.ais.visits(0x221).unwrap(),
            queued_before + 4
        );
        drop((escort, queued));
    }

    /// A call queued while the AI is busy stays pending (and keeps the AI woken) and is run by
    /// a later tick, even with no player anywhere near the NPC.
    #[tokio::test]
    async fn pending_calls_of_a_busy_ai_run_on_a_later_tick_without_players() {
        let server = server();
        let npc = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        assert_eq!(server.systems.ai.ais.tick_stats().candidates, 0);

        // Hold the AI's lock from a worker thread (a long external call; `inspect` never
        // drains the queue, so only a tick can run the call once the lock is released).
        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        let srv = server.clone();
        let holder = std::thread::spawn(move || {
            let _ = srv.systems.ai.ais.inspect(0x220, |_| {
                entered_tx.send(()).unwrap();
                release_rx.recv().unwrap();
            });
        });
        entered_rx
            .recv_timeout(Duration::from_secs(5))
            .expect("AI lock not held");

        let ran = Arc::new(AtomicBool::new(false));
        let flag = ran.clone();
        post(&server, 0x220, move |_| flag.store(true, Ordering::SeqCst));
        assert!(!ran.load(Ordering::SeqCst), "the busy AI ran the call");

        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(
            stats.candidates, 1,
            "a pending call must wake the AI: {stats:?}"
        );
        assert_eq!(stats.busy, 1, "a busy AI is skipped, never waited for");
        assert!(!ran.load(Ordering::SeqCst));

        release_tx.send(()).unwrap();
        holder.join().unwrap();
        let mut fired = false;
        for _ in 0..5 {
            tick_all(&server, 100);
            if ran.load(Ordering::SeqCst) {
                fired = true;
                break;
            }
        }
        assert!(fired, "the queued call never ran through the tick");
        drop(npc);
    }

    /// Dropped owners are cleaned up promptly; a dormant AI of a directly deleted NPC is
    /// caught by the maintenance sweep; reused ids stay a single candidate per tick.
    #[tokio::test]
    async fn stale_handles_are_swept_and_reused_ids_stay_single_tick() {
        let server = server();
        // Owner dropped before the first tick: the initialization grace still visits it.
        let orphan = Entity::new(0x230, Npc::new(1));
        server.systems.ai.ais.add_ai(&orphan, AiKind::Default);
        drop(orphan);
        tick_all(&server, 100);
        assert!(!has_ai(&server, 0x230));

        // `world.delete` without the npc_deleted hook: only the low-frequency sweep sees it.
        let npc = spawn_npc(&server, 0x220, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        server.world.world(0).expect("world").delete(&npc);
        for _ in 0..(MAINTENANCE_SWEEP_TICKS as usize + 2) {
            if !has_ai(&server, 0x220) {
                break;
            }
            tick_all(&server, 100);
        }
        assert!(
            !has_ai(&server, 0x220),
            "the sweep did not drop the deleted AI"
        );
        drop(npc);

        // ID reuse: `add_ai` for an id that is already registered retires the replaced handle
        // and registers the new one - the candidate must still tick exactly once.
        let old = Entity::new(0x250, Npc::new(1));
        server.systems.ai.ais.add_ai(&old, AiKind::Default);
        let new = spawn_npc(&server, 0x250, 1, 1500.0, 1500.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 1510.0, 1500.0);
        let before = server.systems.ai.ais.visits(0x250).unwrap();
        tick_all(&server, 100);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "{stats:?}");
        assert_eq!(server.systems.ai.ais.visits(0x250).unwrap(), before + 1);
        assert_eq!(
            server.systems.ai.ais.inspect(0x250, |ai| ai.kind),
            Some(AiKind::Default)
        );
        drop((old, new));
    }

    /// Only the wanted handles are collected, sorted by their monotonic `AddAi` ordinal: a
    /// dormant distant AI is skipped and a woken one keeps its `AddAi` position between the
    /// region-active NPCs (no registry scan).
    #[tokio::test]
    async fn selected_candidates_follow_addai_ordinals() {
        let server = server();
        let first = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let far = spawn_npc(&server, 0x212, 1, 1500.0, 1500.0, AiKind::Default);
        post(&server, 0x212, |c| {
            c.enqueue_path_point(AiPathPoint {
                position: Vec3::new(1500.0, 1500.0, 100.0),
                action: AiPathPointAction::None,
                param: String::new(),
            });
        });
        let second = spawn_npc(&server, 0x211, 1, 112.0, 100.0, AiKind::Default);
        let dormant = spawn_npc(&server, 0x213, 1, 1600.0, 1500.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 115.0, 100.0);

        let ordinal = |id: u32| server.systems.ai.ais.get(id).unwrap().ordinal;
        assert!(ordinal(0x210) < ordinal(0x212));
        assert!(ordinal(0x212) < ordinal(0x211));
        assert!(ordinal(0x211) < ordinal(0x213));

        let (candidates, local) = tick_candidates(&server, INITIALIZING_TICKS + 10);
        let ids: Vec<u32> = candidates.iter().map(|h| h.obj_id).collect();
        assert_eq!(
            ids,
            vec![0x210, 0x212, 0x211],
            "AddAi order of the wanted handles only"
        );
        assert_eq!(local.len(), 2, "only the region-active NPCs are local");
        drop((first, far, second, dormant));
    }

    /// A recycled object id must never let the replaced (stale) handle touch the replacement:
    /// no wake-state clearing, no registry removal; the replacement keeps its claims.
    #[tokio::test]
    async fn stale_handle_cannot_touch_the_replacement() {
        let server = server();
        let old_npc = Entity::new(0x260, Npc::new(1));
        let old = server.systems.ai.ais.add_ai(&old_npc, AiKind::Default);
        let new_npc = spawn_npc(&server, 0x260, 1, 1500.0, 1500.0, AiKind::Default);
        let new = server.systems.ai.ais.get(0x260).unwrap();
        assert!(!Arc::ptr_eq(&old, &new));
        assert!(
            old.ordinal < new.ordinal,
            "replacement gets a later ordinal"
        );
        assert!(
            old.removed.load(Ordering::Acquire),
            "replaced handle is retired"
        );

        // Wake the replacement (persistent AI) and refresh with the stale snapshot: the
        // replacement's wake state must survive.
        post(&server, 0x260, |c| {
            c.enqueue_path_point(AiPathPoint {
                position: Vec3::new(1500.0, 1500.0, 100.0),
                action: AiPathPointAction::None,
                param: String::new(),
            });
        });
        server.systems.ai.ais.refresh_woken(&old);
        let (candidates, _) = tick_candidates(&server, INITIALIZING_TICKS + 10);
        assert_eq!(candidates.len(), 1, "only the replacement is a candidate");
        assert!(Arc::ptr_eq(&candidates[0], &new), "replacement stays woken");

        // A stale removal must not drop the replacement's registry entry.
        assert!(!server.systems.ai.ais.remove_if_current(&old));
        assert!(server
            .systems
            .ai
            .ais
            .get(0x260)
            .is_some_and(|h| Arc::ptr_eq(&h, &new)));

        // The public removal drops the current handle and every claim it holds.
        server.systems.ai.ais.remove(0x260);
        assert!(server.systems.ai.ais.get(0x260).is_none());
        assert!(tick_candidates(&server, INITIALIZING_TICKS + 11)
            .0
            .is_empty());
        drop((old_npc, new_npc));
    }

    /// `NpcTemplate.ActivateAiAlways` keeps the AI ticking with no player in its region.
    #[tokio::test]
    async fn activate_ai_always_ticks_without_players() {
        let mut data = GameData::empty();
        data.systems.npcs.npc.templates.insert(
            5000,
            NpcTemplate {
                id: 5000,
                ai_file_id: 15,
                activate_ai_always: true,
                ..NpcTemplate::default()
            },
        );
        let server = Arc::new(test_server_with(data));
        server.set_self();
        let mut n = Npc::new(5000);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(1500.0, 1500.0, 100.0);
        n.unit.transform.zone_id = TEST_ZONE;
        let npc = Entity::new(0x240, n);
        let world = server.world.world(0).expect("world");
        world.spawn(&npc, &server.view_ctx());
        assert!(crate::systems::ai::hooks::create_ai(&server, &npc, 15));
        assert_eq!(
            server.systems.ai.ais.inspect(0x240, |ai| ai.kind),
            Some(AiKind::HoldPosition)
        );

        idle(&server, INITIALIZING_TICKS as usize + 2);
        let stats = server.systems.ai.ais.tick_stats();
        assert_eq!(stats.candidates, 1, "{stats:?}");
        let before = server.systems.ai.ais.visits(0x240).unwrap();
        idle(&server, 3);
        assert_eq!(server.systems.ai.ais.visits(0x240).unwrap(), before + 3);
        drop(npc);
    }

    /// Default configuration (`World.AiWorkerThreads = 1`): the asynchronous batch and the
    /// synchronous `tick_all` both run the plan as one sequential pass in `AddAi` order.
    #[tokio::test]
    async fn default_worker_batch_ticks_candidates_in_addai_order() {
        let server = server();
        let first = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let far = spawn_npc(&server, 0x212, 1, 1500.0, 1500.0, AiKind::Default);
        post(&server, 0x212, |c| {
            c.enqueue_path_point(AiPathPoint {
                position: Vec3::new(1500.0, 1500.0, 100.0),
                action: AiPathPointAction::None,
                param: String::new(),
            });
        });
        let second = spawn_npc(&server, 0x211, 1, 112.0, 100.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 115.0, 100.0);
        let ordinals: Vec<u64> = [0x210u32, 0x212, 0x211]
            .iter()
            .map(|id| server.systems.ai.ais.get(*id).unwrap().ordinal)
            .collect();

        assert_eq!(
            server.config.world.ai_worker_threads, 1,
            "default is one worker"
        );
        let diag = tick_batch(&server, 100).await;
        assert_eq!(diag.candidates, 3, "{diag:?}");
        assert_eq!(diag.ticked, 3, "{diag:?}");
        assert_eq!(
            *lock(&server.systems.ai.ais.tick_order),
            ordinals,
            "the default worker must keep the AddAi order"
        );

        lock(&server.systems.ai.ais.tick_order).clear();
        let diag = tick_all(&server, 100);
        assert_eq!(diag.candidates, 3, "{diag:?}");
        assert_eq!(
            *lock(&server.systems.ai.ais.tick_order),
            ordinals,
            "tick_all stays sequential"
        );
        drop((first, far, second));
    }

    /// A configured multi-worker batch visits every selected NPC exactly once, leaves the
    /// dormant ones alone and aggregates its chunk diagnostics once.
    #[tokio::test]
    async fn parallel_batch_ticks_each_selected_npc_exactly_once() {
        let server = server();
        let npcs: Vec<NpcRef> = (0..6)
            .map(|i| {
                spawn_npc(
                    &server,
                    0x210 + i,
                    1,
                    104.0 + i as f32,
                    100.0,
                    AiKind::Default,
                )
            })
            .collect();
        let dormant = spawn_npc(&server, 0x230, 1, 1500.0, 1500.0, AiKind::Default);
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let (_client, _player) = spawn_player_at(&server, 0x100, 110.0, 100.0);
        let before: Vec<u64> = npcs
            .iter()
            .map(|n| server.systems.ai.ais.visits(n.obj_id).unwrap())
            .collect();
        let dormant_before = server.systems.ai.ais.visits(0x230).unwrap();

        let diag = tick_batch_with_workers(&server, 100, 4).await;
        assert_eq!(diag.candidates, npcs.len(), "{diag:?}");
        assert_eq!(diag.ticked, npcs.len(), "{diag:?}");
        assert_eq!(diag.busy, 0, "{diag:?}");
        assert_eq!(diag.removed, 0, "{diag:?}");
        assert_eq!(
            server.systems.ai.ais.tick_stats(),
            diag,
            "the chunk diagnostics are aggregated once"
        );
        for (npc, before) in npcs.iter().zip(before) {
            assert_eq!(
                server.systems.ai.ais.visits(npc.obj_id).unwrap(),
                before + 1,
                "npc {:#x} must be visited exactly once",
                npc.obj_id
            );
        }
        assert_eq!(server.systems.ai.ais.visits(0x230).unwrap(), dormant_before);
        drop(dormant);
    }

    /// A multi-worker batch never waits for a busy AI: it counts it (try-lock), ticks the
    /// others, and a later batch runs the queued call once the holder is gone.
    #[tokio::test]
    async fn parallel_batch_skips_a_busy_ai_and_runs_its_queued_call_later() {
        let server = server();
        let held = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let free = spawn_npc(&server, 0x211, 1, 112.0, 100.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 115.0, 100.0);

        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        let srv = server.clone();
        let holder = std::thread::spawn(move || {
            let _ = srv.systems.ai.ais.inspect(0x210, |_| {
                entered_tx.send(()).unwrap();
                release_rx.recv().unwrap();
            });
        });
        entered_rx
            .recv_timeout(Duration::from_secs(5))
            .expect("AI lock not held");

        let ran = Arc::new(AtomicBool::new(false));
        let flag = ran.clone();
        post(&server, 0x210, move |_| flag.store(true, Ordering::SeqCst));
        assert!(!ran.load(Ordering::SeqCst), "the busy AI ran the call");

        let diag = tick_batch_with_workers(&server, 100, 2).await;
        assert_eq!(diag.candidates, 2, "{diag:?}");
        assert_eq!(diag.busy, 1, "{diag:?}");
        assert_eq!(diag.ticked, 1, "{diag:?}");
        assert!(!ran.load(Ordering::SeqCst));

        release_tx.send(()).unwrap();
        holder.join().unwrap();
        let mut fired = false;
        for _ in 0..5 {
            tick_batch_with_workers(&server, 100, 2).await;
            if ran.load(Ordering::SeqCst) {
                fired = true;
                break;
            }
        }
        assert!(fired, "the queued call never ran through a batch");
        drop((held, free));
    }

    /// `try_inspect` reads a free AI and returns `None` for an unknown, replaced or busy one
    /// without ever waiting for the state lock.
    #[tokio::test]
    async fn try_inspect_reads_free_ai_and_skips_missing_replaced_and_busy_ones() {
        let server = server();
        let npc = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        assert_eq!(
            server.systems.ai.ais.try_inspect(0x210, |ai| ai.kind),
            Some(AiKind::Default)
        );
        assert_eq!(
            server.systems.ai.ais.try_inspect(0x2ff, |ai| ai.kind),
            None,
            "an unknown object id is not readable"
        );

        let replaced_npc = Entity::new(0x211, Npc::new(1));
        let replaced = server.systems.ai.ais.add_ai(&replaced_npc, AiKind::Default);
        let replacement = spawn_npc(&server, 0x211, 1, 112.0, 100.0, AiKind::HoldPosition);
        assert!(
            replaced.removed.load(Ordering::Acquire),
            "the replaced handle is retired"
        );
        assert_eq!(
            server.systems.ai.ais.try_inspect(0x211, |ai| ai.kind),
            Some(AiKind::HoldPosition),
            "the current handle is readable"
        );

        let (held_tx, held_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel::<()>();
        let srv = server.clone();
        let holder = std::thread::spawn(move || {
            let _ = srv.systems.ai.ais.inspect(0x210, |_| {
                held_tx.send(()).unwrap();
                release_rx.recv().unwrap();
            });
        });
        held_rx
            .recv_timeout(Duration::from_secs(5))
            .expect("AI lock not held");
        assert_eq!(
            server.systems.ai.ais.try_inspect(0x210, |ai| ai.kind),
            None,
            "a busy AI is skipped instead of waited for"
        );
        release_tx.send(()).unwrap();
        holder.join().unwrap();
        assert_eq!(
            server.systems.ai.ais.try_inspect(0x210, |ai| ai.kind),
            Some(AiKind::Default),
            "readable again after the holder released the lock"
        );
        drop((npc, replacement, replaced, replaced_npc));
    }

    /// A recycled object id is processed once through the current handle in a multi-worker
    /// batch; the replaced snapshot never becomes a candidate.
    #[tokio::test]
    async fn parallel_batch_ticks_a_reused_id_once_and_keeps_the_replacement() {
        let server = server();
        let old_npc = Entity::new(0x250, Npc::new(1));
        let old = server.systems.ai.ais.add_ai(&old_npc, AiKind::Default);
        let new = spawn_npc(&server, 0x250, 1, 1500.0, 1500.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 1510.0, 1500.0);
        let before = server.systems.ai.ais.visits(0x250).unwrap();

        let diag = tick_batch_with_workers(&server, 100, 3).await;
        assert_eq!(diag.candidates, 1, "the stale snapshot is not a candidate");
        assert_eq!(diag.ticked, 1, "{diag:?}");
        assert_eq!(server.systems.ai.ais.visits(0x250).unwrap(), before + 1);
        assert_eq!(
            server.systems.ai.ais.inspect(0x250, |ai| ai.kind),
            Some(AiKind::Default)
        );
        drop((old, old_npc, new));
    }

    /// `World.AiWorkerThreads` is clamped to `1..=MAX_AI_WORKERS`; the default stays `1`.
    #[tokio::test]
    async fn worker_count_is_clamped_between_one_and_the_bound() {
        let server = server();
        for (configured, expected) in [(0u32, 1usize), (1, 1), (3, 3), (4, 4), (5, 4), (64, 4)] {
            server
                .config
                .update(|c| c.world.ai_worker_threads = configured);
            assert_eq!(
                batch_worker_count(&server),
                expected,
                "configured {configured} must clamp to {expected}"
            );
        }
    }

    /// The plan split keeps the `AddAi` order, partitions every candidate exactly once and
    /// never creates more than `MAX_AI_WORKERS` jobs (or none for an empty plan).
    #[test]
    fn plan_chunks_bound_the_jobs_and_keep_the_plan_order() {
        let manager = AiManager::default();
        let npcs: Vec<NpcRef> = (0..10)
            .map(|i| Entity::new(0x300 + i, Npc::new(1)))
            .collect();
        let handles: Vec<Arc<AiHandle>> = npcs
            .iter()
            .map(|n| manager.add_ai(n, AiKind::Default))
            .collect();
        let ordinals: Vec<u64> = handles.iter().map(|h| h.ordinal).collect();

        assert!(
            plan_chunks(&[], 4).is_empty(),
            "an empty plan costs no jobs"
        );
        let single = plan_chunks(&handles, 1);
        assert_eq!(single.len(), 1, "the default is one sequential job");
        assert_eq!(
            single[0].iter().map(|h| h.ordinal).collect::<Vec<_>>(),
            ordinals
        );

        for workers in [2usize, 3, 4, 99] {
            let bound = workers.min(MAX_AI_WORKERS);
            let chunks = plan_chunks(&handles, workers);
            assert!(
                chunks.len() <= bound,
                "at most {bound} jobs for {workers} workers"
            );
            assert!(chunks.iter().all(|c| !c.is_empty()), "no empty jobs");
            let flat: Vec<u64> = chunks.iter().flatten().map(|h| h.ordinal).collect();
            assert_eq!(flat, ordinals, "each candidate exactly once, in plan order");
        }
        drop(npcs);
    }

    /// Two concurrent batches serialize on the manager lock: every selected NPC is visited
    /// once per batch, with no busy-skips or lost visits.
    #[tokio::test]
    async fn concurrent_batches_serialize_instead_of_overlapping() {
        let server = server();
        let npcs: Vec<NpcRef> = (0..8)
            .map(|i| {
                spawn_npc(
                    &server,
                    0x210 + i,
                    1,
                    104.0 + i as f32,
                    100.0,
                    AiKind::Default,
                )
            })
            .collect();
        idle(&server, INITIALIZING_TICKS as usize + 2);
        let (_client, _player) = spawn_player_at(&server, 0x100, 110.0, 100.0);
        let before: Vec<u64> = npcs
            .iter()
            .map(|n| server.systems.ai.ais.visits(n.obj_id).unwrap())
            .collect();

        let (a, b) = tokio::join!(
            tick_batch_with_workers(&server, 100, 4),
            tick_batch_with_workers(&server, 100, 4),
        );
        assert_eq!(a.candidates, npcs.len(), "{a:?}");
        assert_eq!(b.candidates, npcs.len(), "{b:?}");
        assert_eq!(a.busy + b.busy, 0, "{a:?} {b:?}");
        assert_eq!(a.ticked + b.ticked, npcs.len() * 2, "{a:?} {b:?}");
        for (npc, before) in npcs.iter().zip(before) {
            assert_eq!(
                server.systems.ai.ais.visits(npc.obj_id).unwrap(),
                before + 2,
                "npc {:#x} must be visited once per batch",
                npc.obj_id
            );
        }
    }

    /// Cancelling a caller must not release the batch lock while the owned work is still in
    /// flight: the detached supervisor keeps the serialized batch running, and the next batch
    /// stays queued behind it until the work completes. Channels synchronize the steps.
    #[tokio::test]
    async fn cancelled_batch_caller_cannot_unlock_a_running_batch() {
        let server = server();
        let gated = spawn_npc(&server, 0x210, 1, 110.0, 100.0, AiKind::Default);
        let other = spawn_npc(&server, 0x211, 1, 112.0, 100.0, AiKind::Default);
        let (_client, _player) = spawn_player_at(&server, 0x100, 115.0, 100.0);

        // Queue a call on 0x210 while its state is held, then release the holder without
        // draining: the batch picks the call up and blocks inside it.
        let (held_tx, held_rx) = mpsc::channel();
        let (release_holder_tx, release_holder_rx) = mpsc::channel::<()>();
        let srv = server.clone();
        let holder = std::thread::spawn(move || {
            let _ = srv.systems.ai.ais.inspect(0x210, |_| {
                held_tx.send(()).unwrap();
                release_holder_rx.recv().unwrap();
            });
        });
        held_rx
            .recv_timeout(Duration::from_secs(5))
            .expect("AI lock not held");

        let (work_entered_tx, work_entered_rx) = tokio::sync::oneshot::channel::<()>();
        let (work_release_tx, work_release_rx) = mpsc::channel::<()>();
        post(&server, 0x210, move |_| {
            let _ = work_entered_tx.send(());
            work_release_rx.recv().unwrap();
        });
        release_holder_tx.send(()).unwrap();
        holder.join().unwrap();

        // First batch: its chunk ticks 0x210 and blocks in the queued call.
        let first = tokio::spawn({
            let srv = server.clone();
            async move { tick_batch_with_workers(&srv, 100, 2).await }
        });
        work_entered_rx
            .await
            .expect("the first batch must run the queued call");

        // Cancel the first caller: the supervisor must keep running and holding the lock.
        first.abort();
        assert!(first.await.unwrap_err().is_cancelled());
        assert!(
            server.systems.ai.ais.batch_lock.try_lock().is_err(),
            "cancelling the caller released the batch lock while its work was still running"
        );

        // The next batch arrives while the cancelled batch's work is still gated: it must queue
        // on the manager's batch lock, not run beside the in-flight jobs.
        let (b_started_tx, b_started_rx) = tokio::sync::oneshot::channel();
        let (b_done_tx, b_done_rx) = mpsc::channel::<AiTickDiagnostics>();
        let second = tokio::spawn({
            let srv = server.clone();
            async move {
                let _ = b_started_tx.send(());
                let diag = tick_batch_with_workers(&srv, 100, 2).await;
                let _ = b_done_tx.send(diag);
                diag
            }
        });
        b_started_rx.await.expect("the second batch must start");
        for _ in 0..8 {
            tokio::task::yield_now().await;
        }
        assert!(
            b_done_rx.try_recv().is_err(),
            "the second batch completed while the cancelled batch's work was still running"
        );

        // Release the work: the cancelled batch finishes, then the second batch runs with every
        // AI free (no busy skips caused by an overlap).
        work_release_tx.send(()).unwrap();
        let diag = second.await.expect("second batch task must finish");
        assert_eq!(diag.candidates, 2, "{diag:?}");
        assert_eq!(diag.ticked, 2, "{diag:?}");
        assert_eq!(diag.busy, 0, "{diag:?}");

        drop((gated, other));
    }

    /// The claim cleanup verifies every claim against the **current** registry entry, not
    /// against the sweep's registry snapshot: an AI registered while the sweep walks keeps its
    /// initialization grace and `ActivateAiAlways` claim (and ticks without players), while the
    /// late claims of the handle it replaced are still purged.
    #[tokio::test]
    async fn sweep_keeps_claims_of_an_ai_added_after_its_snapshot() {
        let server = server();
        let replaced_npc = Entity::new(0x270, Npc::new(1));
        let replaced = server.systems.ai.ais.add_ai(&replaced_npc, AiKind::Default);

        let srv = server.clone();
        let replaced_in_hook = replaced.clone();
        let mut diag = AiTickDiagnostics::default();
        maintenance_sweep_with(&server, &mut diag, move || {
            // Registered after the sweep's registry snapshot: the current handle of 0x270.
            let fresh = spawn_npc(&srv, 0x270, 1, 1500.0, 1500.0, AiKind::Default);
            srv.systems.ai.ais.mark_always_active(fresh.obj_id);
            // Late claims of the replaced handle (the safety net's race window): one per set.
            let ordinal = replaced_in_hook.ordinal;
            srv.systems
                .ai
                .ais
                .woken
                .insert(ordinal, replaced_in_hook.clone());
            srv.systems
                .ai
                .ais
                .always
                .insert(ordinal, replaced_in_hook.clone());
            srv.systems
                .ai
                .ais
                .initializing
                .insert(ordinal, (0, replaced_in_hook.clone()));
        });

        let manager = &server.systems.ai.ais;
        let current = manager.get(0x270).expect("the new handle is registered");
        assert!(!Arc::ptr_eq(&current, &replaced));
        assert!(replaced.removed.load(Ordering::Acquire));
        assert!(
            manager.initializing.contains_key(&current.ordinal),
            "the new handle lost its initialization grace"
        );
        assert!(
            manager.always.contains_key(&current.ordinal),
            "the new handle lost its ActivateAiAlways claim"
        );
        assert!(
            !manager.woken.contains_key(&replaced.ordinal)
                && !manager.always.contains_key(&replaced.ordinal)
                && !manager.initializing.contains_key(&replaced.ordinal),
            "the replaced handle's late claims must still be purged"
        );

        // No players anywhere: only the surviving always claim can tick the new handle, past the
        // initialization grace too.
        let ticks = INITIALIZING_TICKS as usize + 2;
        idle(&server, ticks);
        assert_eq!(manager.visits(0x270).unwrap(), ticks as u64);
        let stats = manager.tick_stats();
        assert_eq!(stats.candidates, 1, "{stats:?}");
        assert_eq!(stats.ticked, 1, "{stats:?}");
    }
}
