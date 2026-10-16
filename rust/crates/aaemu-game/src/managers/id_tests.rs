//! Port of AAEmu.UnitTests/Game/Core/Managers/Id/IdManagerTests.cs
//!
//! The C# `[MethodDataSource]` rows (`IdManagerData` / `IdManagerFirstIdData`) become a loop over
//! [`managers`]. `manager.Initialize(true)` is [`IdManager::initialize_without_db`] (the unit
//! tests have no MySQL: the C# falls back to "no used ids").

use std::collections::HashSet;

use super::*;

/// `IdManagerFirstIdData` (`IdManagerData` = the same managers).
fn managers() -> Vec<(IdManager, u32)> {
    vec![
        (IdManager::new(CHARACTER_ID), 0x0000_0001),
        (IdManager::new(ITEM_ID), 0x0100_0000),
        (IdManager::new(OBJECT_ID), 0x0000_0100),
        (IdManager::new(DOODAD_ID), 0x0000_0001),
        (IdManager::new(AUCTION_ID), 0x0000_0001),
    ]
}

fn initialized(spec: IdManagerSpec) -> IdManager {
    let m = IdManager::new(spec);
    assert!(m.initialize_without_db(true));
    m
}

fn next(m: &IdManager) -> u32 {
    m.get_next_id().expect("id")
}

// ----- GetNextId -----

#[test]
fn get_next_id_first_call_returns_first_id() {
    for (manager, expected_first_id) in managers() {
        manager.initialize_without_db(true);
        assert_eq!(next(&manager), expected_first_id, "{}", manager.name());
    }
}

#[test]
fn get_next_id_multiple_calls_returns_sequential_ids() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let first_id = next(&manager);
        let second_id = next(&manager);
        let third_id = next(&manager);
        assert_eq!(second_id, first_id + 1, "{}", manager.name());
        assert_eq!(third_id, first_id + 2, "{}", manager.name());
    }
}

#[test]
fn get_next_id_multiple_calls_ids_are_unique() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let count = 100;
        let ids: HashSet<u32> = (0..count).map(|_| next(&manager)).collect();
        assert_eq!(ids.len(), count, "{}", manager.name());
    }
}

// ----- GetNextId(int count) -----

#[test]
fn get_next_id_with_count_returns_array_of_correct_size() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let ids = manager.get_next_ids(10).unwrap();
        assert_eq!(ids.len(), 10, "{}", manager.name());
    }
}

#[test]
fn get_next_id_with_count_returns_sequential_ids() {
    for (manager, expected_first_id) in managers() {
        manager.initialize_without_db(true);
        let ids = manager.get_next_ids(10).unwrap();
        assert_eq!(ids[0], expected_first_id, "{}", manager.name());
        for w in ids.windows(2) {
            assert_eq!(w[1], w[0] + 1, "{}", manager.name());
        }
    }
}

#[test]
fn get_next_id_with_count_all_ids_are_unique() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let ids = manager.get_next_ids(100).unwrap();
        let unique: HashSet<u32> = ids.iter().copied().collect();
        assert_eq!(unique.len(), 100, "{}", manager.name());
    }
}

#[test]
fn get_next_id_zero_count_returns_empty_array() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        assert!(
            manager.get_next_ids(0).unwrap().is_empty(),
            "{}",
            manager.name()
        );
    }
}

#[test]
fn get_next_id_single_count_returns_array_with_one_element() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        assert_eq!(
            manager.get_next_ids(1).unwrap().len(),
            1,
            "{}",
            manager.name()
        );
    }
}

// ----- ReleaseId -----

#[test]
fn release_id_after_getting_id_id_can_be_reused() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let id = next(&manager);
        next(&manager); // Get another ID to move forward
        manager.release_id(id);
        assert_eq!(next(&manager), id, "{}", manager.name());
    }
}

#[test]
fn release_id_multiple_ids_released_in_reverse_order() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let id1 = next(&manager);
        let id2 = next(&manager);
        let id3 = next(&manager);

        manager.release_id(id3);
        manager.release_id(id2);
        manager.release_id(id1);

        // the smallest released id comes first
        assert_eq!(next(&manager), id1, "{}", manager.name());
        assert_eq!(next(&manager), id2, "{}", manager.name());
        assert_eq!(next(&manager), id3, "{}", manager.name());
    }
}

