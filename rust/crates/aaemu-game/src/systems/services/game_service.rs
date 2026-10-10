//! `GameService` (the hosted service's start time and `TimeSinceStart`), `Program.UpTime` and
//! `Core/Managers/ManagerOrchestrator` (dependency-ordered manager batches). The startup /
//! shutdown sequence itself is in `main.rs` (`GameService.StartAsync` / `StopAsync` stages).

use std::collections::{HashMap, HashSet};
use std::sync::{Arc, OnceLock, RwLock};
use std::time::Instant;

use chrono::{NaiveDateTime, TimeDelta, Utc};

/// `TimeProvider` (`GetUtcNow`).
pub trait TimeProvider: Send + Sync {
    fn utc_now(&self) -> NaiveDateTime;
}

/// `TimeProvider.System`.
#[derive(Debug, Default, Clone, Copy)]
pub struct SystemTimeProvider;

impl TimeProvider for SystemTimeProvider {
    fn utc_now(&self) -> NaiveDateTime {
        Utc::now().naive_utc()
    }
}

/// `FakeTimeProvider` (tests): a manually advanced clock.
#[derive(Debug)]
pub struct FakeTimeProvider(RwLock<NaiveDateTime>);

impl Default for FakeTimeProvider {
    fn default() -> Self {
        // `FakeTimeProvider()` starts at 2000-01-01 00:00:00 UTC.
        Self(RwLock::new(
            chrono::NaiveDate::from_ymd_opt(2000, 1, 1)
                .and_then(|d| d.and_hms_opt(0, 0, 0))
                .expect("valid date"),
        ))
    }
}

impl FakeTimeProvider {
    /// `Advance(delta)`.
    pub fn advance(&self, delta: TimeDelta) {
        *self.0.write().expect("clock poisoned") += delta;
    }
}

impl TimeProvider for FakeTimeProvider {
    fn utc_now(&self) -> NaiveDateTime {
        *self.0.read().expect("clock poisoned")
    }
}

struct Clock {
    provider: Arc<dyn TimeProvider>,
    start_time: NaiveDateTime,
}

/// The static `s_timeProvider` / `StartTime` of `GameService`.
static CLOCK: std::sync::LazyLock<RwLock<Clock>> = std::sync::LazyLock::new(|| {
    RwLock::new(Clock {
        provider: Arc::new(SystemTimeProvider),
        start_time: Utc::now().naive_utc(),
    })
});

/// `GameService` (`IHostedService`, `IDisposable`).
pub struct GameService {
    pub orchestrator: ManagerOrchestrator,
}

impl GameService {
    /// `new GameService(serviceProvider, orchestrator, timeProvider)`: sets the static time
    /// provider and `StartTime = timeProvider.GetUtcNow()`.
    pub fn new(orchestrator: ManagerOrchestrator, time_provider: Arc<dyn TimeProvider>) -> Self {
        let mut c = CLOCK.write().expect("clock poisoned");
        c.start_time = time_provider.utc_now();
        c.provider = time_provider;
        Self { orchestrator }
    }

    /// `GameService.StartTime`.
    pub fn start_time() -> NaiveDateTime {
        CLOCK.read().expect("clock poisoned").start_time
    }

    /// `GameService.TimeSinceStart`.
    pub fn time_since_start() -> TimeDelta {
        let c = CLOCK.read().expect("clock poisoned");
        c.provider.utc_now() - c.start_time
    }

    /// `Dispose()`: "Disposing..." (NLog flush).
    pub fn dispose(&self) {
        tracing::info!("Disposing...");
    }
}

impl Drop for GameService {
    fn drop(&mut self) {
        // `using var service = ...` disposes; nothing else to release.
    }
}

/// `Program._startTime` (set by `Program.Initialization`).
static PROGRAM_START: OnceLock<Instant> = OnceLock::new();

/// `Program.Initialization()`: records the process start (first call wins).
pub fn program_initialization() {
    let _ = PROGRAM_START.set(Instant::now());
}

/// `Program.UpTime`: whole seconds since `Program.Initialization` (0 before it).
pub fn up_time() -> i32 {
    PROGRAM_START
        .get()
        .map_or(0, |s| s.elapsed().as_secs().min(i32::MAX as u64) as i32)
}

/// A constructor dependency of a manager (`Lazy<T>` parameters break cycles and are no edge).
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Dependency {
    Direct(&'static str),
    Lazy(&'static str),
}

/// One registered manager: its name, the interfaces it takes in its constructor and whether it
/// implements the orchestrated interface (`ILoadable` / `IInitializable`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ManagerRegistration {
    pub name: &'static str,
    pub dependencies: Vec<Dependency>,
    pub loadable: bool,
    pub initializable: bool,
}

/// `ManagerOrchestrator.BuildBatches` failure.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("Cycle detected in manager dependency graph among: {0}")]
pub struct CycleError(pub String);

/// Which orchestrated interface to batch.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Stage {
    /// `ILoadable.Load()` (stage 2).
    Load,
    /// `IInitializable.Initialize()` (stage 4).
    Initialize,
}

/// `ManagerOrchestrator`.
#[derive(Debug, Clone, Default)]
pub struct ManagerOrchestrator {
    registrations: Vec<ManagerRegistration>,
}

impl ManagerOrchestrator {
    pub fn new(registrations: Vec<ManagerRegistration>) -> Self {
        Self { registrations }
    }

