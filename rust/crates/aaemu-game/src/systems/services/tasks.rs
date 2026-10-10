//! `Core/Managers/TaskManager2` (`TaskManager`) and `Models/Tasks/Task`: the queue of timed /
//! repeating / cron tasks checked every 50 ms from the [`TickManager`](super::tick::TickManager).
//!
//! Every task embeds a [`TaskState`] (the C# `Task` base-class members: `Id`, `Name`,
//! `Cancelled`, `ExecuteCount`, `TriggerTime`, `RepeatInterval`, `RepeatCount`,
//! `CronSchedule`). Due tasks run as Tokio tasks (`Task.Run(task.ExecuteAsync)`); a task that
//! is scheduled to run once without delay runs synchronously inside `schedule` (`Execute()`).
//!
//! The phase-3 subsystems mostly use their own Tokio timers; this queue serves the managers
//! ported here (timed rewards, online tracking) and later the GM commands, so
//! `GetQueueCount` (WebApi `/status`) counts only those.

use std::collections::{BTreeMap, HashSet};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use chrono::{NaiveDateTime, Utc};
use futures::future::BoxFuture;

use super::tick::TickEventHandler;
use crate::systems::npcs::schedule::CronSchedule;

/// `DateTime.UtcNow`.
fn utc_now() -> NaiveDateTime {
    Utc::now().naive_utc()
}

/// The `Models/Tasks/Task` base-class state.
#[derive(Debug)]
pub struct TaskState {
    inner: Mutex<TaskStateInner>,
}

#[derive(Debug, Clone)]
pub struct TaskStateInner {
    pub id: u32,
    pub name: String,
    pub cancelled: bool,
    pub execute_count: i32,
    pub trigger_time: NaiveDateTime,
    pub repeat_interval: Duration,
    pub repeat_count: i32,
    pub cron_schedule: Option<CronSchedule>,
}

impl TaskState {
    /// `Task()`: `Name = GetType().Name`.
    pub fn new(name: impl Into<String>) -> Self {
        Self {
            inner: Mutex::new(TaskStateInner {
                id: 0,
                name: name.into(),
                cancelled: false,
                execute_count: 0,
                trigger_time: NaiveDateTime::default(),
                repeat_interval: Duration::ZERO,
                repeat_count: 0,
                cron_schedule: None,
            }),
        }
    }

    /// A copy of the members.
    pub fn snapshot(&self) -> TaskStateInner {
        self.inner.lock().expect("task poisoned").clone()
    }

    pub fn with<R>(&self, f: impl FnOnce(&mut TaskStateInner) -> R) -> R {
        f(&mut self.inner.lock().expect("task poisoned"))
    }

    pub fn id(&self) -> u32 {
        self.with(|s| s.id)
    }

    pub fn cancelled(&self) -> bool {
        self.with(|s| s.cancelled)
    }
}

/// `Models/Tasks/Task`.
pub trait GameTask: Send + Sync + 'static {
    /// The base-class members.
    fn state(&self) -> &TaskState;

    /// `Execute()`.
    fn execute(&self);

    /// `ExecuteAsync()`: by default runs [`GameTask::execute`].
    fn execute_async(self: Arc<Self>) -> BoxFuture<'static, ()> {
        Box::pin(async move { self.execute() })
    }

    /// `OnCancel()`.
    fn on_cancel(&self) {}
}

/// A boxed task as stored in the queue.
pub type TaskRef = Arc<dyn GameTask>;

/// Errors of [`TaskManager::cron_schedule`] (`CrontabException` in C#).
pub type CronError = crate::systems::npcs::schedule::CronError;

#[derive(Default)]
struct TaskIds {
    used: HashSet<u32>,
    index: u32,
}

/// `TaskManager`.
pub struct TaskManager {
    /// `_queue` (task id -> task).
    queue: Mutex<BTreeMap<u32, TaskRef>>,
    ids: Mutex<TaskIds>,
}

impl Default for TaskManager {
    fn default() -> Self {
        Self {
            queue: Mutex::new(BTreeMap::new()),
            ids: Mutex::new(TaskIds {
                used: HashSet::new(),
                index: 1,
            }),
        }
    }
}

impl std::fmt::Debug for TaskManager {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("TaskManager")
            .field("queue", &self.get_queue_count())
            .finish()
    }
}

impl TaskManager {
    /// `Initialize()`: intentionally keeps the queue (tasks queued by `Load()` survive).
    pub fn initialize(&self) {}

    /// `Start()`: `OnTick.Subscribe(Tick, 50 ms, useAsync: true)`.
    pub fn start(self: &Arc<Self>, on_tick: &TickEventHandler) {
        let me = self.clone();
        on_tick.subscribe(move |delta| me.tick(delta), Duration::from_millis(50), true);
    }

