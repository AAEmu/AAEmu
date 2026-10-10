//! Port of AAEmu.UnitTests/Game/Core/Managers/ExperienceManagerTests.cs
//!
//! `ExperienceManager` is [`ExperienceTable`]; the mocked `IExperienceLevelTemplateLoader` is
//! the row list handed to [`ExperienceTable::load`]. `ArgumentOutOfRangeException` is an
//! [`ExperienceError`].

use std::cell::Cell;

use super::*;

const MAX_PLAYER_EXP: i32 = 200;
const MAX_MATE_EXP: i32 = 100;

fn tpl(level: u8, total_exp: i32, total_mate_exp: i32, skill_points: i32) -> LevelTemplate {
    LevelTemplate {
        level,
        total_exp,
        total_mate_exp,
        skill_points,
    }
}

/// `Setup()`: three levels, caps = row count.
fn cut() -> ExperienceTable {
    let rows = vec![tpl(1, 0, 0, 1), tpl(2, 100, 50, 2), tpl(3, 200, 100, 3)];
    let n = rows.len() as u8;
    let mut t = ExperienceTable::default();
    t.load(rows, n, n).unwrap();
    t
}

/// A loader that counts its `Load()` calls.
struct Loader {
    rows: Vec<LevelTemplate>,
    calls: Cell<u32>,
}

impl Loader {
    fn new(rows: Vec<LevelTemplate>) -> Self {
        Self {
            rows,
            calls: Cell::new(0),
        }
    }
    fn load(&self) -> Vec<LevelTemplate> {
        self.calls.set(self.calls.get() + 1);
        self.rows.clone()
    }
}

#[test]
fn max_player_level_greater_than_zero() {
    assert!(cut().max_player_level() > 0);
}

#[test]
fn max_mate_level_greater_than_zero() {
    assert!(cut().max_mate_level() > 0);
}

#[test]
fn get_exp_for_level() {
    let t = cut();
    for (level, mate, expected) in [
        // character
        (0, false, 0), // level 0 is invalid
        (1, false, 0),
        (2, false, 100),
        (3, false, 200),
        (4, false, 0), // level 4 is invalid
        // mate
        (0, true, 0),
        (1, true, 0),
        (2, true, 50),
        (3, true, 100),
        (4, true, 0),
    ] {
        assert_eq!(
            t.exp_for_level(level, mate),
            expected,
            "level {level} mate {mate}"
        );
    }
}

#[test]
fn get_level_from_exp_invalid_too_small_throws() {
    let t = cut();
    for exp in [-1, i32::MIN] {
        assert_eq!(
            t.level_from_exp_any(exp, false),
            Err(ExperienceError::NegativeExp(exp))
        );
    }
}

#[test]
fn get_level_from_exp_invalid_too_big_returns_max_level() {
    let t = cut();
    for (exp, mate) in [
        (201, false),
        (300, false),
        (i32::MAX, false),
        (101, true),
        (300, true),
        (i32::MAX, true),
    ] {
        let (level, overflow) = t.level_from_exp_any(exp, mate).unwrap();
        let expected_level = if mate {
            t.max_mate_level()
        } else {
            t.max_player_level()
        };
        let expected_overflow = exp - if mate { MAX_MATE_EXP } else { MAX_PLAYER_EXP };
        assert_eq!(level, expected_level, "exp {exp} mate {mate}");
        assert_eq!(overflow, expected_overflow, "exp {exp} mate {mate}");
    }
}

#[test]
fn get_level_from_exp_valid() {
    let t = cut();
    for (exp, mate, expected_level, expected_overflow) in [
        // character
        (0, false, 1, 0),
        (1, false, 1, 1),
        (99, false, 1, 99),
        (100, false, 2, 0),
        (101, false, 2, 1),
        (199, false, 2, 99),
        (200, false, 3, 0),
        // mate
        (0, true, 1, 0),
        (1, true, 1, 1),
        (49, true, 1, 49),
        (50, true, 2, 0),
        (51, true, 2, 1),
        (99, true, 2, 49),
        (100, true, 3, 0),
    ] {
        assert_eq!(
            t.level_from_exp_any(exp, mate).unwrap(),
            (expected_level, expected_overflow),
            "exp {exp} mate {mate}"
        );
    }
}

#[test]
fn get_level_from_exp_current_level_zero_throws() {
    assert_eq!(
        cut().level_from_exp(1, 0, false),
        Err(ExperienceError::ZeroLevel)
    );
}

#[test]
fn get_level_from_exp_current_level_invalid_too_small_throws() {
    let t = cut();
    for (exp, current_level) in [(-1, 1), (i32::MIN, 1)] {
        assert_eq!(
            t.level_from_exp(exp, current_level, false),
            Err(ExperienceError::NegativeExp(exp))
        );
    }
}

#[test]
fn get_level_from_exp_current_level_invalid_too_big_returns_max_level() {
    let t = cut();
    for (exp, current_level, mate, expected_overflow) in [
        // characters
        (201, 1, false, 1),
        (300, 1, false, 100),
        (i32::MAX, 1, false, i32::MAX - MAX_PLAYER_EXP),
        (201, 2, false, 1),
        (300, 2, false, 100),
        (i32::MAX, 2, false, i32::MAX - MAX_PLAYER_EXP),
        (201, 3, false, 1),
        (300, 3, false, 100),
        (i32::MAX, 3, false, i32::MAX - MAX_PLAYER_EXP),
        // mates
        (101, 1, true, 1),
        (300, 1, true, 200),
        (i32::MAX, 1, true, i32::MAX - MAX_MATE_EXP),
        (101, 2, true, 1),
        (300, 2, true, 200),
        (i32::MAX, 2, true, i32::MAX - MAX_MATE_EXP),
        (101, 3, true, 1),
        (300, 3, true, 200),
        (i32::MAX, 3, true, i32::MAX - MAX_MATE_EXP),
    ] {
        let (level, overflow) = t.level_from_exp(exp, current_level, mate).unwrap();
        let expected_level = if mate {
            t.max_mate_level()
        } else {
            t.max_player_level()
        };
        assert_eq!(
            level, expected_level,
            "exp {exp} cur {current_level} mate {mate}"
        );
        assert_eq!(
            overflow, expected_overflow,
            "exp {exp} cur {current_level} mate {mate}"
        );
    }
}

