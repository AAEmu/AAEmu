//! `Core/Managers/TickManager` (+ `TickEventHandler`, `TickEventEntity`): one loop thread that
//! invokes every subscriber whose tick rate elapsed, then sleeps 20 ms.
//!
//! Subscribers are plain closures (`OnTickEvent(TimeSpan delta)`). Synchronous subscribers run
//! on the tick thread; asynchronous ones (`useAsync`) run on a blocking worker (`Task.Run`) and
//! are skipped while their previous run is still busy. A panicking subscriber is logged like the
//! C# `catch (Exception e)`.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// `OnTickEvent(TimeSpan delta)`.
pub type OnTickEvent = Arc<dyn Fn(Duration) + Send + Sync>;

/// A subscription (`TickEventHandler.UnSubscribe(tickEvent)` takes the delegate in C#; Rust
/// closures have no identity, so `subscribe` returns this id).
pub type SubscriptionId = u64;

/// `TickEventEntity`.
struct TickEventEntity {
    id: SubscriptionId,
    event: OnTickEvent,
    /// `LastExecution` (`None` = `default`, never ran).
    last_execution: Option<Duration>,
    tick_rate: Duration,
    /// `ActiveTask` is running (async subscribers).
    active: Option<Arc<AtomicBool>>,
    use_async: bool,
}

#[derive(Default)]
struct Queues {
    to_add: Vec<TickEventEntity>,
    to_remove: Vec<SubscriptionId>,
}

/// `TickManager.TickEventHandler`.
pub struct TickEventHandler {
    events: Mutex<Vec<TickEventEntity>>,
    queues: Mutex<Queues>,
    sw: Instant,
    next_id: AtomicU64,
}

impl Default for TickEventHandler {
    fn default() -> Self {
        Self {
            events: Mutex::new(Vec::new()),
            queues: Mutex::new(Queues::default()),
            sw: Instant::now(),
            next_id: AtomicU64::new(1),
        }
    }
}

impl std::fmt::Debug for TickEventHandler {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("TickEventHandler")
            .field("subscribers", &self.subscriber_count())
            .finish()
    }
}

impl TickEventHandler {
    /// `Invoke()`: applies queued (un)subscriptions, then runs every due subscriber.
    pub fn invoke(&self) {
        let mut events = self.events.lock().expect("tick poisoned");
        {
            let mut q = self.queues.lock().expect("tick poisoned");
            events.append(&mut q.to_add);
            for id in q.to_remove.drain(..) {
                if let Some(i) = events.iter().position(|e| e.id == id) {
                    events.remove(i);
                }
            }
        }
        for ev in events.iter_mut() {
            let elapsed = self.sw.elapsed();
            let delta = match ev.last_execution {
                Some(last) => elapsed.saturating_sub(last),
                None => ev.tick_rate + Duration::from_millis(1),
            };
            if delta <= ev.tick_rate {
                continue;
            }
            if ev.use_async {
                if ev
                    .active
                    .as_ref()
                    .is_some_and(|busy| busy.load(Ordering::Acquire))
                {
                    continue;
                }
                ev.last_execution = Some(elapsed);
                let busy = Arc::new(AtomicBool::new(true));
                ev.active = Some(busy.clone());
                let event = ev.event.clone();
                run_async(move || {
                    run_logged(&event, delta);
                    busy.store(false, Ordering::Release);
                });
            } else {
                ev.last_execution = Some(elapsed);
                run_logged(&ev.event, delta);
            }
        }
    }

    /// `Subscribe(tickEvent, tickRate, useAsync)`.
    pub fn subscribe(
        &self,
        event: impl Fn(Duration) + Send + Sync + 'static,
        tick_rate: Duration,
        use_async: bool,
    ) -> SubscriptionId {
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        self.queues
            .lock()
            .expect("tick poisoned")
            .to_add
            .push(TickEventEntity {
                id,
                event: Arc::new(event),
                last_execution: None,
                tick_rate,
                active: None,
                use_async,
            });
        id
    }

