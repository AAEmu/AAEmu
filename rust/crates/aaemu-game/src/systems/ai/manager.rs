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
use std::sync::{Arc, Mutex, MutexGuard};
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

impl AiHandle {
    /// Runs the queued calls while holding the state (the holder's duty).
    fn drain_locked(&self, server: &GameServer, st: &mut NpcAi) {
        loop {
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
                let Ok(mut st) = self.state.try_lock() else {
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
            let mut st = lock(&h.state);
            st.owner = None;
            st.should_tick = false;
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
            let mut st = lock(&h.state);
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
