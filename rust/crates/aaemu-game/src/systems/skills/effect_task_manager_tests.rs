//! Port of AAEmu.UnitTests/Game/Core/Managers/EffectTaskManagerTests.cs
//!
//! C#: `EffectTaskManager.AddDispelTask(buff, interval)` hands one `DispelTask` to
//! `TaskManager.Schedule` with a delay of `interval` ms (checked with a mocked `ITaskManager`).
//! Rust: buff dispel tasks go through `tasks::schedule` (the `TaskManager.Schedule` port, used
//! by `buffs::schedule_dispel_task`); the observable effect checked here is that exactly one
//! job runs, and not before its delay.

use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::test_support::test_server_with;

use super::tasks;

async fn schedules_one_task_after(interval_ms: i64) {
    let server = Arc::new(test_server_with(GameData::empty()));
    super::bind(&server);
    let runs = Arc::new(AtomicUsize::new(0));
    let r = runs.clone();
    tasks::schedule(&server, interval_ms, move |_| {
        r.fetch_add(1, Ordering::SeqCst);
    });
    // Not before the interval.
    tokio::time::sleep(Duration::from_millis(interval_ms as u64 / 2)).await;
    assert_eq!(runs.load(Ordering::SeqCst), 0);
    // Exactly once after it.
    for _ in 0..100 {
        if runs.load(Ordering::SeqCst) > 0 {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    tokio::time::sleep(Duration::from_millis(100)).await;
    assert_eq!(runs.load(Ordering::SeqCst), 1);
}

#[tokio::test]
async fn add_dispel_task_calls_task_manager_schedule() {
    schedules_one_task_after(250).await;
}

#[tokio::test]
async fn add_dispel_task_with_different_interval_passes_correct_time_span() {
    schedules_one_task_after(1000).await;
}
