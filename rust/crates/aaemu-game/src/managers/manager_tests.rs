//! Port of AAEmu.UnitTests/Game/Core/Managers/EnterWorldManagerTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/AccountManagerTests.cs (constructor part; `Initialize` is
//! in `systems::services::manager_tests`) and
//! AAEmu.UnitTests/Game/Core/Managers/SaveManagerTests.cs.
//!
//! The C# `Constructor_DoesNotCallDeps` tests verify that the DI constructor touches none of
//! its injected managers. The Rust managers have no injected dependencies (they reach the
//! others through `GameServer` at call time), so these assert that construction has no side
//! effects: the new manager's observable state is empty.

use super::{AccountManager, EnterWorldManager, LoginDecision};
use crate::managers::save::SaveManager;

/// `EnterWorldManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn enter_world_manager_constructor_does_not_call_deps() {
    let manager = EnterWorldManager::default();
    // No account / token registered yet.
    assert_eq!(manager.login(1, 1), LoginDecision::InvalidToken);
    assert_eq!(manager.login(0, 0), LoginDecision::InvalidToken);
}

/// `AccountManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn account_manager_constructor_does_not_call_deps() {
    let manager = AccountManager::default();
    assert_eq!(manager.count(), 0);
    assert!(manager.entries().is_empty());
    assert!(!manager.contains(1));
}

/// `SaveManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn save_manager_constructor_does_not_call_deps() {
    let manager = SaveManager::default();
    // Not saving, auto-save not enabled (`_isSaving` / `_enabled` false), no task started.
    let state = format!("{manager:?}");
    assert!(state.contains("saving: false"), "{state}");
    assert!(state.contains("enabled: false"), "{state}");
}
