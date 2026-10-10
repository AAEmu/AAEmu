//! Port of AAEmu.UnitTests/Game/Core/Managers/SusManagerTests.cs and
//! AAEmu.UnitTests/Game/Core/Managers/AccountManagerTests.cs (`Initialize`; the constructor test
//! is in `managers::manager_tests`).
//!
//! The Rust `SusManager` has no injected `WorldManager`: the movement analysis takes the world
//! position from its caller, so "does not call the world manager" holds by construction; these
//! tests check the manager's own observable state instead.

use std::sync::Arc;

use super::accounts::{self, DEAD_CONNECTION_TIMEOUT};
use super::sus::SusManager;
use crate::test_support::test_server;

/// `SusManagerTests.Constructor_WithMockedWorldManager_DoesNotThrow`: a new manager tracks no
/// position (the first sample is only recorded).
#[test]
fn constructor_with_mocked_world_manager_does_not_throw() {
    let manager = SusManager::default();
    assert_eq!(
        manager.analyze_player_delta_movement(42, [0.0; 3], false, false, 1.0),
        None
    );
}

/// `SusManagerTests.ResetAnalyzePlayerDeltaMovement_DoesNotCallWorldManager`: resetting an
/// unknown player is a no-op, and a reset drops the tracked position (the next sample is a
/// first sample again, so a large jump is not reported).
#[test]
fn reset_analyze_player_delta_movement_does_not_call_world_manager() {
    let manager = SusManager::default();
    manager.reset_analyze_player_delta_movement(42);

    manager.analyze_player_delta_movement(42, [0.0; 3], false, false, 1.0);
    manager.reset_analyze_player_delta_movement(42);
    assert_eq!(
        manager.analyze_player_delta_movement(42, [10_000.0, 0.0, 0.0], false, false, 1.0),
        None
    );
}

/// `AccountManagerTests.Initialize_AccessesOnTickProperty`: `Initialize` subscribes exactly one
/// handler (`RemoveDeadConnections`, every 30 s) to `TickManager.OnTick`.
#[tokio::test]
async fn initialize_accesses_on_tick_property() {
    let server = Arc::new(test_server());
    let on_tick = &server.systems.services.tick.on_tick;
    let before = on_tick.subscriber_count();
    accounts::initialize(&server);
    assert_eq!(on_tick.subscriber_count(), before + 1);
    assert_eq!(DEAD_CONNECTION_TIMEOUT, std::time::Duration::from_secs(30));
}
