//! Port of AAEmu.UnitTests/Game/GameData/BuffGameDataTests.cs

use super::misc::BuffGameData;
use super::BuffTolerance;

#[test]
fn can_create_instance() {
    let instance = BuffGameData::default();
    assert!(instance.buff_modifiers.is_empty());
    assert!(instance.tolerances.is_empty());
}

/// C#: two `new BuffGameData()` are different references. Rust: values are independent, so a
/// change to one is not seen by the other.
#[test]
fn new_instances_are_independent() {
    let mut instance1 = BuffGameData::default();
    let instance2 = BuffGameData::default();
    instance1.tolerances.insert(
        1,
        BuffTolerance {
            id: 1,
            buff_tag_id: 1,
            step_duration: 0,
            final_step_buff_id: 0,
            character_time_reduction: 0,
            steps: Vec::new(),
        },
    );
    assert_eq!(instance1.tolerances.len(), 1);
    assert!(instance2.tolerances.is_empty());
}