    /// `Stop()` (TODO in C#: wait for running tasks).
    pub fn stop(&self) {}

    /// `Tick(delta)`: runs every task whose `TriggerTime` passed and reschedules repeats.
    pub fn tick(&self, _delta: Duration) {
        let now = utc_now();
        let mut to_remove = Vec::new();
        let due: Vec<(u32, TaskRef)> = {
            let q = self.queue.lock().expect("task queue poisoned");
            q.iter().map(|(id, t)| (*id, t.clone())).collect()
        };
        for (id, task) in due {
            let keep = task.state().with(|s| {
                if s.trigger_time >= now {
                    return None;
                }
                s.execute_count += 1;
                if s.repeat_count < 0 || s.execute_count < s.repeat_count {
                    if let Some(cron) = &s.cron_schedule {
                        s.trigger_time = cron.next_occurrence(now);
                    }
                    if s.repeat_interval != Duration::ZERO {
                        s.trigger_time = now + chrono_duration(s.repeat_interval);
                    }
                    return Some(true);
                }
                Some(false)
            });
            let Some(keep) = keep else {
                continue;
            };
            spawn_execution(task);
            if !keep {
                to_remove.push(id);
            }
        }
        for id in to_remove {
            self.queue.lock().expect("task queue poisoned").remove(&id);
            self.release_id(id);
        }
    }

    /// `Schedule(task, startDelay, repeatInterval, count)`: `count` -1 repeats forever, 0 is
    /// the same as 1.
    pub fn schedule(
        &self,
        task: TaskRef,
        start_delay: Option<Duration>,
        repeat_interval: Option<Duration>,
        count: i32,
    ) -> bool {
        let task_id = self.next_id();
        task.state().with(|s| s.id = task_id);

        // Only supposed to run once and immediately: run now without queueing.
        if start_delay == Some(Duration::ZERO) && (0..=1).contains(&count) {
            task.execute();
            self.release_id(task_id);
            return true;
        }

        task.state().with(|s| {
            s.trigger_time = utc_now() + chrono_duration(start_delay.unwrap_or_default());
            match repeat_interval {
                Some(iv) => {
                    s.repeat_interval = iv;
                    s.repeat_count = count;
                }
                None => s.repeat_count = 1,
            }
        });
        self.try_add(task_id, task)
    }

    /// `CronSchedule(task, cronExpression, startDelay, count)`.
    pub fn cron_schedule(
        &self,
        task: TaskRef,
        cron_expression: &str,
        start_delay: Option<Duration>,
        count: i32,
    ) -> Result<bool, CronError> {
        let task_id = self.next_id();
        task.state().with(|s| s.id = task_id);

        if start_delay == Some(Duration::ZERO) {
            task.execute();
            self.release_id(task_id);
            return Ok(true);
        }

        let first_possible = utc_now() + chrono_duration(start_delay.unwrap_or_default());
        // The C# assigns the id before parsing: a parse error leaks it, as here.
        let cron = CronSchedule::parse(cron_expression)?;
        task.state().with(|s| {
            s.trigger_time = cron.next_occurrence(first_possible);
            s.cron_schedule = Some(cron);
            s.repeat_count = count;
        });
        Ok(self.try_add(task_id, task))
    }

    /// `Cancel(task)`: removes it from the queue; `true` (and `Cancelled`) when it was queued.
    /// `Task.Cancel()` additionally runs `OnCancel` on success: use [`TaskManager::cancel_task`].
    pub fn cancel(&self, task: &dyn GameTask) -> bool {
        let id = task.state().id();
        let removed = self
            .queue
            .lock()
            .expect("task queue poisoned")
            .remove(&id)
            .is_some();
        if removed {
            task.state().with(|s| s.cancelled = true);
            self.release_id(id);
        }
        removed
    }

    /// `Task.Cancel()`: [`TaskManager::cancel`] + `OnCancel()`.
    pub fn cancel_task(&self, task: &dyn GameTask) -> bool {
        if self.cancel(task) {
            task.on_cancel();
            return true;
        }
        false
    }

    /// `RemoveTasks(predicate)`.
    pub fn remove_tasks(&self, predicate: impl Fn(&dyn GameTask) -> bool) {
        let snapshot: Vec<(u32, TaskRef)> = self
            .queue
            .lock()
            .expect("task queue poisoned")
            .iter()
            .map(|(k, v)| (*k, v.clone()))
            .collect();
        for (id, task) in snapshot {
            if predicate(task.as_ref()) {
                self.queue.lock().expect("task queue poisoned").remove(&id);
                self.release_id(id);
            }
        }
    }

