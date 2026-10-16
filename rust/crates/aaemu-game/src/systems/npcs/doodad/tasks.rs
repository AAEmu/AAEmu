//! `Models/Tasks/Doodads/*`: `DoodadFuncTimerTask`, `DoodadFuncFinalTask`,
//! `DoodadFuncGrowthTask`, `DoodadFuncTodTask`, `DoodadFuncCloutTask`, `DoodadDeleteTask`.
//!
//! `Doodad.FuncTask` is the generation number `doodad.phase.func_task` (0 = null). Starting a
//! func task stores a fresh generation; `FuncTask.Cancel()` + `FuncTask = null` set it to 0. A
//! scheduled func task only executes while its generation is still the doodad's current one
//! (`TaskManager.Cancel` semantics). Tasks scheduled outside `FuncTask` (`DoodadDeleteTask`, the
//! second stage of `DoodadFuncFinalTask`) carry generation 0 and always run.
//!
//! Scheduling: a tokio task (`server.self_arc()`) sleeping for the delay. Without a runtime or a
//! shared server handle (unit tests), or when [`TaskScheduler::set_manual`] is on, tasks are
//! queued and run by [`TaskScheduler::advance`] (virtual clock).

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use super::phase;
use super::runtime::DoodadCtx;
use super::spawner::DoodadSpawner;
use crate::server::GameServer;
use crate::systems::npcs::area_trigger::{self, AreaTrigger};
use crate::world::{DoodadRef, WorldInstance, WorldObject};

/// What a scheduled doodad task does when it fires.
#[derive(Debug, Clone)]
pub enum FuncTaskKind {
    /// `DoodadFuncTimerTask(caster, owner, 0, nextPhase)`.
    Timer { next_phase: i32 },
    /// `DoodadFuncGrowthTask(caster, owner, 0, nextPhase, endScale)`.
    Growth { next_phase: i32, end_scale: f32 },
    /// `DoodadFuncCloutTask(caster, owner, 0, nextPhase, areaTrigger)`.
    Clout {
        next_phase: i32,
        trigger: Arc<AreaTrigger>,
    },
    /// `DoodadFuncFinalTask(caster, owner, 0, respawn, delay)`; `respawn_stage` is the
    /// rescheduled second run (`_respawnTime != null`) with the spawner captured by the first.
    Final {
        respawn: bool,
        delay: i32,
        respawn_stage: Option<Arc<DoodadSpawner>>,
    },
    /// `DoodadFuncTodTask(caster, owner, 0, nextPhase)`.
    Tod { next_phase: i32 },
    /// `DoodadDeleteTask(owner)`.
    Delete,
    /// A family/link signal deferred while the target is processing an interaction.
    Pulse { source: DoodadRef, value: bool },
}

/// One scheduled doodad task.
#[derive(Debug, Clone)]
pub struct DoodadTask {
    /// The original instance, so callbacks cannot act on a recycled instance ID.
    pub world: Option<Weak<WorldInstance>>,
    /// The task's `owner` (the doodad it acts on).
    pub doodad: DoodadRef,
    /// The doodad whose `FuncTask` this is (normally the owner).
    pub holder: DoodadRef,
    pub caster: Option<WorldObject>,
    /// `FuncTask` generation (0 = not a `FuncTask`, always runs).
    pub generation: u64,
    pub kind: FuncTaskKind,
}

static NEXT_GENERATION: AtomicU64 = AtomicU64::new(1);

/// The scheduler of `DoodadManager` (`server.systems.npcs.doodads.tasks`).
#[derive(Debug, Default)]
pub struct TaskScheduler {
    manual: Mutex<Option<ManualQueue>>,
    /// The pending `FuncTask` objects by generation (what `doodad.FuncTask` references; used by
    /// [`execute_func_task_now`]).
    func_tasks: Mutex<std::collections::HashMap<u64, DoodadTask>>,
}

#[derive(Debug, Default)]
struct ManualQueue {
    now_ms: u64,
    seq: u64,
    pending: Vec<(u64, u64, DoodadTask)>,
}

impl TaskScheduler {
    /// Manual mode: tasks are queued on a virtual clock and run by [`Self::advance`].
    pub fn set_manual(&self, on: bool) {
        *self.manual.lock().expect("tasks poisoned") = on.then(ManualQueue::default);
    }

    /// Number of queued (manual mode) tasks.
    pub fn pending(&self) -> usize {
        self.manual
            .lock()
            .expect("tasks poisoned")
            .as_ref()
            .map_or(0, |q| q.pending.len())
    }

