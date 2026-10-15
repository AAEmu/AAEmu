//! Regression tests for `SQL/patches/compact/2026-10-14-fix-npc-ai-param-syntax.sql`: the patch
//! must repair exactly the three verified malformed `npc_ai_params` rows (443, 835, 1975), keep
//! all authored keys, numbers and comments, leave every other row untouched and be idempotent.
//! The tests execute the real patch file (`include_str!`), not a hand-written copy of its
//! replacements.

use rusqlite::Connection;

use crate::systems::ai::params::{
    AiParamType, AiParams, AiParamsKind, ArcherCombatSkill, BigMonsterCombatSkill,
    FlytrapCombatSkill,
};

/// The actual compact-data patch.
const PATCH_SQL: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../../SQL/patches/compact/2026-10-14-fix-npc-ai-param-syntax.sql"
));

/// `npc_ai_params` 443 (npc 798, ArcherRoaming), byte-for-byte from the 1.2 compact database.
const ORIG_443: &str = "alertDuration = 10,\r\nalertSafeTargetRememberTime = 5,\r\nmeleeAttackRange = 4,\r\ncombatSkills = {\r\n  melee = {2, 12482}, --\u{d654}\u{c5fc} \u{bfdc}\u{ae30}\r\n  makeAGap = 12449,--\u{ac15}\u{b825}\u{d55c} \u{c190}\u{c544}\u{adc0}\r\n  rangedDef = {15926} --\u{bd88}\u{af43} \u{c1a1}\u{c774}\r\n  rangedStrong = {11911, 11068, } --\u{d608}\u{c561} \u{c1a1}\u{c774}, \u{c0dd}\u{ae30} \u{d761}\u{c218}\r\n},\r\npreferedCombatDist = 15,\r\nmaxMakeAGapCount = 1";

/// The repaired 443 text: only `{` `}` `,` added; every number, key and comment preserved.
const REPAIRED_443: &str = "alertDuration = 10,\r\nalertSafeTargetRememberTime = 5,\r\nmeleeAttackRange = 4,\r\ncombatSkills = {\r\n  melee = {2, 12482}, --\u{d654}\u{c5fc} \u{bfdc}\u{ae30}\r\n  makeAGap = {12449},--\u{ac15}\u{b825}\u{d55c} \u{c190}\u{c544}\u{adc0}\r\n  rangedDef = {15926}, --\u{bd88}\u{af43} \u{c1a1}\u{c774}\r\n  rangedStrong = {11911, 11068, } --\u{d608}\u{c561} \u{c1a1}\u{c774}, \u{c0dd}\u{ae30} \u{d761}\u{c218}\r\n},\r\npreferedCombatDist = 15,\r\nmaxMakeAGapCount = 1";

/// `npc_ai_params` 835 (npc 5877, Flytrap), byte-for-byte from the 1.2 compact database.
const ORIG_835: &str = "attackEndDistance = 25,\r\nmeleeAttackRange = 5,\r\ncombatSkills = {\r\n  melee = { [2,12279] },\r\n}";

/// `npc_ai_params` 1975 (npc 12169, BigMonsterHoldPosition); the row is missing only its final
/// closing brace for `combatSkills`.
const ORIG_1975: &str = "alertDuration = 3,\r\nalertSafeTargetRememberTime = 15,\r\ncombatSkills =\r\n{\r\n  { skillType = 17341, skillDelay = 0, strafeDuringDelay = false, healthRange = { 0, 90 } }, --\u{c5b4}\u{b460}\u{c758}\u{d654}\u{c0b4}\r\n  { skillType = 15189, skillDelay = 0, strafeDuringDelay = false, healthRange = { 70, 90 } }, --\u{c5b4}\u{b460}\u{c758}\u{c18d}\u{bc15}\r\n  { skillType = 15189, skillDelay = 0, strafeDuringDelay = false, healthRange = { 30, 50 } }, --\u{c5b4}\u{b460}\u{c758}\u{c18d}\u{bc15}\r\n  { skillType = 15054, skillDelay = 0, strafeDuringDelay = false, healthRange = { 50, 70 } }, --\u{c8fd}\u{c74c}\u{c758}\u{c18d}\u{bc15}\r\n  { skillType = 15054, skillDelay = 0, strafeDuringDelay = false, healthRange = { 10, 30 } }, --\u{c8fd}\u{c74c}\u{c758}\u{c18d}\u{bc15}";

/// Unrelated row that must never be touched.
const SENTINEL: &str = "alertDuration = 0";

fn seeded(rows: &[(u32, &str)]) -> Connection {
    let c = Connection::open_in_memory().expect("in-memory sqlite");
    c.execute_batch("CREATE TABLE npc_ai_params (id INTEGER PRIMARY KEY, ai_param TEXT);")
        .expect("schema");
    for (id, text) in rows {
        c.execute(
            "INSERT INTO npc_ai_params (id, ai_param) VALUES (?1, ?2)",
            rusqlite::params![id, text],
        )
        .expect("seed row");
    }
    c
}

fn read(c: &Connection, id: u32) -> String {
    c.query_row(
        "SELECT ai_param FROM npc_ai_params WHERE id = ?1",
        [id],
        |r| r.get(0),
    )
    .expect("row present")
}