    /// `UnSubscribe(tickEvent)`.
    pub fn unsubscribe(&self, id: SubscriptionId) {
        self.queues
            .lock()
            .expect("tick poisoned")
            .to_remove
            .push(id);
    }

    /// Active + queued subscriptions.
    pub fn subscriber_count(&self) -> usize {
        let active = self.events.lock().map(|e| e.len()).unwrap_or(0);
        let queued = self.queues.lock().map(|q| q.to_add.len()).unwrap_or(0);
        active + queued
    }
}

/// `Task.Run`: on a blocking worker of the current Tokio runtime when there is one, else on a
/// plain thread.
fn run_async(f: impl FnOnce() + Send + 'static) {
    match tokio::runtime::Handle::try_current() {
        Ok(h) => {
            h.spawn_blocking(f);
        }
        Err(_) => {
            std::thread::spawn(f);
        }
    }
}

/// `try { ev.Event(delta); } catch (Exception e) { Logger.Error(...) }`.
fn run_logged(event: &OnTickEvent, delta: Duration) {
    if let Err(e) = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| event(delta))) {
        let msg = e
            .downcast_ref::<String>()
            .cloned()
            .or_else(|| e.downcast_ref::<&str>().map(|s| (*s).to_string()))
            .unwrap_or_else(|| "panic".into());
        tracing::error!("{msg}");
    }
}

/// `TickManager`.
#[derive(Debug, Default)]
pub struct TickManager {
    /// `OnTick`.
    pub on_tick: Arc<TickEventHandler>,
    running: Arc<AtomicBool>,
}

impl TickManager {
    /// `Initialize()`: starts the tick thread (`TickLoop`: invoke, warn when a tick took more
    /// than 100 ms, sleep 20 ms). Tokio-aware subscribers run inside the caller's runtime.
    pub fn initialize(&self) {
        if self.running.swap(true, Ordering::AcqRel) {
            return;
        }
        let handler = self.on_tick.clone();
        let running = self.running.clone();
        let rt = tokio::runtime::Handle::try_current().ok();
        let spawned = std::thread::Builder::new()
            .name("TickManager".into())
            .spawn(move || {
                let _guard = rt.as_ref().map(tokio::runtime::Handle::enter);
                while running.load(Ordering::Acquire) {
                    let before = Instant::now();
                    handler.invoke();
                    let time = before.elapsed();
                    if time > Duration::from_millis(100) {
                        tracing::warn!("Tick took {}ms to finish", time.as_secs_f64() * 1000.0);
                    }
                    std::thread::sleep(Duration::from_millis(20));
                }
            });
        if let Err(e) = spawned {
            tracing::error!("TickManager thread: {e}");
            self.running.store(false, Ordering::Release);
        }
    }

    /// `Stop()`.
    pub fn stop(&self) {
        self.running.store(false, Ordering::Release);
    }

    pub fn is_running(&self) -> bool {
        self.running.load(Ordering::Acquire)
    }
}

#[cfg(test)]
mod tests {
    //! Port of `AAEmu.UnitTests/Game/Core/Managers/TickManagerTests.cs`.
    use std::sync::atomic::AtomicUsize;
    use std::sync::mpsc;

    use super::*;

    const ONE_MS_TICK: Duration = Duration::from_millis(1);

    /// A one-shot gate (`ManualResetEventSlim`).
    #[derive(Clone, Default)]
    struct Gate(Arc<(Mutex<bool>, std::sync::Condvar)>);

    impl Gate {
        fn set(&self) {
            *self.0 .0.lock().unwrap() = true;
            self.0 .1.notify_all();
        }

        fn wait(&self, timeout: Duration) -> bool {
            let (m, c) = &*self.0;
            let g = m.lock().unwrap();
            let (g, _) = c.wait_timeout_while(g, timeout, |set| !*set).unwrap();
            *g
        }
    }