#[test]
fn get_level_from_exp_current_level_valid() {
    let t = cut();
    for (exp, mate, current_level, expected_level) in [
        // character
        (0, false, 1, 1),
        (1, false, 1, 1),
        (99, false, 1, 1),
        (100, false, 1, 2),
        (101, false, 1, 2),
        (199, false, 1, 2),
        (200, false, 1, 3),
        // level 2 with less exp stays level 2 (never checks below the current level)
        (0, false, 2, 2),
        (1, false, 2, 2),
        (99, false, 2, 2),
        (100, false, 2, 2),
        (101, false, 2, 2),
        (199, false, 2, 2),
        (200, false, 2, 3),
        // mate
        (0, true, 1, 1),
        (1, true, 1, 1),
        (49, true, 1, 1),
        (50, true, 1, 2),
        (51, true, 1, 2),
        (99, true, 1, 2),
        (100, true, 1, 3),
        (0, true, 2, 2),
        (1, true, 2, 2),
        (49, true, 2, 2),
        (50, true, 2, 2),
        (51, true, 2, 2),
        (99, true, 2, 2),
        (100, true, 2, 3),
    ] {
        let (level, _) = t.level_from_exp(exp, current_level, mate).unwrap();
        assert_eq!(
            level, expected_level,
            "exp {exp} mate {mate} cur {current_level}"
        );
    }
}

#[test]
fn get_exp_needed_to_given_level_valid() {
    let t = cut();
    for (current_exp, target_level, mate, expected) in [
        // characters
        (0, 1, false, 0),
        (5, 1, false, 0),
        (500, 1, false, 0),
        (0, 2, false, 100),
        (0, 3, false, 200),
        // mates
        (0, 1, true, 0),
        (0, 2, true, 50),
        (0, 3, true, 100),
    ] {
        assert_eq!(
            t.exp_needed_to_given_level(current_exp, target_level, mate)
                .unwrap(),
            expected,
            "exp {current_exp} target {target_level} mate {mate}"
        );
    }
}

#[test]
fn get_exp_needed_to_given_level_invalid_negative_exp_throws() {
    let t = cut();
    for mate in [false, true] {
        assert_eq!(
            t.exp_needed_to_given_level(-1, 1, mate),
            Err(ExperienceError::NegativeExp(-1))
        );
    }
}

#[test]
fn get_exp_needed_to_given_level_invalid_target_level_too_high() {
    let t = cut();
    for mate in [false, true] {
        assert_eq!(t.exp_needed_to_given_level(0, 100, mate).unwrap(), 0);
    }
}

#[test]
fn get_skill_points_for_level_valid() {
    let t = cut();
    for (level, expected) in [(1, 1), (2, 2), (3, 3)] {
        assert_eq!(t.skill_points_for_level(level), expected);
    }
}

#[test]
fn get_skill_points_for_level_invalid_returns_zero() {
    let t = cut();
    for level in [4, 50, u8::MAX] {
        assert_eq!(t.skill_points_for_level(level), 0);
    }
}

#[test]
fn load_calls_loader() {
    let mut t = cut();
    let loader = Loader::new(Vec::new());

    t.load(loader.load(), 1, 1).unwrap();

    assert_eq!(loader.calls.get(), 1);
}

#[test]
fn load_resets_state() {
    let mut t = cut();
    let loader1 = Loader::new(vec![tpl(1, 0, 0, 0), tpl(2, 100, 100, 0)]);
    let loader2 = Loader::new(vec![tpl(1, 0, 0, 0)]);
    t.load(loader1.load(), 2, 2).unwrap();

    t.load(loader2.load(), 1, 1).unwrap();
    let (level, _) = t.level_from_exp_any(100, false).unwrap();

    // level 1: the second loader overwrote the first
    assert_eq!(level, 1);
    assert_eq!(t.max_player_level(), 1);
    assert_eq!(t.max_mate_level(), 1);
    assert_eq!(loader1.calls.get(), 1);
    assert_eq!(loader2.calls.get(), 1);
}

#[test]
fn load_sets_max_level() {
    for (player_cap, mate_cap, expected_player, expected_mate) in [
        (1, 1, 1, 1),
        (2, 2, 2, 2),
        (3, 3, 2, 2),
        (1, 2, 1, 2),
        (2, 1, 2, 1),
        (3, 1, 2, 1),
    ] {
        let mut t = cut();
        let loader = Loader::new(vec![tpl(1, 0, 0, 0), tpl(2, 100, 100, 0)]);

        t.load(loader.load(), player_cap, mate_cap).unwrap();

        assert_eq!(loader.calls.get(), 1);
        assert_eq!(
            t.max_player_level(),
            expected_player,
            "caps {player_cap}/{mate_cap}"
        );
        assert_eq!(
            t.max_mate_level(),
            expected_mate,
            "caps {player_cap}/{mate_cap}"
        );
    }
}