    /// `GetQueueCount()`.
    pub fn get_queue_count(&self) -> usize {
        self.queue.lock().map(|q| q.len()).unwrap_or(0)
    }

    /// Test helper (the C# tests poke `_queue` through reflection).
    #[cfg(test)]
    fn insert_raw(&self, id: u32, task: TaskRef) {
        self.queue.lock().unwrap().insert(id, task);
    }

    fn try_add(&self, id: u32, task: TaskRef) -> bool {
        let mut q = self.queue.lock().expect("task queue poisoned");
        if q.contains_key(&id) {
            return false;
        }
        q.insert(id, task);
        true
    }

    fn next_id(&self) -> u32 {
        let mut ids = self.ids.lock().expect("task ids poisoned");
        let mut id = ids.index;
        while ids.used.contains(&id) {
            id = if id == u32::MAX { 1 } else { id + 1 };
        }
        ids.used.insert(id);
        ids.index = id.wrapping_add(1);
        if ids.index == 0 {
            ids.index = 1;
        }
        id
    }

    fn release_id(&self, id: u32) {
        self.ids.lock().expect("task ids poisoned").used.remove(&id);
    }
}

fn chrono_duration(d: Duration) -> chrono::Duration {
    chrono::Duration::from_std(d).unwrap_or(chrono::Duration::MAX)
}

/// `System.Threading.Tasks.Task.Run(task.ExecuteAsync)`.
fn spawn_execution(task: TaskRef) {
    match tokio::runtime::Handle::try_current() {
        Ok(h) => {
            h.spawn(task.execute_async());
        }
        Err(_) => {
            // No runtime (plain unit tests): run on a fresh single-threaded one.
            std::thread::spawn(move || {
                if let Ok(rt) = tokio::runtime::Builder::new_current_thread()
                    .enable_all()
                    .build()
                {
                    rt.block_on(task.execute_async());
                }
            });
        }
    }
}

/// A task that runs a closure (`new SomeTask(...)` with an `Execute` body).
pub struct FnTask<F: Fn() + Send + Sync + 'static> {
    state: TaskState,
    f: F,
}

impl<F: Fn() + Send + Sync + 'static> FnTask<F> {
    pub fn new(name: &str, f: F) -> Arc<Self> {
        Arc::new(Self {
            state: TaskState::new(name),
            f,
        })
    }
}

impl<F: Fn() + Send + Sync + 'static> GameTask for FnTask<F> {
    fn state(&self) -> &TaskState {
        &self.state
    }

    fn execute(&self) {
        (self.f)();
    }
}

#[cfg(test)]
mod tests {
    //! Port of `AAEmu.UnitTests/Game/Core/Managers/TaskManagerTests.cs`.
    use std::sync::atomic::{AtomicI32, Ordering};

    use super::*;

    #[derive(Default)]
    struct TestTaskInner {
        calls: AtomicI32,
    }

    struct TestTask {
        state: TaskState,
        inner: Arc<TestTaskInner>,
    }

    impl TestTask {
        fn new() -> Arc<Self> {
            Arc::new(Self {
                state: TaskState::new("TestTask"),
                inner: Arc::default(),
            })
        }

        fn was_executed(&self) -> bool {
            self.inner.calls.load(Ordering::SeqCst) > 0
        }
    }

    impl GameTask for TestTask {
        fn state(&self) -> &TaskState {
            &self.state
        }

        fn execute(&self) {
            self.inner.calls.fetch_add(1, Ordering::SeqCst);
        }
    }

    fn secs(s: u64) -> Option<Duration> {
        Some(Duration::from_secs(s))
    }

    #[test]
    fn start_subscribes_to_tick_manager() {
        let handler = TickEventHandler::default();
        let manager = Arc::new(TaskManager::default());
        manager.start(&handler);
        assert_eq!(handler.subscriber_count(), 1);
        // Start can be called multiple times: each call subscribes again.
        manager.start(&handler);
        manager.start(&handler);
        assert_eq!(handler.subscriber_count(), 3);
    }

    #[test]
    fn schedule_returns_true_when_task_is_queued() {
        let manager = TaskManager::default();
        assert!(manager.schedule(TestTask::new(), secs(60), None, -1));
    }

    #[test]
    fn cancel_returns_false_when_task_not_in_queue() {
        let manager = TaskManager::default();
        assert!(!manager.cancel(TestTask::new().as_ref()));
    }