    /// Delays (ms from the virtual now) of the queued tasks, in due order.
    pub fn pending_delays(&self) -> Vec<u64> {
        let g = self.manual.lock().expect("tasks poisoned");
        let Some(q) = g.as_ref() else {
            return Vec::new();
        };
        let mut v: Vec<_> = q.pending.iter().map(|p| (p.0, p.1)).collect();
        v.sort_unstable();
        v.into_iter().map(|(due, _)| due - q.now_ms).collect()
    }

    /// Advances the virtual clock by `ms`, running every task that becomes due (in due order,
    /// tasks scheduled while running included). Returns the number of tasks executed.
    pub fn advance(&self, server: &GameServer, ms: u64) -> usize {
        let target = {
            let g = self.manual.lock().expect("tasks poisoned");
            match g.as_ref() {
                Some(q) => q.now_ms + ms,
                None => return 0,
            }
        };
        let mut n = 0;
        loop {
            let next = {
                let mut g = self.manual.lock().expect("tasks poisoned");
                let Some(q) = g.as_mut() else {
                    return n;
                };
                let best = q
                    .pending
                    .iter()
                    .enumerate()
                    .filter(|(_, p)| p.0 <= target)
                    .min_by_key(|(_, p)| (p.0, p.1))
                    .map(|(i, _)| i);
                match best {
                    Some(i) => {
                        let (due, _, task) = q.pending.remove(i);
                        q.now_ms = q.now_ms.max(due);
                        Some(task)
                    }
                    None => {
                        q.now_ms = target;
                        None
                    }
                }
            };
            match next {
                Some(task) => {
                    execute(server, task);
                    n += 1;
                }
                None => return n,
            }
        }
    }

    /// Queues `task` in manual mode; gives it back otherwise.
    fn try_queue(&self, task: DoodadTask, delay: Duration) -> Option<DoodadTask> {
        let mut g = self.manual.lock().expect("tasks poisoned");
        match g.as_mut() {
            Some(q) => {
                q.seq += 1;
                let due = q.now_ms + delay.as_millis() as u64;
                let seq = q.seq;
                q.pending.push((due, seq, task));
                None
            }
            None => Some(task),
        }
    }
}

/// `TimeSpan.FromMilliseconds(ms)` (negative / NaN = 0).
pub fn ms(ms: f64) -> Duration {
    if ms.is_finite() && ms > 0.0 {
        Duration::from_secs_f64(ms / 1000.0)
    } else {
        Duration::ZERO
    }
}

/// `TaskManager.Instance.Schedule(task, delay)`.
pub fn schedule(server: &GameServer, task: DoodadTask, delay: Duration) {
    let Some(task) = server.systems.npcs.doodads.tasks.try_queue(task, delay) else {
        return;
    };
    match (server.self_arc(), tokio::runtime::Handle::try_current()) {
        (Some(server), Ok(rt)) => {
            rt.spawn(async move {
                tokio::time::sleep(delay).await;
                execute(&server, task);
            });
        }
        _ => {
            // No shared server / runtime (unit tests without manual mode): queue manually so
            // the task is not lost; it runs on the next `advance`.
            let tasks = &server.systems.npcs.doodads.tasks;
            tasks.set_manual_if_off();
            let _ = tasks.try_queue(task, delay);
        }
    }
}

impl TaskScheduler {
    fn set_manual_if_off(&self) {
        let mut g = self.manual.lock().expect("tasks poisoned");
        if g.is_none() {
            *g = Some(ManualQueue::default());
        }
    }
}

/// `owner.FuncTask?.Cancel(); owner.FuncTask = new <task>; Schedule(owner.FuncTask, delay)`.
/// Returns the new generation.
pub fn start_func_task(
    server: &GameServer,
    doodad: &DoodadRef,
    caster: Option<&WorldObject>,
    kind: FuncTaskKind,
    delay: Duration,
) -> u64 {
    start_func_task_for(server, doodad, doodad, caster, kind, delay)
}

/// [`start_func_task`] for a task stored as `holder.FuncTask` that acts on `owner`.
pub fn start_func_task_for(
    server: &GameServer,
    holder: &DoodadRef,
    owner: &DoodadRef,
    caster: Option<&WorldObject>,
    kind: FuncTaskKind,
    delay: Duration,
) -> u64 {
    let generation = NEXT_GENERATION.fetch_add(1, Ordering::Relaxed);
    let old = std::mem::replace(&mut holder.data().phase.func_task, generation);
    let task = DoodadTask {
        world: server
            .world
            .world_of(&WorldObject::from(owner))
            .as_ref()
            .map(Arc::downgrade),
        doodad: owner.clone(),
        holder: holder.clone(),
        caster: caster.cloned(),
        generation,
        kind,
    };
    {
        let mut current = server
            .systems
            .npcs
            .doodads
            .tasks
            .func_tasks
            .lock()
            .expect("tasks poisoned");
        current.remove(&old);
        current.insert(generation, task.clone());
    }
    schedule(server, task, delay);
    generation
}