    /// `BuildBatches<TInterface>()`: topological batches (Kahn) over the constructor
    /// dependencies between participating managers; each batch may run in parallel. The
    /// managers of a batch keep their registration order.
    pub fn build_batches(&self, stage: Stage) -> Result<Vec<Vec<&'static str>>, CycleError> {
        let participating: Vec<&ManagerRegistration> = self
            .registrations
            .iter()
            .filter(|r| match stage {
                Stage::Load => r.loadable,
                Stage::Initialize => r.initializable,
            })
            .collect();
        let names: HashSet<&str> = participating.iter().map(|r| r.name).collect();
        let deps: HashMap<&str, Vec<&str>> = participating
            .iter()
            .map(|r| {
                let d = r
                    .dependencies
                    .iter()
                    .filter_map(|d| match d {
                        Dependency::Direct(n) if names.contains(n) => Some(*n),
                        _ => None,
                    })
                    .collect();
                (r.name, d)
            })
            .collect();
        let mut remaining: Vec<&'static str> = participating.iter().map(|r| r.name).collect();
        let mut batches = Vec::new();
        while !remaining.is_empty() {
            let rem: HashSet<&str> = remaining.iter().copied().collect();
            let batch: Vec<&'static str> = remaining
                .iter()
                .copied()
                .filter(|n| deps[n].iter().all(|d| !rem.contains(d)))
                .collect();
            if batch.is_empty() {
                return Err(CycleError(remaining.join(", ")));
            }
            remaining.retain(|n| !batch.contains(n));
            batches.push(batch);
        }
        Ok(batches)
    }
}

#[cfg(test)]
mod tests {
    //! Ports of `GameServiceTests.cs` and `ManagerOrchestratorTests.cs`.
    use super::*;

    /// The static clock is shared (`[NotInParallel]` in C#).
    static CLOCK_TESTS: std::sync::Mutex<()> = std::sync::Mutex::new(());

    #[test]
    fn start_time_is_initialized_to_utc_now() {
        let _g = CLOCK_TESTS.lock().unwrap();
        let fake = Arc::new(FakeTimeProvider::default());
        let service = GameService::new(ManagerOrchestrator::default(), fake.clone());
        assert_eq!(GameService::start_time(), fake.utc_now());
        service.dispose();
    }

    #[test]
    fn time_since_start_returns_time_span_since_start() {
        let _g = CLOCK_TESTS.lock().unwrap();
        let fake = Arc::new(FakeTimeProvider::default());
        let _service = GameService::new(ManagerOrchestrator::default(), fake.clone());
        fake.advance(TimeDelta::seconds(5));
        assert_eq!(GameService::time_since_start(), TimeDelta::seconds(5));
    }

    #[test]
    fn dispose_does_not_throw() {
        let _g = CLOCK_TESTS.lock().unwrap();
        let service =
            GameService::new(ManagerOrchestrator::default(), Arc::new(SystemTimeProvider));
        service.dispose();
    }

    #[test]
    fn up_time_counts_from_initialization() {
        program_initialization();
        assert!(up_time() >= 0);
    }

    fn reg(name: &'static str, deps: Vec<Dependency>) -> ManagerRegistration {
        ManagerRegistration {
            name,
            dependencies: deps,
            loadable: true,
            initializable: false,
        }
    }

    #[test]
    fn build_batches_produces_correct_topological_order() {
        let o = ManagerOrchestrator::new(vec![
            reg("C", vec![Dependency::Direct("B")]),
            reg("A", vec![]),
            reg("B", vec![Dependency::Direct("A")]),
        ]);
        assert_eq!(
            o.build_batches(Stage::Load).unwrap(),
            vec![vec!["A"], vec!["B"], vec!["C"]]
        );
    }

    #[test]
    fn build_batches_independent_managers_are_in_same_batch() {
        let o = ManagerOrchestrator::new(vec![reg("A", vec![]), reg("Q", vec![])]);
        assert_eq!(o.build_batches(Stage::Load).unwrap(), vec![vec!["A", "Q"]]);
    }

    #[test]
    fn build_batches_throws_on_cycle() {
        let o = ManagerOrchestrator::new(vec![
            reg("CycleX", vec![Dependency::Direct("CycleY")]),
            reg("CycleY", vec![Dependency::Direct("CycleX")]),
        ]);
        let e = o.build_batches(Stage::Load).unwrap_err();
        assert!(e.to_string().contains("Cycle detected"));
    }

    #[test]
    fn build_batches_skips_lazy_dependencies() {
        let o = ManagerOrchestrator::new(vec![
            reg("Q", vec![]),
            reg("P", vec![Dependency::Lazy("Q")]),
        ]);
        assert_eq!(o.build_batches(Stage::Load).unwrap(), vec![vec!["Q", "P"]]);
    }

    #[test]
    fn build_batches_empty_and_non_participating() {
        assert!(ManagerOrchestrator::default()
            .build_batches(Stage::Load)
            .unwrap()
            .is_empty());
        // Dependencies on managers that do not implement the interface are ignored.
        let mut init_only = reg("T", vec![]);
        init_only.loadable = false;
        init_only.initializable = true;
        let o = ManagerOrchestrator::new(vec![init_only, reg("A", vec![Dependency::Direct("T")])]);
        assert_eq!(o.build_batches(Stage::Load).unwrap(), vec![vec!["A"]]);
        assert_eq!(o.build_batches(Stage::Initialize).unwrap(), vec![vec!["T"]]);
    }
}
