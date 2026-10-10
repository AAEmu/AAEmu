//! Port of AAEmu.UnitTests/Game/Core/Managers/TeamManagerTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/FamilyManagerTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/ExpeditionManagerTests.cs and
//! AAEmu.UnitTests/Game/Core/Managers/World/FactionManagerTests.cs.
//!
//! The C# tests check that the DI constructors touch none of their injected managers. The Rust
//! managers have no injected dependencies, so these assert that construction has no side
//! effects: the new manager's observable state is empty.

use super::expedition::ExpeditionManager;
use super::faction::FactionRelations;
use super::family::FamilyManager;
use super::team::TeamManager;
use crate::data::world_data::FactionData;

/// `TeamManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn team_manager_constructor_does_not_call_deps() {
    let manager = TeamManager::default();
    assert!(manager.active_team(1).is_none());
    assert!(manager.active_team_by_unit(1).is_none());
}

/// `FamilyManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn family_manager_constructor_does_not_call_deps() {
    let manager = FamilyManager::default();
    assert_eq!(manager.count(), 0);
    assert!(manager.family(1).is_none());
}

/// `ExpeditionManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn expedition_manager_constructor_does_not_call_deps() {
    let manager = ExpeditionManager::default();
    assert!(manager.all().is_empty());
    assert!(!manager.exists(1));
}

/// `FactionManagerTests.Constructor_WithMockedLocalizationManager_DoesNotThrow`: the Rust
/// `FactionManager` is its loaded data (`FactionData` + `FactionRelations`); the localization
/// is only read by `FactionData::load`, so constructing does not touch it.
#[test]
fn faction_manager_constructor_with_mocked_localization_manager_does_not_throw() {
    let factions = FactionData::default();
    assert!(factions.factions.is_empty());
    assert!(factions.get(1).is_none());
    let relations = FactionRelations::default();
    assert!(relations.is_empty());
}