/// `doodad.FuncTask.Cancel(); Task.Run(doodad.FuncTask.ExecuteAsync)` (`/tickdoodad`): runs the
/// doodad's pending func task now (on the task runtime, as `Task.Run`) and cancels the scheduled
/// run. `false` when the doodad has no `FuncTask`.
pub fn execute_func_task_now(server: &GameServer, doodad: &DoodadRef) -> bool {
    let generation = doodad.data().phase.func_task;
    if generation == 0 {
        return false;
    }
    let tasks = &server.systems.npcs.doodads.tasks;
    let Some(mut task) = tasks
        .func_tasks
        .lock()
        .expect("tasks poisoned")
        .remove(&generation)
    else {
        return false;
    };
    // Cancel(): the scheduled run no longer matches the doodad's FuncTask generation; the task
    // object itself (still `FuncTask`) runs under a fresh generation.
    let fresh = NEXT_GENERATION.fetch_add(1, Ordering::Relaxed);
    {
        let mut d = task.holder.data();
        if d.phase.func_task == generation {
            d.phase.func_task = fresh;
        }
    }
    task.generation = fresh;
    tasks
        .func_tasks
        .lock()
        .expect("tasks poisoned")
        .insert(fresh, task.clone());
    schedule(server, task, Duration::ZERO);
    true
}

/// `FuncTask.Cancel(); FuncTask = null`. Returns whether a task was set.
pub fn cancel_func_task(doodad: &DoodadRef) -> bool {
    let mut d = doodad.data();
    let had = d.phase.func_task != 0;
    d.phase.func_task = 0;
    had
}

/// `TaskManager.Schedule(new DoodadDeleteTask(doodad))` (no delay).
pub fn schedule_delete(server: &GameServer, doodad: &DoodadRef) {
    schedule(
        server,
        DoodadTask {
            world: server
                .world
                .world_of(&WorldObject::from(doodad))
                .as_ref()
                .map(Arc::downgrade),
            doodad: doodad.clone(),
            holder: doodad.clone(),
            caster: None,
            generation: 0,
            kind: FuncTaskKind::Delete,
        },
        Duration::ZERO,
    );
}