#[test]
fn patch_repairs_the_three_verified_rows_and_is_idempotent() {
    let c = seeded(&[
        (443, ORIG_443),
        (835, ORIG_835),
        (1975, ORIG_1975),
        (9001, SENTINEL),
    ]);
    let before = [read(&c, 443), read(&c, 835), read(&c, 1975)];
    // The loader's typed parsers reject the original rows (the startup warnings).
    assert!(AiParams::create_by_type(Some(AiParamType::ArcherRoaming), &before[0]).is_err());
    assert!(AiParams::create_by_type(Some(AiParamType::Flytrap), &before[1]).is_err());
    assert!(
        AiParams::create_by_type(Some(AiParamType::BigMonsterHoldPosition), &before[2]).is_err()
    );

    c.execute_batch(PATCH_SQL).expect("patch");

    let after = [read(&c, 443), read(&c, 835), read(&c, 1975)];
    // Exactly the reviewed punctuation edits: all authored keys, numbers and comments stay.
    assert_eq!(after[0], REPAIRED_443);
    assert_eq!(
        after[1],
        before[1].replace("melee = { [2,12279] },", "melee = { 2, 12279 },")
    );
    assert_eq!(after[2], format!("{}\r\n}}", before[2]));
    assert_eq!(read(&c, 9001), SENTINEL);

    let archer = AiParams::create_by_type(Some(AiParamType::ArcherRoaming), &after[0])
        .expect("repaired 443 parses")
        .expect("archer params");
    assert_eq!(archer.alert_duration, 10.0);
    assert_eq!(archer.alert_safe_target_remember_time, 5.0);
    assert_eq!(archer.melee_attack_range, 4.0);
    assert_eq!(archer.prefered_combat_dist, 15.0);
    assert_eq!(archer.max_make_a_gap_count, 1);
    assert_eq!(
        archer.kind,
        AiParamsKind::Archer {
            combat_skills: vec![ArcherCombatSkill {
                melee: vec![2, 12482],
                make_a_gap: vec![12449],
                ranged_def: vec![15926],
                ranged_strong: vec![11911, 11068],
            }]
        }
    );

    let flytrap = AiParams::create_by_type(Some(AiParamType::Flytrap), &after[1])
        .expect("repaired 835 parses")
        .expect("flytrap params");
    assert_eq!(flytrap.melee_attack_range, 5.0);
    assert_eq!(
        flytrap.kind,
        AiParamsKind::Flytrap {
            attack_end_distance: 25.0,
            combat_skills: FlytrapCombatSkill {
                melee: vec![2, 12279],
                ranged: Vec::new(),
            }
        }
    );

    let big = AiParams::create_by_type(Some(AiParamType::BigMonsterHoldPosition), &after[2])
        .expect("repaired 1975 parses")
        .expect("big monster params");
    assert_eq!(big.alert_duration, 3.0);
    assert_eq!(big.alert_safe_target_remember_time, 15.0);
    assert_eq!(
        big.kind,
        AiParamsKind::BigMonster {
            combat_skills: vec![
                BigMonsterCombatSkill {
                    skill_type: 17341,
                    skill_delay: 0.0,
                    strafe_during_delay: false,
                    health_range_min: 0,
                    health_range_max: 90,
                },
                BigMonsterCombatSkill {
                    skill_type: 15189,
                    skill_delay: 0.0,
                    strafe_during_delay: false,
                    health_range_min: 70,
                    health_range_max: 90,
                },
                BigMonsterCombatSkill {
                    skill_type: 15189,
                    skill_delay: 0.0,
                    strafe_during_delay: false,
                    health_range_min: 30,
                    health_range_max: 50,
                },
                BigMonsterCombatSkill {
                    skill_type: 15054,
                    skill_delay: 0.0,
                    strafe_during_delay: false,
                    health_range_min: 50,
                    health_range_max: 70,
                },
                BigMonsterCombatSkill {
                    skill_type: 15054,
                    skill_delay: 0.0,
                    strafe_during_delay: false,
                    health_range_min: 10,
                    health_range_max: 30,
                },
            ]
        }
    );

    // A second execution changes nothing: the exact-original guards no longer match.
    c.execute_batch(PATCH_SQL).expect("patch rerun");
    assert_eq!([read(&c, 443), read(&c, 835), read(&c, 1975)], after);
    assert_eq!(read(&c, 9001), SENTINEL);
}

#[test]
fn patch_guards_leave_customized_rows_untouched() {
    let custom_443 =
        "alertDuration = 20,\r\ncombatSkills = {\r\n  melee = { 2 }\r\n},\r\npreferedCombatDist = 20";
    let already_repaired_835 = ORIG_835.replace("melee = { [2,12279] },", "melee = { 2, 12279 },");
    let custom_1975 =
        "combatSkills = {\r\n  { skillType = 1, skillDelay = 0, healthRange = { 0, 100 } }\r\n}";
    let c = seeded(&[
        (443, custom_443),
        (835, &already_repaired_835),
        (1975, custom_1975),
        // The verified original under a different id must not match either.
        (7443, ORIG_443),
        (9001, SENTINEL),
    ]);
    let ids = [443, 835, 1975, 7443, 9001];
    let before: Vec<String> = ids.iter().map(|id| read(&c, *id)).collect();

    c.execute_batch(PATCH_SQL).expect("patch");

    let after: Vec<String> = ids.iter().map(|id| read(&c, *id)).collect();
    assert_eq!(after, before);
    let count: i64 = c
        .query_row("SELECT COUNT(*) FROM npc_ai_params", [], |r| r.get(0))
        .unwrap();
    assert_eq!(count, 5);
}
