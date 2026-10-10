//! Port of AAEmu.UnitTests/Game/Models/Game/Quests/CompletedQuestTests.cs
//!
//! `Body` (`BitArray`) is the stored byte form: 64 bits = 8 bytes, null = empty.

// The C# tests construct the object, then set its (private) fields: kept as written.
#![allow(clippy::field_reassign_with_default)]

use super::CompletedQuest;

#[test]
fn default_constructor_should_initialize_empty() {
    let quest = CompletedQuest::default();

    assert_eq!(quest.id, 0);
    assert!(quest.body.is_empty());
}

#[test]
fn parameterized_constructor_should_initialize_properties() {
    let quest = CompletedQuest::new(123);

    assert_eq!(quest.id, 123);
    assert_eq!(quest.body.len() * 8, 64);
    assert!(quest.body.iter().all(|&b| b == 0));
}

#[test]
fn parameterized_constructor_should_accept_various_ids() {
    for id in [0, 1, 100, u16::MAX] {
        assert_eq!(CompletedQuest::new(id).id, id);
    }
}

#[test]
fn body_should_be_initialized_with_64_bits() {
    let quest = CompletedQuest::new(1);

    assert_eq!(quest.body.len() * 8, 64);
}

#[test]
fn properties_can_be_modified() {
    let mut quest = CompletedQuest::default();

    quest.id = 42;

    assert_eq!(quest.id, 42);
}