#[test]
fn release_id_invalid_id_does_not_throw() {
    for (manager, first_id) in managers() {
        manager.initialize_without_db(true);
        // Both are rejected ("release objectId ... failed"): the C# offset cast to int is
        // negative for 0 (below the first id) and for uint.MaxValue.
        manager.release_id(0);
        manager.release_id(u32::MAX);
        assert_eq!(next(&manager), first_id, "{}", manager.name());
    }
}

#[test]
fn release_id_multiple_ids_via_enumerable_all_released() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let ids = manager.get_next_ids(5).unwrap();
        manager.get_next_ids(5).unwrap(); // Move forward

        manager.release_ids(ids.iter().copied());

        let new_ids: Vec<u32> = (0..5).map(|_| next(&manager)).collect();
        assert!(
            ids.iter().all(|id| new_ids.contains(id)),
            "{}",
            manager.name()
        );
    }
}

// ----- specific managers -----

fn assert_first_sequential_and_reuse(spec: IdManagerSpec, first: u32) {
    // *_GetNextId_ReturnsCorrectFirstId
    let m = initialized(spec);
    assert_eq!(next(&m), first);
    // *_GetNextId_SequentialCalls_IncrementsCorrectly
    let m = initialized(spec);
    assert_eq!(
        [next(&m), next(&m), next(&m)],
        [first, first + 1, first + 2]
    );
    // *_ReleaseId_MakesIdAvailable
    let m = initialized(spec);
    let id = next(&m);
    next(&m);
    m.release_id(id);
    assert_eq!(next(&m), id);
}

#[test]
fn character_id_manager_get_next_id_returns_correct_first_id() {
    assert_eq!(next(&initialized(CHARACTER_ID)), 0x0000_0001);
}

#[test]
fn character_id_manager_get_next_id_sequential_calls_increments_correctly() {
    let m = initialized(CHARACTER_ID);
    assert_eq!([next(&m), next(&m), next(&m)], [1, 2, 3]);
}

#[test]
fn character_id_manager_release_id_makes_id_available() {
    assert_first_sequential_and_reuse(CHARACTER_ID, 0x0000_0001);
}

#[test]
fn item_id_manager_get_next_id_returns_correct_first_id() {
    assert_eq!(next(&initialized(ITEM_ID)), 0x0100_0000);
}

#[test]
fn item_id_manager_get_next_id_sequential_calls_increments_correctly() {
    let m = initialized(ITEM_ID);
    assert_eq!(
        [next(&m), next(&m), next(&m)],
        [0x0100_0000, 0x0100_0001, 0x0100_0002]
    );
}

#[test]
fn item_id_manager_release_id_makes_id_available() {
    assert_first_sequential_and_reuse(ITEM_ID, 0x0100_0000);
}

#[test]
fn object_id_manager_get_next_id_returns_correct_first_id() {
    assert_eq!(next(&initialized(OBJECT_ID)), 0x0000_0100);
}

#[test]
fn object_id_manager_get_next_id_sequential_calls_increments_correctly() {
    let m = initialized(OBJECT_ID);
    assert_eq!([next(&m), next(&m), next(&m)], [0x100, 0x101, 0x102]);
}

#[test]
fn object_id_manager_release_id_makes_id_available() {
    assert_first_sequential_and_reuse(OBJECT_ID, 0x0000_0100);
}

#[test]
fn release_hook_sees_released_ids_but_not_rejected_ones() {
    let m = initialized(OBJECT_ID);
    let seen = std::sync::Arc::new(std::sync::Mutex::new(Vec::new()));
    let s2 = seen.clone();
    m.install_release_hook(move |id| s2.lock().unwrap().push(id));
    let id = next(&m);
    m.release_id(id);
    m.release_id(0x10); // below the first id: rejected, not observed
    assert_eq!(*seen.lock().unwrap(), vec![id]);
}

#[test]
fn object_id_manager_get_next_id_multiple_returns_array() {
    let m = initialized(OBJECT_ID);
    let first_id = 0x0000_0100;
    let ids = m.get_next_ids(10).unwrap();
    assert_eq!(ids.len(), 10);
    assert_eq!(ids[0], first_id);
    assert_eq!(ids[9], first_id + 9);
}

#[test]
fn doodad_id_manager_get_next_id_returns_correct_first_id() {
    assert_eq!(next(&initialized(DOODAD_ID)), 0x0000_0001);
}

