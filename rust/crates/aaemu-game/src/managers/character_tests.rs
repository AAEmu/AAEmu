//! Port of AAEmu.UnitTests/Game/Core/Managers/CharacterManagerTests.cs
//!
//! `Constructor_DoesNotCallDeps` verifies the DI wiring of `CharacterManager`. The Rust
//! `CharacterManager` has no injected managers (its tables are `CharacterData`, its operations
//! free functions taking the server), so the port checks that construction has no side
//! effects: a new `CharacterData` is empty.

use crate::data::character_templates::CharacterData;

#[test]
fn constructor_does_not_call_deps() {
    let manager = CharacterData::default();

    assert!(manager.templates.is_empty());
    assert!(manager.ability_items.is_empty());
    assert!(manager.appellations.is_empty());
    assert!(manager.actabilities.is_empty());
    assert!(manager.actability_categories.is_empty());
    assert!(manager.expert_limits.is_empty());
}
