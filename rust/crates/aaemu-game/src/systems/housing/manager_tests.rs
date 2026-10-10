//! Port of AAEmu.UnitTests/Game/Core/Managers/HousingManagerTests.cs
//!
//! The C# test checks that the DI constructor touches none of its 14 injected managers. The
//! Rust `HousingManager` has no injected dependencies, so this asserts that construction has no
//! side effects: no house is registered or queued for deletion.

use super::manager::HousingManager;

#[test]
fn constructor_does_not_call_deps() {
    let manager = HousingManager::default();
    assert_eq!(manager.count(), 0);
    assert!(manager.all().is_empty());
    assert!(manager.by_id(1).is_none());
    assert!(manager.removed_ids().is_empty());
}