    #[test]
    fn invoke_when_slow_async_subscriber_returns_without_waiting() {
        let handler = TickEventHandler::default();
        let (started, release) = (Gate::default(), Gate::default());
        let (s, r) = (started.clone(), release.clone());
        handler.subscribe(
            move |_| {
                s.set();
                r.wait(Duration::from_secs(5));
            },
            ONE_MS_TICK,
            true,
        );
        let sw = Instant::now();
        handler.invoke();
        let took = sw.elapsed();
        assert!(started.wait(Duration::from_secs(2)));
        release.set();
        assert!(took < Duration::from_millis(100));
    }

    #[test]
    fn invoke_when_sync_subscriber_blocks_until_complete() {
        let handler = Arc::new(TickEventHandler::default());
        let (entered, release) = (Gate::default(), Gate::default());
        let (e, r) = (entered.clone(), release.clone());
        handler.subscribe(
            move |_| {
                e.set();
                r.wait(Duration::from_secs(5));
            },
            ONE_MS_TICK,
            false,
        );
        let (tx, rx) = mpsc::channel();
        let h = handler.clone();
        std::thread::spawn(move || {
            h.invoke();
            tx.send(()).unwrap();
        });
        assert!(entered.wait(Duration::from_secs(2)));
        assert!(rx.try_recv().is_err(), "invoke must still be running");
        release.set();
        rx.recv_timeout(Duration::from_secs(2)).unwrap();
    }

    #[test]
    fn invoke_when_async_subscriber_busy_does_not_start_overlapping_run() {
        let handler = TickEventHandler::default();
        let starts = Arc::new(AtomicUsize::new(0));
        let (started, release) = (Gate::default(), Gate::default());
        let (n, s, r) = (starts.clone(), started.clone(), release.clone());
        handler.subscribe(
            move |_| {
                n.fetch_add(1, Ordering::SeqCst);
                s.set();
                r.wait(Duration::from_secs(5));
            },
            ONE_MS_TICK,
            true,
        );
        handler.invoke();
        assert!(started.wait(Duration::from_secs(2)));
        for _ in 0..20 {
            std::thread::sleep(Duration::from_millis(2));
            handler.invoke();
        }
        assert_eq!(starts.load(Ordering::SeqCst), 1);
        release.set();
    }

    #[test]
    fn repeated_invoke_when_slow_neighbor_is_async_invoke_keeps_returning() {
        let handler = TickEventHandler::default();
        let (slow_started, release) = (Gate::default(), Gate::default());
        handler.subscribe(|_| {}, ONE_MS_TICK, true);
        let (s, r) = (slow_started.clone(), release.clone());
        handler.subscribe(
            move |_| {
                s.set();
                r.wait(Duration::from_secs(5));
            },
            ONE_MS_TICK,
            true,
        );
        handler.invoke();
        assert!(slow_started.wait(Duration::from_secs(2)));
        let sw = Instant::now();
        for _ in 0..10 {
            handler.invoke();
        }
        let took = sw.elapsed();
        release.set();
        assert!(took < Duration::from_millis(100));
    }

    #[test]
    fn unsubscribe_and_tick_rate() {
        let handler = TickEventHandler::default();
        let calls = Arc::new(AtomicUsize::new(0));
        let c = calls.clone();
        let id = handler.subscribe(
            move |_| {
                c.fetch_add(1, Ordering::SeqCst);
            },
            Duration::from_secs(60),
            false,
        );
        // First invoke always runs (delta = rate + 1 ms), the next one is not due yet.
        handler.invoke();
        handler.invoke();
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        handler.unsubscribe(id);
        handler.invoke();
        assert_eq!(handler.subscriber_count(), 0);
    }

    #[test]
    fn panicking_subscriber_is_contained() {
        let handler = TickEventHandler::default();
        let calls = Arc::new(AtomicUsize::new(0));
        handler.subscribe(|_| panic!("boom"), ONE_MS_TICK, false);
        let c = calls.clone();
        handler.subscribe(
            move |_| {
                c.fetch_add(1, Ordering::SeqCst);
            },
            ONE_MS_TICK,
            false,
        );
        handler.invoke();
        assert_eq!(calls.load(Ordering::SeqCst), 1);
    }
}
