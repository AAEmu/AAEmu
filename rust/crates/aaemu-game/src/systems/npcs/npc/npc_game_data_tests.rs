//! Port of AAEmu.UnitTests/Game/GameData/NpcGameDataTests.cs
//!
//! `NpcGameData` is [`NpcSpawnerData`]. `IsNotSameReferenceAs` becomes "separate state": a
//! spawner added to one instance is not seen by the other.

use super::{NpcSpawnerData, NpcSpawnerTemplate};

#[test]
fn can_create_instance() {
    let instance = NpcSpawnerData::default();
    assert_eq!(instance.template_count(), 0);
    assert_eq!(instance.member_count(), 0);
}

#[test]
fn new_instances_are_independent() {
    let instance1 = NpcSpawnerData::default();
    let instance2 = NpcSpawnerData::default();
    assert!(!std::ptr::eq(&instance1, &instance2));

    assert!(instance1.add_npc_spawner(NpcSpawnerTemplate {
        id: 1,
        ..NpcSpawnerTemplate::default()
    }));

    assert!(instance1.get_npc_spawner_template(1).is_some());
    assert!(instance2.get_npc_spawner_template(1).is_none());
    assert_eq!(instance2.template_count(), 0);
}
