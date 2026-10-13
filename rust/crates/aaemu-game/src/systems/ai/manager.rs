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

use std::collections::{HashSet, VecDeque};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, TryLockError};
use std::time::{Duration, Instant};

use dashmap::DashMap;

use crate::server::GameServer;
use crate::world::{NpcRef, WorldObject};

use super::framework::{AiCtx, AiKind, NpcAi};

/// A call queued for an AI.
pub type AiFn = Box<dyn FnOnce(&mut AiCtx<'_>) + Send>;

/// One NPC's AI.
pub struct AiHandle {
    pub obj_id: u32,
    state: Mutex<NpcAi>,
    pending: Mutex<VecDeque<AiFn>>,
    removed: AtomicBool,
}

impl std::fmt::Debug for AiHandle {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AiHandle")
            .field("obj_id", &self.obj_id)
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

/// `AIManager`.
#[derive(Debug, Default)]
pub struct AiManager {
    ais: DashMap<u32, Arc<AiHandle>>,
    /// `_npcAis` order.
    order: Mutex<Vec<u32>>,
    started: AtomicBool,
}

impl AiManager {
    /// `AIManager.AddAi(ai)` (+ `npc.Ai = ai`).
    pub fn add_ai(&self, npc: &NpcRef, kind: AiKind) -> Arc<AiHandle> {
        let h = Arc::new(AiHandle {
            obj_id: npc.obj_id,
            state: Mutex::new(NpcAi::new(kind, npc)),
            pending: Mutex::new(VecDeque::new()),
            removed: AtomicBool::new(false),
        });
        self.ais.insert(npc.obj_id, h.clone());
        lock(&self.order).push(npc.obj_id);
        npc.data().has_ai = true;
        h
    }

    /// The AI of an NPC.
    pub fn get(&self, obj_id: u32) -> Option<Arc<AiHandle>> {
        self.ais.get(&obj_id).map(|h| h.clone())
    }

    /// `Npc.Delete()`: `Ai.Owner = null; Ai = null` (the AI stops ticking and is forgotten).
    pub fn remove(&self, obj_id: u32) {
        if let Some((_, h)) = self.ais.remove(&obj_id) {
            // A skill can delete its own caster while its AI holds this mutex.
            // Despawn callers may also hold the spawn lock needed by an AI skill.
            // Mark removal immediately; a busy holder detaches when it drains.
            h.removed.store(true, Ordering::Release);
            lock(&h.pending).clear();
            if let Some(mut st) = try_lock(&h.state) {
                h.detach_locked(&mut st);
            }
        }
        lock(&self.order).retain(|id| *id != obj_id);
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
    Some(res)
}

/// The regions with player activity (`Region.HasPlayerActivity()`: a player in the region's
/// neighbourhood) of every instance.
fn active_regions(server: &GameServer) -> HashSet<(u32, i32, i32)> {
    let mut set = HashSet::new();
    for world in server.world.worlds() {
        for p in world.all_characters() {
            let Some((inst, x, y)) = p.region() else {
                continue;
            };
            for (nx, ny) in world.neighbors((x, y)) {
                set.insert((inst, nx, ny));
            }
        }
    }
    set
}

/// `AIManager.Tick(delta)`: every AI with an owner ticks (`NpcAi.Tick`), in `AddAi` order.
pub fn tick_all(server: &GameServer, delta_ms: i64) {
    let manager = &server.systems.ai.ais;
    let ids: Vec<u32> = lock(&manager.order).clone();
    let active = active_regions(server);
    for id in ids {
        let Some(h) = manager.get(id) else { continue };
        let mut gone = false;
        {
            // External skill/aggro calls also run under this lock. Leave a busy
            // NPC for the next tick instead of stopping every other NPC's AI.
            let Some(mut st) = try_lock(&h.state) else {
                continue;
            };
            if h.removed.load(Ordering::Acquire) {
                h.detach_locked(&mut st);
                continue;
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
                        let activity = region.is_some_and(|r| active.contains(&r));
                        let mut ctx = AiCtx {
                            server,
                            npc,
                            obj,
                            ai: &mut st,
                        };
                        if catch_unwind(AssertUnwindSafe(|| ctx.tick(delta_ms, activity))).is_err()
                        {
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
            manager.remove(id);
        } else {
            h.try_drain(server);
        }
    }
}

/// `AIManager.Initialize()`: the 100 ms tick (one tick at a time, measured delta), on a
/// blocking thread so long ticks do not stall the runtime.
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
            let res = tokio::task::spawn_blocking(move || tick_all(&server, delta_ms)).await;
            if res.is_err() {
                tracing::error!("AI tick task failed");
            }
            let took = started.elapsed();
            if took > Duration::from_millis(100) {
                tracing::warn!("AI tick took {}ms to finish", took.as_millis());
            }
        }
    });
}