#[test]
fn doodad_id_manager_get_next_id_sequential_calls_increments_correctly() {
    let m = initialized(DOODAD_ID);
    assert_eq!([next(&m), next(&m), next(&m)], [1, 2, 3]);
}

#[test]
fn doodad_id_manager_release_id_makes_id_available() {
    assert_first_sequential_and_reuse(DOODAD_ID, 0x0000_0001);
}

#[test]
fn auction_id_manager_get_next_id_returns_correct_first_id() {
    assert_eq!(next(&initialized(AUCTION_ID)), 0x0000_0001);
}

#[test]
fn auction_id_manager_get_next_id_sequential_calls_increments_correctly() {
    let m = initialized(AUCTION_ID);
    assert_eq!([next(&m), next(&m), next(&m)], [1, 2, 3]);
}

#[test]
fn auction_id_manager_release_id_makes_id_available() {
    assert_first_sequential_and_reuse(AUCTION_ID, 0x0000_0001);
}

// ----- edge cases and stress tests -----

#[test]
fn get_next_id_large_number_of_calls_all_ids_unique() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let ids: HashSet<u32> = (0..1000).map(|_| next(&manager)).collect();
        assert_eq!(ids.len(), 1000, "{}", manager.name());
    }
}

#[test]
fn initialize_multiple_calls_does_not_reset_ids() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let id1 = next(&manager);

        manager.initialize_without_db(false); // second call without forceReset
        let id2 = next(&manager);

        assert_ne!(id2, id1, "{}", manager.name());
        assert_eq!(id2, id1 + 1, "{}", manager.name());
    }
}

#[test]
fn initialize_force_reset_resets_ids() {
    for (manager, first_id) in managers() {
        manager.initialize_without_db(true);
        next(&manager);
        next(&manager);
        next(&manager);

        manager.initialize_without_db(true); // force reset
        let id = next(&manager);

        assert!(id <= 0x0100_0001, "{}", manager.name());
        assert_eq!(id, first_id, "{}", manager.name());
    }
}

#[test]
fn release_and_get_many_ids_correctly_recycles() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let ids = manager.get_next_ids(50).unwrap();
        for &id in &ids {
            manager.release_id(id);
        }
        let new_ids: HashSet<u32> = manager.get_next_ids(50).unwrap().into_iter().collect();
        assert_eq!(
            new_ids,
            ids.into_iter().collect::<HashSet<u32>>(),
            "{}",
            manager.name()
        );
    }
}

// ----- integration scenarios -----

#[test]
fn mixed_operations_release_and_get_maintains_consistency() {
    for (manager, _) in managers() {
        manager.initialize_without_db(true);
        let all_ids: Vec<u32> = (0..10).map(|_| next(&manager)).collect();

        // release every other id
        for id in all_ids.iter().step_by(2) {
            manager.release_id(*id);
        }

        let new_ids: Vec<u32> = (0..5).map(|_| next(&manager)).collect();

        let released: HashSet<u32> = all_ids.iter().step_by(2).copied().collect();
        assert!(
            new_ids.iter().all(|id| released.contains(id)),
            "{}",
            manager.name()
        );
    }
}

#[test]
fn all_managers_initialized_simultaneous_use() {
    let char_mgr = initialized(CHARACTER_ID);
    let item_mgr = initialized(ITEM_ID);
    let obj_mgr = initialized(OBJECT_ID);
    let doodad_mgr = initialized(DOODAD_ID);
    let auction_mgr = initialized(AUCTION_ID);

    assert_eq!(next(&char_mgr), 0x0000_0001);
    assert_eq!(next(&item_mgr), 0x0100_0000);
    assert_eq!(next(&obj_mgr), 0x0000_0100);
    assert_eq!(next(&doodad_mgr), 0x0000_0001);
    assert_eq!(next(&auction_mgr), 0x0000_0001);
}

#[test]
fn all_managers_release_id_independently_managed() {
    let char_mgr = initialized(CHARACTER_ID);
    let item_mgr = initialized(ITEM_ID);

    let char_id = next(&char_mgr);
    let item_id = next(&item_mgr);

    char_mgr.release_id(char_id);

    assert_eq!(next(&char_mgr), char_id);
    assert_ne!(next(&item_mgr), item_id);
}
