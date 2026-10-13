//! Port of AAEmu.UnitTests/Game/Core/Managers/CharacterManagerTests.cs
//!
//! `Constructor_DoesNotCallDeps` verifies the DI wiring of `CharacterManager`. The Rust
//! `CharacterManager` has no injected managers (its tables are `CharacterData`, its operations
//! free functions taking the server), so the port checks that construction has no side
//! effects: a new `CharacterData` is empty.

use crate::data::character_templates::CharacterData;
use crate::data::skills::{DefaultSkill, SkillTemplate};
use crate::data::GameData;
use crate::models::char_data::{read_action_slots, ActionSlot, ActionSlotType, MAX_ACTION_SLOTS};
use crate::models::character::Character;
use crate::test_support::test_server_with;

use super::initialize_starting_skills;

const STARTERS: [(u8, u32); 10] = [
    (1, 18132),
    (2, 14376),
    (3, 10399),
    (4, 16486),
    (5, 14810),
    (6, 16210),
    (7, 10752),
    (8, 18125),
    (9, 11973),
    (10, 10534),
];

fn starting_skill_data() -> GameData {
    let mut data = GameData::empty();
    for (ability_id, id) in STARTERS {
        data.skills.add_skill(SkillTemplate {
            id,
            ability_id,
            ability_level: 1,
            auto_learn: true,
            need_learn: true,
            show: true,
            ..SkillTemplate::default()
        });
    }
    for (slot, skill_id, add_to_slot) in [
        (1, 2, true),
        (2, 16064, true),
        (14, 14495, true),
        (21, 10594, true),
        (6, 20297, false),
    ] {
        data.skills.add_skill(SkillTemplate {
            id: skill_id,
            ..SkillTemplate::default()
        });
        data.skills.default_skills.push(DefaultSkill {
            skill_id,
            slot,
            add_to_slot,
        });
    }
    data.skills.finish();
    data
}

fn new_character(ability: u8) -> Character {
    let mut c = Character::new(1, 1, Default::default());
    c.unit.level = 1;
    c.ability1 = ability;
    c.ability2 = 11;
    c.ability3 = 11;
    c.abilities.set_ability(ability, 0);
    c
}

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

#[tokio::test]
async fn selected_ability_starter_is_first_and_survives_saved_hotbar_roundtrip() {
    let server = test_server_with(starting_skill_data());
    for (ability, skill_id) in STARTERS {
        let mut c = new_character(ability);
        c.set_action(10, ActionSlotType::ItemType, 4045);
        c.set_action(11, ActionSlotType::ItemType, 18791);
        initialize_starting_skills(&server, &mut c).unwrap();

        assert_eq!(c.skills.skills.len(), 1);
        assert_eq!(c.skills.skills[0].id, skill_id);
        assert_eq!(c.slots[1].kind, ActionSlotType::Spell);
        assert_eq!(c.slots[1].action_id, u64::from(skill_id));
        assert_eq!(c.slots[2].action_id, 16064, "ranged attack retained");
        assert_eq!(c.slots[3].action_id, 2, "melee attack retained");
        assert_eq!(c.slots[6].kind, ActionSlotType::None);
        assert_eq!(c.slots[10].action_id, 4045);
        assert_eq!(c.slots[10].kind, ActionSlotType::ItemType);
        assert_eq!(c.slots[11].action_id, 18791);
        assert_eq!(c.slots[14].action_id, 14495);
        assert_eq!(c.slots[21].action_id, 10594);

        let mut saved = vec![ActionSlot::default(); MAX_ACTION_SLOTS];
        read_action_slots(&mut saved, &c.action_slots_blob());
        assert_eq!(saved, c.slots);
    }
}

#[tokio::test]
async fn dense_shared_defaults_do_not_bury_sorcery_starter() {
    let mut data = starting_skill_data();
    data.skills.default_skills.clear();
    for slot in 1..=15 {
        let skill_id = 1000 + u32::from(slot);
        data.skills.add_skill(SkillTemplate {
            id: skill_id,
            ..SkillTemplate::default()
        });
        data.skills.default_skills.push(DefaultSkill {
            skill_id,
            slot,
            add_to_slot: true,
        });
    }
    let server = test_server_with(data);
    let mut c = new_character(7);
    initialize_starting_skills(&server, &mut c).unwrap();
    assert_eq!(c.slots[1].action_id, 10752);
    assert_eq!(c.slots[16].action_id, 1001);
    for slot in 2..=15 {
        assert_eq!(c.slots[slot].action_id, 1000 + slot as u64);
    }
}

#[tokio::test]
async fn additional_starting_spells_skip_occupied_utility_slots() {
    let mut data = starting_skill_data();
    data.skills.add_skill(SkillTemplate {
        id: 30000,
        ability_id: 7,
        ability_level: 1,
        auto_learn: true,
        need_learn: true,
        show: true,
        ..SkillTemplate::default()
    });
    data.skills.finish();
    let server = test_server_with(data);
    let mut c = new_character(7);
    c.set_action(4, ActionSlotType::ItemType, 18791);
    initialize_starting_skills(&server, &mut c).unwrap();
    assert_eq!(c.slots[1].action_id, 10752);
    assert_eq!(c.slots[4].action_id, 18791);
    assert_eq!(c.slots[4].kind, ActionSlotType::ItemType);
    assert_eq!(c.slots[5].action_id, 30000);
    assert_eq!(c.skills.skills.len(), 2);
}

#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn starting_hotbar_real_compact_db() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let mut data = GameData::empty();
    data.skills = crate::data::SkillData::load(&conn).unwrap();
    let server = test_server_with(data);
    for (ability, skill_id) in STARTERS {
        assert_eq!(
            server.game_data.skills.start_ability_skills(ability),
            Some([skill_id].as_slice())
        );
        let mut c = new_character(ability);
        initialize_starting_skills(&server, &mut c).unwrap();
        assert_eq!(c.slots[1].action_id, u64::from(skill_id));
        assert_eq!(c.skills.skills[0].id, skill_id);
    }
}