/// `Task.Execute()` of a doodad task (skipped when its `FuncTask` was cancelled / replaced).
pub fn execute(server: &GameServer, task: DoodadTask) {
    if task.generation != 0 && task.holder.data().phase.func_task != task.generation {
        return;
    }
    let Some(world) = task.world.as_ref().and_then(Weak::upgrade) else {
        return;
    };
    if !server
        .world
        .world(world.id)
        .is_some_and(|current| Arc::ptr_eq(&current, &world))
    {
        return;
    }
    let respawning = matches!(
        task.kind,
        FuncTaskKind::Final {
            respawn_stage: Some(_),
            ..
        }
    );
    if !respawning
        && (task.doodad.data().phase.deleted
            || !world
                .doodad(task.doodad.obj_id)
                .is_some_and(|current| Arc::ptr_eq(&current, &task.doodad)))
    {
        return;
    }
    let _operation = if respawning {
        None
    } else {
        match phase::Operation::enter(&task.doodad) {
            Some(guard) => Some(guard),
            None => {
                schedule(server, task, Duration::from_millis(10));
                return;
            }
        }
    };
    if task.generation != 0 {
        server
            .systems
            .npcs
            .doodads
            .tasks
            .func_tasks
            .lock()
            .expect("tasks poisoned")
            .remove(&task.generation);
    }
    if task.generation != 0 && task.holder.data().phase.func_task != task.generation {
        return; // cancelled
    }
    let ctx = DoodadCtx::new(server, world, task.doodad.clone());
    let caster = task.caster.as_ref();
    let (template_id, func_group_id) = {
        let d = ctx.doodad.data();
        (d.template_id, d.func_group_id)
    };
    match task.kind {
        FuncTaskKind::Timer { next_phase } => {
            tracing::trace!(
                "[Doodad] DoodadFuncTimerTask: Doodad {}, TemplateId {template_id}. Using skill 0 with doodad phase {next_phase}",
                ctx.doodad.obj_id
            );
            ctx.doodad.data().phase.func_task = 0;
            phase::do_change_phase(&ctx, caster, next_phase);
            // The phase state does not allow us to interact with the object, so we will
            // automatically get items from ID=6121 & ID=6125, "Treasure Chest" in Palace
            // Cellar Dungeon.
            let funcs = ctx.data().funcs_for_group(next_phase as u32);
            for f in funcs.iter().filter(|f| {
                f.func_type == "DoodadFuncLootItem" || f.func_type == "DoodadFuncLootPack"
            }) {
                if !phase::is_group_kind_start(&ctx, next_phase as u32) {
                    phase::do_func(&ctx, caster, 0, Some(f));
                }
            }
        }
        FuncTaskKind::Growth {
            next_phase,
            end_scale,
        } => {
            tracing::trace!(
                "[Doodad] DoodadFuncGrowthTask: Doodad {}, TemplateId {template_id}. Using skill 0 with doodad phase {func_group_id}",
                ctx.doodad.obj_id
            );
            // C# `_owner.Scale = endScale` assigns BaseUnit.Scale, which Doodad.Scale (get-only
            // override returning `_scale`) never reads: the visible scale does not change.
            let _ = end_scale;
            ctx.doodad.data().phase.func_task = 0;
            phase::do_change_phase(&ctx, caster, next_phase);
        }
        FuncTaskKind::Clout {
            next_phase,
            trigger,
        } => {
            tracing::trace!(
                "[Doodad] DoodadFuncCloutTask: Doodad {}, TemplateId {template_id}. Using skill 0 with doodad phase {next_phase}",
                ctx.doodad.obj_id
            );
            ctx.doodad.data().phase.func_task = 0;
            if next_phase == -1 {
                super::manager::delete(server, &ctx.world, &ctx.doodad);
            }
            area_trigger::remove_area_trigger(server, &trigger);
            phase::do_change_phase(&ctx, caster, next_phase);
        }
        FuncTaskKind::Final {
            respawn,
            delay,
            respawn_stage,
        } => {
            tracing::trace!(
                "[Doodad] DoodadFuncFinalTask: Doodad {}, TemplateId {template_id}. Using skill 0 with doodad phase {func_group_id}",
                ctx.doodad.obj_id
            );
            let spawner = respawn_stage.clone().or_else(|| {
                server
                    .systems
                    .npcs
                    .spawns
                    .doodad_spawner_of(ctx.doodad.obj_id)
            });
            match spawner {
                Some(spawner) if respawn => {
                    if respawn_stage.is_none() && ctx.doodad.data().phase.func_task != 0 {
                        // DoodadSpawner.Despawn() clears FuncTask; reschedule this task for the
                        // second stage.
                        ctx.doodad.data().phase.func_task = 0;
                        spawner.despawn(server, &ctx.doodad);
                        schedule(
                            server,
                            DoodadTask {
                                world: Some(Arc::downgrade(&ctx.world)),
                                doodad: ctx.doodad.clone(),
                                holder: ctx.doodad.clone(),
                                caster: task.caster.clone(),
                                generation: 0,
                                kind: FuncTaskKind::Final {
                                    respawn,
                                    delay,
                                    respawn_stage: Some(spawner),
                                },
                            },
                            ms(f64::from(delay)),
                        );
                        return;
                    }
                    // C#: `Spawner.Position.WorldId = world?.Id ?? DefaultInstanceId` — the
                    // Rust spawner spawns into its parent world.
                    spawner.spawn(server, 0);
                }
                spawner => {
                    ctx.doodad.data().phase.func_task = 0;
                    match spawner {
                        Some(s) => s.despawn(server, &ctx.doodad),
                        None => super::manager::delete(server, &ctx.world, &ctx.doodad),
                    }
                }
            }
        }
        FuncTaskKind::Tod { next_phase } => {
            tracing::trace!(
                "[Doodad] DoodadFuncTodTask: Doodad {}, TemplateId {template_id}. Using skill 0 with doodad phase {func_group_id}",
                ctx.doodad.obj_id
            );
            if cancel_func_task(&ctx.doodad) {
                tracing::trace!("DoodadFuncTodTask: The current timer has been ended.");
            }
            phase::do_change_phase(&ctx, caster, next_phase);
        }
        FuncTaskKind::Delete => super::manager::delete(server, &ctx.world, &ctx.doodad),
        FuncTaskKind::Pulse { source, value } => {
            let source_ctx = DoodadCtx::new(server, ctx.world.clone(), source);
            super::logic::receive(&source_ctx, &ctx.doodad, caster, value);
        }
    }
}

pub(crate) fn schedule_pulse(
    ctx: &DoodadCtx<'_>,
    target: &DoodadRef,
    caster: Option<&WorldObject>,
    value: bool,
) {
    schedule(
        ctx.server,
        DoodadTask {
            world: Some(Arc::downgrade(&ctx.world)),
            doodad: target.clone(),
            holder: target.clone(),
            caster: caster.cloned(),
            generation: 0,
            kind: FuncTaskKind::Pulse {
                source: ctx.doodad.clone(),
                value,
            },
        },
        Duration::from_millis(10),
    );
}
