//! Port of AAEmu.UnitTests/Game/Core/Managers/Name/NameManagerTests.cs
//!
//! `new NameManager()` without a character manager = a `pending_deletion` closure answering
//! false (the C# `characterManager?.Value...` is null); the mocked
//! `ICharacterManager.IsCharacterPendingDeletion(name)` is the closure's answer.

use std::collections::HashMap;

use super::*;

fn empty() -> NameManager {
    let sut = NameManager::default();
    sut.load_from(HashMap::new(), HashMap::new(), HashMap::new());
    sut
}

#[test]
fn empty_name_manager_should_not_have_names() {
    let sut = empty();
    assert!(sut.no_names_registered());
}

#[test]
fn add_character_name_should_have_names() {
    let char_name = normalize_name("TestName");
    let (char_id, char_account) = (1u32, 1000u32);
    let sut = empty();

    sut.add_character(char_id, &char_name, char_account);

    assert!(!sut.no_names_registered());
    assert_eq!(sut.get_character_name(char_id), Some(char_name.clone()));
    assert_eq!(sut.get_character_id(&char_name), char_id);
}

#[test]
fn get_character_account_should_return_found_accounts() {
    let (char_id, char_account) = (1u32, 1000u32);
    let sut = NameManager::default();
    sut.load_from(
        HashMap::new(),
        HashMap::new(),
        HashMap::from([(char_id, char_account)]),
    );

    assert_eq!(sut.get_character_account(char_id), char_account);
}

#[test]
fn validation_character_name_already_exists_check() {
    let (char_id, char_account) = (1u32, 1000u32);
    let char_name = normalize_name("TestName");
    let sut = empty();
    sut.add_character(char_id, &char_name, char_account);

    let result = sut.validate_character_name(&char_name, || false);

    assert_eq!(result, CharacterCreateError::NameAlreadyExists);
}

#[test]
fn validation_character_name_pending_deletion_failed() {
    let (char_id, char_account) = (1u32, 1000u32);
    let char_name = normalize_name("TestName");
    let sut = empty();
    sut.add_character(char_id, &char_name, char_account);

    let result = sut.validate_character_name(&char_name, || true);

    assert_eq!(result, CharacterCreateError::Failed);
}

#[test]
fn validation_character_invalid_name() {
    for provided in ["", "   ", "000&#$*"] {
        let char_name = normalize_name(provided);
        let sut = empty();

        let result = sut.validate_character_name(&char_name, || false);

        assert_eq!(
            result,
            CharacterCreateError::InvalidCharacters,
            "{provided:?}"
        );
    }
}

#[test]
fn validation_character_valid_name_succeed() {
    for provided in ["Roger", "Zero", "NLObP"] {
        let char_name = normalize_name(provided);
        let sut = empty();

        let result = sut.validate_character_name(&char_name, || false);

        assert_eq!(result, CharacterCreateError::Ok, "{provided:?}");
    }
}

#[test]
fn remove_character_id_works_as_expected() {
    let (char_id, char_account) = (1u32, 1000u32);
    let char_name = normalize_name("TestName");
    let sut = empty();
    sut.add_character(char_id, &char_name, char_account);

    sut.remove_character_id(char_id);

    assert!(sut.no_names_registered());
    assert_eq!(sut.get_character_name(char_id), None);
    assert_eq!(sut.get_character_id(&char_name), 0);
    assert_eq!(sut.get_character_account(char_id), 0);
}