    #[test]
    fn cron_schedule_variants() {
        let manager = TaskManager::default();
        assert!(manager
            .cron_schedule(TestTask::new(), "* * * * * *", None, -1)
            .unwrap());
        let t = TestTask::new();
        assert!(manager
            .cron_schedule(t.clone(), "*/5 * * * * *", None, -1)
            .unwrap());
        assert!(t.state.snapshot().cron_schedule.is_some());
        assert!(manager
            .cron_schedule(
                TestTask::new(),
                "* * * * * *",
                Some(Duration::from_secs(300)),
                -1
            )
            .unwrap());
        assert!(manager
            .cron_schedule(TestTask::new(), "* * * * * *", None, 5)
            .unwrap());
        assert!(manager
            .cron_schedule(TestTask::new(), "invalid-cron", None, -1)
            .is_err());
    }

    #[test]
    fn cancel_queued_task() {
        let manager = TaskManager::default();
        let task = TestTask::new();
        task.state.with(|s| s.id = 1);
        manager.insert_raw(1, task.clone());
        assert_eq!(manager.get_queue_count(), 1);
        assert!(manager.cancel(task.as_ref()));
        assert!(task.state.cancelled());
        assert_eq!(manager.get_queue_count(), 0);
        // Already cancelled / not queued.
        assert!(!manager.cancel(task.as_ref()));
    }

    #[test]
    fn repeat_settings() {
        let manager = TaskManager::default();
        let t = TestTask::new();
        manager.schedule(t.clone(), secs(10), secs(5), 3);
        let s = t.state.snapshot();
        assert_eq!(s.repeat_interval, Duration::from_secs(5));
        assert_eq!(s.repeat_count, 3);
        let t = TestTask::new();
        manager.schedule(t.clone(), secs(10), None, -1);
        assert_eq!(t.state.snapshot().repeat_count, 1);
        assert!(manager.schedule(TestTask::new(), secs(10), secs(5), -1));
        assert!(manager.schedule(TestTask::new(), secs(10), secs(5), 0));
    }

    #[test]
    fn executes_immediately_without_delay() {
        let manager = TaskManager::default();
        for count in [1, 0] {
            let t = TestTask::new();
            assert!(manager.schedule(t.clone(), Some(Duration::ZERO), None, count));
            assert!(t.was_executed());
        }
        let t = TestTask::new();
        assert!(manager
            .cron_schedule(t.clone(), "* * * * * *", Some(Duration::ZERO), -1)
            .unwrap());
        assert!(t.was_executed());
        assert_eq!(manager.get_queue_count(), 0);
    }

    #[test]
    fn queue_count_ids_and_trigger_time() {
        let manager = TaskManager::default();
        assert_eq!(manager.get_queue_count(), 0);
        let tasks: Vec<_> = (0..3).map(|_| TestTask::new()).collect();
        for t in &tasks {
            manager.schedule(t.clone(), secs(60), None, -1);
        }
        assert_eq!(manager.get_queue_count(), 3);
        let ids: HashSet<u32> = tasks.iter().map(|t| t.state.id()).collect();
        assert_eq!(ids.len(), 3);
        // Initialize keeps the queue.
        manager.initialize();
        assert_eq!(manager.get_queue_count(), 3);
        manager.remove_tasks(|t| t.state().id() > 1);
        assert_eq!(manager.get_queue_count(), 1);
        manager.remove_tasks(|_| true);
        assert_eq!(manager.get_queue_count(), 0);

        let t = TestTask::new();
        let delay = Duration::from_secs(24 * 3600);
        manager.schedule(t.clone(), Some(delay), Some(Duration::from_secs(3600)), 10);
        let expected = utc_now() + chrono_duration(delay);
        let diff = (t.state.snapshot().trigger_time - expected)
            .num_milliseconds()
            .abs();
        assert!(diff < 1000);
    }

    #[tokio::test]
    async fn tick_runs_due_tasks_and_repeats() {
        let manager = TaskManager::default();
        let once = TestTask::new();
        let repeat = TestTask::new();
        let later = TestTask::new();
        // A negative-less "already due" task: no delay given -> TriggerTime = now.
        manager.schedule(once.clone(), None, None, -1);
        manager.schedule(repeat.clone(), None, secs(60), 2);
        manager.schedule(later.clone(), secs(60), None, -1);
        tokio::time::sleep(Duration::from_millis(5)).await;
        manager.tick(Duration::ZERO);
        tokio::time::sleep(Duration::from_millis(20)).await;
        assert!(once.was_executed());
        assert!(repeat.was_executed());
        assert!(!later.was_executed());
        // `once` is gone, `repeat` waits for its second run, `later` waits.
        assert_eq!(manager.get_queue_count(), 2);
        assert_eq!(repeat.state.snapshot().execute_count, 1);
        // Released ids are reused.
        assert!(!manager.ids.lock().unwrap().used.contains(&once.state.id()));
    }
}
